using System;
using System.Collections.Generic;
using System.Diagnostics;
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
   private readonly SemaphoreSlim _jobRunnerLock;
   
   private JobConfiguration Configuration => _options.Value;
   
   public JobRunnerService(IServiceProvider services, IJobStorage jobStorage, IOptions<JobConfiguration> options)
   {
      _services = services;
      _jobStorage = jobStorage;
      _options = options;
      _jobRunnerLock = new SemaphoreSlim(Configuration.MaxConcurrentJobs, Configuration.MaxConcurrentJobs);
   }
   
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      var runningTasks = new List<Task>();
      
      // Start job runner threads
      while (!stoppingToken.IsCancellationRequested && _jobRunnerLock.CurrentCount > 0)
      {
         runningTasks.Add(PerformAvailableJobsAsync(stoppingToken));
      }

      // Wait for all running threads to complete before exiting.
      await Task.WhenAll(runningTasks);
   }

   private async Task PerformAvailableJobsAsync(CancellationToken cancellationToken)
   {
      await _jobRunnerLock.WaitAsync(cancellationToken);

      try
      {
         while (!cancellationToken.IsCancellationRequested)
         {
            try
            {
               await PerformNextJobAsync(cancellationToken);
            }
            catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
            {
               // Ignore cancellation exceptions; they are expected when the service is stopped.
            }
            catch (Exception e)
            {
               Log.Error(e, "Error while performing available jobs");
            }
         }
      }
      finally
      {
         _jobRunnerLock.Release();
      }
   }

   private async Task PerformNextJobAsync(CancellationToken cancellationToken)
   {
      var jobBusItem = await _jobStorage.GetNextJobAsync(cancellationToken);
      if (jobBusItem is null)
      {
         await Task.Delay(1, cancellationToken);
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
         if(jobBusItem.CronExpression is not null)
            await ScheduleNextOccurrence(jobBusItem, cancellationToken);
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

         Log.Information("Finished job {JobType} with parameters {@Parameters} in {Duration}", jobBusItem.JobType.Name, jobBusItem.Parameters, Stopwatch.GetElapsedTime(startTime));
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while running job {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Namespace, jobBusItem.Parameters);
         await job.OnJobFailedAsync(jobBusItem.Parameters, e, cancellationToken);
      }
   }

   private async Task ScheduleNextOccurrence(JobStoreItem jobItem, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(jobItem.CronExpression);

      var nextOccurrence = jobItem.CronExpression.GetNextOccurrence(DateTime.UtcNow);
      if(nextOccurrence is null)
         throw new InvalidOperationException("CRON expression does not have a next occurrence.");

      await _jobStorage.QueueJobAsync(jobItem, nextOccurrence.Value, ct);
   }
}