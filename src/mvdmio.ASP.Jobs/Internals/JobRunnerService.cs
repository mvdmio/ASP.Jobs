using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

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

   private IJobStorage _jobStorage = null!;
   
   private JobRunnerOptions Configuration => _options.Value;

   /// <summary>
   ///    Initializes a new instance of the <see cref="JobRunnerService"/> class.
   /// </summary>
   /// <param name="services">The service provider for resolving job instances.</param>
   /// <param name="options">The job runner configuration options.</param>
   /// <param name="logger">The logger for job runner operations.</param>
   public JobRunnerService(IServiceProvider services, IOptions<JobRunnerOptions> options, ILogger<JobRunnerService> logger)
   {
      _services = services;
      _options = options;
      _logger = logger;
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

      try
      {
         _logger.LogInformation("Running job: {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Name, jobBusItem.Parameters);
         activity?.AddEvent(new ActivityEvent("Job Started"));

         await job.ExecuteAsync(jobBusItem.Parameters, cancellationToken);
         await job.OnJobExecutedAsync(jobBusItem.Parameters, cancellationToken);

         activity?.AddEvent(new ActivityEvent("Job Completed"));
         activity?.SetStatus(ActivityStatusCode.Ok, "Job completed successfully");

         var endTime = Stopwatch.GetTimestamp();
         var duration = new TimeSpan(endTime - startTime);
         _logger.LogInformation("Finished job {JobType} with parameters {@Parameters} in {Duration}", jobBusItem.JobType.Name, jobBusItem.Parameters, duration);
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
         activity?.AddEvent(new ActivityEvent("Job Canceled"));
      }
      catch (Exception e)
      {
         _logger.LogError(e, "Error while running job {JobType} with parameters: {@Parameters}", jobBusItem.JobType.Name, jobBusItem.Parameters);

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
      ArgumentNullException.ThrowIfNull(jobItem.CronExpression, nameof(jobItem.CronExpression));
      
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
}
