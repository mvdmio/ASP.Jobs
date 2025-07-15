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

internal sealed class JobRunnerService : BackgroundService
{
   // OpenTelemetry tracing setup
   private static readonly ActivitySource _activitySource = new("mvdmio.ASP.Jobs");
   
   private readonly IJobStorage _jobStorage;
   private readonly IOptions<JobConfiguration> _options;
   private readonly IServiceProvider _services;

   private ThreadLimitedTaskScheduler _scheduler = null!;
   
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
         // Make sure the jobs are run on a custom task scheduler that limits the number of threads but still allows for parallel task execution while threads are waiting on async I/O.
         _scheduler = new ThreadLimitedTaskScheduler(Configuration.JobRunnerThreadsCount);
         SynchronizationContext.SetSynchronizationContext(new JobSchedulerSynchronizationContext(_scheduler));

         await RunOnJobScheduler(() => PerformAvailableJobsAsync(stoppingToken), stoppingToken);
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
         // Dispose the task scheduler to ensure all threads are stopped gracefully.
         _scheduler.Dispose();
         SynchronizationContext.SetSynchronizationContext(oldContext);
      }
   }

   private async Task PerformAvailableJobsAsync(CancellationToken cancellationToken)
   {
      while (!cancellationToken.IsCancellationRequested)
      {
         try
         {
            await RunOnJobScheduler(() => PerformNextJobAsync(cancellationToken), cancellationToken);
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
      var jobBusItem = await _jobStorage.WaitForNextJobAsync(ct: cancellationToken);
      if (jobBusItem is null)
         return;

      // Do not await this task, otherwise only one job will run at a time.
      // The TaskScheduler will handle concurrency and ensure all tasks are completed before the service stops.
      _ = RunOnJobScheduler(() => PerformJob(jobBusItem, cancellationToken), cancellationToken);
   }

   private async Task PerformJob(JobStoreItem jobBusItem, CancellationToken cancellationToken)
   {
      var startTime = Stopwatch.GetTimestamp();

      using var scope = _services.CreateScope();
      var job = (IJob)scope.ServiceProvider.GetRequiredService(jobBusItem.JobType);
      
      // OpenTelemetry tracing
      using var activity = _activitySource.StartActivity();

      if (activity is not null)
         activity.DisplayName = $"Job: {jobBusItem.JobType.Name}";

      activity?.SetTag("job.type", jobBusItem.JobType.AssemblyQualifiedName);
      activity?.SetTag("job.name", jobBusItem.Options.JobName);
      activity?.SetTag("job.group", jobBusItem.Options.Group);
      activity?.SetTag("job.parameters", jobBusItem.Parameters);
      activity?.SetTag("job.cron", jobBusItem.CronExpression?.ToString());

      try
      {
         Log.Information("Running job: {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Name, jobBusItem.Parameters);
         activity?.AddEvent(new ActivityEvent("Job Started"));

         await job.ExecuteAsync(jobBusItem.Parameters, cancellationToken);
         await job.OnJobExecutedAsync(jobBusItem.Parameters, cancellationToken);

         activity?.AddEvent(new ActivityEvent("Job Completed"));
         activity?.SetStatus(ActivityStatusCode.Ok, "Job completed successfully");

         var endTime = Stopwatch.GetTimestamp();
         var duration = new TimeSpan(endTime - startTime);
         Log.Information("Finished job {JobType} with parameters {@Parameters} in {Duration}", jobBusItem.JobType.Name, jobBusItem.Parameters, duration);
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
         activity?.AddEvent(new ActivityEvent("Job Canceled"));
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while running job {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Name, jobBusItem.Parameters);

         activity?.AddException(e);
         activity?.SetStatus(ActivityStatusCode.Error, "Job failed with exception");

         await job.OnJobFailedAsync(jobBusItem.Parameters, e, cancellationToken);
      }
      finally
      {
         try
         {
            await _jobStorage.FinalizeJobAsync(jobBusItem, cancellationToken);

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
         Options = jobItem.Options,
         CronExpression = jobItem.CronExpression
      };

      await _jobStorage.ScheduleJobAsync(newJobItem, ct);
   }

   private Task RunOnJobScheduler(Func<Task> asyncAction, CancellationToken ct)
   {
      return Task.Factory.StartNew(async () => await asyncAction.Invoke(), ct, TaskCreationOptions.None, _scheduler);
   }
}