using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using Serilog;

namespace mvdmio.ASP.Jobs.Internals;

internal class JobRunnerService : BackgroundService
{
   private readonly IServiceProvider _services;
   private readonly IJobStorage _jobStorage;
   private readonly IOptions<JobConfiguration> _options;

   private JobConfiguration Configuration => _options.Value;

   public JobRunnerService(IServiceProvider services, IJobStorage jobStorage, IOptions<JobConfiguration> options)
   {
      _services = services;
      _jobStorage = jobStorage;
      _options = options;
   }

   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      var runningTasks = Enumerable.Range(0, Configuration.MaxConcurrentJobs).Select(_ => PerformAvailableJobsAsync(stoppingToken));

      try
      {
         // Wait for all running threads to complete before exiting.
         await Task.WhenAll(runningTasks);
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error while executing job runner threads");
      }
   }

   private async Task PerformAvailableJobsAsync(CancellationToken cancellationToken)
   {
      while (!cancellationToken.IsCancellationRequested)
      {
         try
         {
            await PerformNextJobAsync(cancellationToken);
         }
         catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
         {
            // Ignore cancellation exceptions; they are expected when the service is stopped.
         }
         catch (Exception ex)
         {
            Log.Error(ex, "Error while performing available jobs");
         }
      }
   }

   private async Task PerformNextJobAsync(CancellationToken cancellationToken)
   {
      var jobBusItem = await _jobStorage.StartNextJobAsync(cancellationToken);
      if (jobBusItem is null)
      {
         await Task.Delay(100, cancellationToken);
         return;
      }

      try
      {
         using var scope = _services.CreateScope();
         var job = (IJob)scope.ServiceProvider.GetRequiredService(jobBusItem.JobType);

         await PerformJob(job, jobBusItem, cancellationToken);
      }
      finally
      {
         try
         {
            await _jobStorage.FinalizeJobAsync(jobBusItem.Options.JobId, cancellationToken);

            if (jobBusItem.CronExpression is not null)
               await ScheduleNextOccurrence(jobBusItem, cancellationToken);
         }
         catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
         {
            // Ignore cancellation exceptions; they are expected when the service is stopped.
         }
      }
   }

   private static async Task PerformJob(IJob job, JobStoreItem jobBusItem, CancellationToken cancellationToken)
   {
      var startTime = Stopwatch.GetTimestamp();

      try
      {
         Log.Information("Running job: {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Name, jobBusItem.Parameters);

         await job.ExecuteAsync(jobBusItem.Parameters, cancellationToken);
         await job.OnJobExecutedAsync(jobBusItem.Parameters, cancellationToken);

         var endTime = Stopwatch.GetTimestamp();
         var duration = new TimeSpan(endTime - startTime);
         Log.Information("Finished job {JobType} with parameters {@Parameters} in {Duration}", jobBusItem.JobType.Name, jobBusItem.Parameters, duration);
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while running job {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Namespace, jobBusItem.Parameters);
         await job.OnJobFailedAsync(jobBusItem.Parameters, e, cancellationToken);
      }
   }

   private async Task ScheduleNextOccurrence(JobStoreItem jobItem, CancellationToken ct = default)
   {
      if (jobItem.CronExpression is null)
         throw new ArgumentNullException(nameof(jobItem.CronExpression));

      var nextOccurrence = jobItem.CronExpression.GetNextOccurrence(DateTime.UtcNow);
      if (nextOccurrence is null)
         throw new InvalidOperationException("CRON expression does not have a next occurrence.");

      var newJobItem = new JobStoreItem {
         JobType = jobItem.JobType,
         PerformAt = nextOccurrence.Value,
         Parameters = jobItem.Parameters,
         Options = jobItem.Options
      };

      await _jobStorage.ScheduleJobAsync(newJobItem, ct);
   }
}