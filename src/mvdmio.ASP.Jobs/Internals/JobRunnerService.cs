using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Utils;

namespace mvdmio.ASP.Jobs.Internals;

/// <summary>
///    Background service that continuously processes scheduled jobs.
///    Uses a producer-consumer pattern with channels for efficient async job execution.
/// </summary>
internal sealed class JobRunnerService : BackgroundService
{
   // OpenTelemetry tracing setup
   private static readonly ActivitySource _openTelemetry = new("mvdmio.ASP.Jobs");

   private readonly IOptions<JobRunnerOptions> _options;
   private readonly ILogger<JobRunnerService> _logger;
   private readonly IServiceProvider _services;
   private readonly IClock _clock;

   private IJobStorage _jobStorage = null!;

   private JobRunnerOptions Configuration => _options.Value;

   /// <summary>
   ///    Initializes a new instance of the <see cref="JobRunnerService"/> class.
   /// </summary>
   /// <param name="services">The service provider for resolving job instances.</param>
   /// <param name="options">The job runner configuration options.</param>
   /// <param name="logger">The logger for job runner operations.</param>
   /// <param name="clock">The clock used to compute retry attempt times.</param>
   public JobRunnerService(IServiceProvider services, IOptions<JobRunnerOptions> options, ILogger<JobRunnerService> logger, IClock clock)
   {
      _services = services;
      _options = options;
      _logger = logger;
      _clock = clock;
   }

