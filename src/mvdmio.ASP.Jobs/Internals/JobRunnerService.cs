using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Tasks;
using Serilog;

namespace mvdmio.ASP.Jobs.Internals;

internal class JobRunnerService : BackgroundService
{
   private readonly IJobStorage _jobStorage;
   private readonly IOptions<JobConfiguration> _options;
   private readonly IServiceProvider _services;

   private JobConfiguration Configuration => _options.Value;

   public JobRunnerService(IServiceProvider services, IJobStorage jobStorage, IOptions<JobConfiguration> options)
   {
      _services = services;
      _jobStorage = jobStorage;
      _options = options;
   }

   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      var oldContext = SynchronizationContext.Current;

      try
      {
         using var scheduler = new JobTaskScheduler(Configuration.JobRunnerThreadsCount);

         SynchronizationContext.SetSynchronizationContext(new JobSchedulerSynchronizationContext(scheduler));

         await StartTask(() => PerformAvailableJobsAsync(scheduler, stoppingToken), scheduler, stoppingToken);
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error while executing job runner threads");
      }
      finally
      {
         SynchronizationContext.SetSynchronizationContext(oldContext);
      }
   }

   private async Task PerformAvailableJobsAsync(JobTaskScheduler scheduler, CancellationToken cancellationToken)
   {
      while (!cancellationToken.IsCancellationRequested)
      {
         try
         {
            await StartTask(() => PerformNextJobAsync(scheduler, cancellationToken), scheduler, cancellationToken);
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

   private async Task PerformNextJobAsync(JobTaskScheduler scheduler, CancellationToken cancellationToken)
   {
      var jobBusItem = await _jobStorage.StartNextJobAsync(cancellationToken);
      if (jobBusItem is null)
      {
         await Task.Delay(100, cancellationToken);
         return;
      }

      // Do not await this task, otherwise only one job will run at a time.
      _ = StartTask(() => PerformJob(jobBusItem, cancellationToken), scheduler, cancellationToken);
   }

   private async Task PerformJob(JobStoreItem jobBusItem, CancellationToken cancellationToken)
   {
      var startTime = Stopwatch.GetTimestamp();

      using var scope = _services.CreateScope();
      var job = (IJob)scope.ServiceProvider.GetRequiredService(jobBusItem.JobType);
      
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
         Log.Error(e, "Error while running job {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Name, jobBusItem.Parameters);
         await job.OnJobFailedAsync(jobBusItem.Parameters, e, cancellationToken);
      }
      finally
      {
         try
         {
            await _jobStorage.FinalizeJobAsync(jobBusItem.Options.JobName, cancellationToken);

            if (jobBusItem.CronExpression is not null)
               await ScheduleNextOccurrence(jobBusItem, cancellationToken);
         }
         catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
         {
            // Ignore cancellation exceptions; they are expected when the service is stopped.
         }
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

   private static Task StartTask(Func<Task> asyncAction, JobTaskScheduler scheduler, CancellationToken ct)
   {
      return Task.Factory.StartNew(async () => await asyncAction.Invoke(), ct, TaskCreationOptions.None, scheduler);
   }
}