   /// <inheritdoc />
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      try
      {
         _logger.LogInformation(
            "Starting job runner service with max {MaxConcurrent} concurrent jobs and channel capacity {ChannelCapacity}", 
            Configuration.MaxConcurrentJobs,
            Configuration.JobChannelCapacity);
         
         // Retrieve scoped services
         await using var scope = _services.CreateAsyncScope();
         _jobStorage = scope.ServiceProvider.GetRequiredService<IJobStorage>();
         
         // Create bounded channel for job items
         var channel = Channel.CreateBounded<JobStoreItem>(
            new BoundedChannelOptions(Configuration.JobChannelCapacity)
            {
               FullMode = BoundedChannelFullMode.Wait,
               SingleReader = false,
               SingleWriter = true
            });
         
         // Create semaphore to limit concurrent job execution
         var concurrencySemaphore = new SemaphoreSlim(Configuration.MaxConcurrentJobs);
         
         // Start producer and consumer tasks
         var producerTask = ProduceJobsAsync(channel.Writer, stoppingToken);
         var consumerTask = ConsumeJobsAsync(channel.Reader, concurrencySemaphore, stoppingToken);
         
         await Task.WhenAll(producerTask, consumerTask);
         
         _logger.LogInformation("Shutting down job runner service");
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "Error while executing job runner service");
      }
   }

   /// <summary>
   ///    Producer: Continuously fetches jobs from storage and writes them to the channel.
   /// </summary>
   private async Task ProduceJobsAsync(ChannelWriter<JobStoreItem> writer, CancellationToken ct)
   {
      try
      {
         while (!ct.IsCancellationRequested)
         {
            try
            {
               var job = await _jobStorage.WaitForNextJobAsync(ct);
               
               if (job is null)
                  continue;
               
               await writer.WriteAsync(job, ct);
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
            {
               // Expected during shutdown
               break;
            }
            catch (Exception ex)
            {
               _logger.LogError(ex, "Error while fetching next job from storage");
            }
         }
      }
      finally
      {
         writer.Complete();
      }
   }

   /// <summary>
   ///    Consumer: Reads jobs from the channel and executes them concurrently,
   ///    limited by the semaphore.
   /// </summary>
   private async Task ConsumeJobsAsync(
      ChannelReader<JobStoreItem> reader, 
      SemaphoreSlim semaphore, 
      CancellationToken ct)
   {
      // Track all running job tasks for graceful shutdown
      var runningJobs = new List<Task>();
      
      try
      {
         await foreach (var job in reader.ReadAllAsync(ct))
         {
            // Wait for a slot to become available
            await semaphore.WaitAsync(ct);
            
            // Fire off the job execution (don't await - let it run concurrently)
            var jobTask = ExecuteJobWithSemaphoreAsync(job, semaphore, ct);
            runningJobs.Add(jobTask);
            
            // Periodically clean up completed tasks to avoid memory growth
            runningJobs.RemoveAll(t => t.IsCompleted);
         }
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Expected during shutdown
      }
      finally
      {
         // Wait for all running jobs to complete on shutdown
         if (runningJobs.Count > 0)
         {
            _logger.LogInformation("Waiting for {Count} running jobs to complete before shutdown", runningJobs.Count);
            await Task.WhenAll(runningJobs);
         }
      }
   }

   /// <summary>
   ///    Executes a job and ensures the semaphore is released when done.
   /// </summary>
   private async Task ExecuteJobWithSemaphoreAsync(
      JobStoreItem job, 
      SemaphoreSlim semaphore, 
      CancellationToken ct)
   {
      try
      {
         await PerformJob(job, ct);
      }
      finally
      {
         semaphore.Release();
      }
   }

   private async Task PerformJob(JobStoreItem jobBusItem, CancellationToken cancellationToken)
   {
      var startTime = Stopwatch.GetTimestamp();

      await using var scope = _services.CreateAsyncScope();
      var job = (IJob)scope.ServiceProvider.GetRequiredService(jobBusItem.JobType);

      // OpenTelemetry tracing
      using var activity = _openTelemetry.StartActivity();

      if (activity is not null)
         activity.DisplayName = $"Job: {jobBusItem.JobType.Name}";

      activity?.SetTag("job.type", jobBusItem.JobType.AssemblyQualifiedName);
      activity?.SetTag("job.name", jobBusItem.Options.JobName);
      activity?.SetTag("job.group", jobBusItem.Options.Group);
      activity?.SetTag("job.parameters", jobBusItem.Parameters);
      activity?.SetTag("job.cron", jobBusItem.CronExpression?.ToString());
      activity?.SetTag("job.attempt", jobBusItem.Attempt);

      Exception? executionException = null;
      var wasCanceled = false;

      // Save the thread's culture so we can restore it after the job runs; jobs run on shared thread-pool
      // threads, so without a restore one job's Captured Culture would leak into the next job on that thread.
      var originalCulture = CultureInfo.CurrentCulture;
      var originalUICulture = CultureInfo.CurrentUICulture;

      try
      {
         // Resolving/applying the Captured Culture happens inside the try on purpose: an unresolvable culture
         // then rides the normal job-failure path (logged + OnJobFailedAsync) instead of being lost on this
         // fire-and-forget task.
         ApplyCapturedCulture(jobBusItem);

         _logger.LogInformation("Running job: {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Name, jobBusItem.Parameters);
         activity?.AddEvent(new ActivityEvent("Job Started"));

         await job.ExecuteAsync(jobBusItem.Parameters, cancellationToken);

         activity?.AddEvent(new ActivityEvent("Job Completed"));
         activity?.SetStatus(ActivityStatusCode.Ok, "Job completed successfully");

         var endTime = Stopwatch.GetTimestamp();
         var duration = new TimeSpan(endTime - startTime);
         _logger.LogInformation("Finished job {JobType} with parameters {@Parameters} in {Duration}", jobBusItem.JobType.Name, jobBusItem.Parameters, duration);
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
         wasCanceled = true;
         activity?.AddEvent(new ActivityEvent("Job Canceled"));
      }
      catch (Exception e)
      {
         executionException = e;

         _logger.LogError(e, "Error while running job {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Name, jobBusItem.Parameters);

         activity?.AddException(e);
         activity?.SetStatus(ActivityStatusCode.Error, "Job failed with exception");
      }
      finally
      {
         CultureInfo.CurrentCulture = originalCulture;
         CultureInfo.CurrentUICulture = originalUICulture;
      }

      if (wasCanceled)
      {
         // Cancellation during shutdown is not a failure and is not retried; the chain simply ends here.
         await FinalizeChainAsync(jobBusItem, cancellationToken);
         return;
      }

      if (executionException is null)
      {
         // Runner-side hooks are fire-safe observers: a throw is logged but never changes the chain outcome.
         await InvokeHookSafelyAsync(() => job.OnJobExecutedAsync(jobBusItem.Parameters, cancellationToken), nameof(IJob.OnJobExecutedAsync), jobBusItem.JobType);
         await FinalizeChainAsync(jobBusItem, cancellationToken);
         return;
      }

      var matchedBehavior = job.RetryPolicy.FindMatchingBehavior(executionException);

      if (matchedBehavior is not null && jobBusItem.Attempt < matchedBehavior.MaxRetriesValue)
      {
         await RetryJobAsync(jobBusItem, job, executionException, matchedBehavior, activity, cancellationToken);
         return;
      }

      // No matching behavior, or the retry budget is depleted: the chain ends in failure.
      await InvokeHookSafelyAsync(() => job.OnJobFailedAsync(jobBusItem.Parameters, executionException, cancellationToken), nameof(IJob.OnJobFailedAsync), jobBusItem.JobType);
      await FinalizeChainAsync(jobBusItem, cancellationToken);
   }

   private async Task RetryJobAsync(JobStoreItem jobBusItem, IJob job, Exception exception, RetryBehavior matchedBehavior, Activity? activity, CancellationToken cancellationToken)
   {
      var nextAttempt = jobBusItem.Attempt + 1;
      var nextAttemptAtUtc = _clock.UtcNow + matchedBehavior.ComputeDelay(nextAttempt);

      try
      {
         var scheduled = await _jobStorage.TryScheduleRetryAsync(jobBusItem, nextAttemptAtUtc, cancellationToken);

         if (scheduled)
         {
            // Supersession is silent (no hooks fire) per ADR 0003, so OnJobRetryAsync only fires once the retry
            // is confirmed written - a superseded chain never had a "retried attempt" in the first place.
            var retryContext = new RetryContext {
               Attempt = nextAttempt,
               MaxRetries = matchedBehavior.MaxRetriesValue,
               NextAttemptAtUtc = nextAttemptAtUtc
            };

            await InvokeHookSafelyAsync(() => job.OnJobRetryAsync(jobBusItem.Parameters, exception, retryContext, cancellationToken), nameof(IJob.OnJobRetryAsync), jobBusItem.JobType);

            _logger.LogInformation(
               "Retrying job {JobType} (attempt {Attempt}/{MaxRetries}) at {NextAttemptAtUtc} after: {ExceptionMessage}",
               jobBusItem.JobType.Name,
               nextAttempt,
               matchedBehavior.MaxRetriesValue,
               nextAttemptAtUtc,
               exception.Message
            );

            activity?.AddEvent(
               new ActivityEvent(
                  "Job Retry Scheduled",
                  tags: new ActivityTagsCollection {
                     { "retry.attempt", nextAttempt },
                     { "retry.max_retries", matchedBehavior.MaxRetriesValue },
                     { "retry.next_attempt_at_utc", nextAttemptAtUtc.ToString("O") }
                  }
               )
            );
         }
         else
         {
            _logger.LogInformation(
               "Job chain for {JobType} ('{JobName}') was superseded by a newer scheduled job; abandoning the retry.",
               jobBusItem.JobType.Name,
               jobBusItem.Options.JobName
            );

            activity?.AddEvent(new ActivityEvent("Job Chain Superseded"));
         }
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
      }
   }

   /// <summary>
   ///    Ends an Execution Chain: removes the job from storage and, for CRON jobs, schedules the next occurrence.
   ///    Only called when the chain actually ends (success, non-retryable/depleted failure, or cancellation) - never
   ///    for a retry, since the chain continues under the same identity.
   /// </summary>
   private async Task FinalizeChainAsync(JobStoreItem jobBusItem, CancellationToken cancellationToken)
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

   /// <summary>
   ///    Invokes a runner-side lifecycle hook (<see cref="IJob.OnJobExecutedAsync"/>, <see cref="IJob.OnJobFailedAsync"/>,
   ///    <see cref="IJob.OnJobRetryAsync"/>) as a fire-safe observer: a throw is logged and never alters the chain outcome.
   /// </summary>
   private async Task InvokeHookSafelyAsync(Func<Task> hook, string hookName, Type jobType)
   {
      try
      {
         await hook();
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Expected during shutdown.
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "Error while executing {HookName} for job {JobType}", hookName, jobType.Name);
      }
   }

   /// <summary>
   ///    Applies the job's Captured Culture to the current thread. Does nothing when no culture was captured
   ///    (e.g. a job persisted before culture capture existed), leaving the thread's ambient culture untouched.
   ///    Both cultures are resolved before either is assigned, so an unresolvable name never leaves a half-applied culture.
   /// </summary>
   private static void ApplyCapturedCulture(JobStoreItem jobBusItem)
   {
      if (jobBusItem.CultureName is null || jobBusItem.UICultureName is null)
         return;

      var culture = CultureInfo.GetCultureInfo(jobBusItem.CultureName);
      var uiCulture = CultureInfo.GetCultureInfo(jobBusItem.UICultureName);

      CultureInfo.CurrentCulture = culture;
      CultureInfo.CurrentUICulture = uiCulture;
   }

   private async Task ScheduleNextOccurrence(JobStoreItem jobItem, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(jobItem.CronExpression, nameof(jobItem.CronExpression));
      
      var nextOccurrence = jobItem.CronExpression.GetNextOccurrence(DateTime.UtcNow);
      if (nextOccurrence is null)
         throw new InvalidOperationException("CRON expression does not have a next occurrence.");

      var newJobItem = new JobStoreItem {
         JobType = jobItem.JobType,
         PerformAt = nextOccurrence.Value,
         Parameters = jobItem.Parameters,
         Options = jobItem.Options,
         CronExpression = jobItem.CronExpression,
         CultureName = jobItem.CultureName,
         UICultureName = jobItem.UICultureName
      };

      await _jobStorage.ScheduleJobAsync(newJobItem, ct);
   }
}
