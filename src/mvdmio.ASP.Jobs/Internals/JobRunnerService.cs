using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
///    Runs multiple worker threads to execute jobs concurrently.
/// </summary>
internal sealed class JobRunnerService : BackgroundService
{
   // OpenTelemetry tracing setup
   private static readonly ActivitySource ActivitySource = new("mvdmio.ASP.Jobs");
   
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
         _logger.LogInformation("Starting job runner service on {ThreadCount} threads", Configuration.JobRunnerThreadsCount);
         
         // Retrieve scoped services
         await using var scope = _services.CreateAsyncScope();
         _jobStorage = scope.ServiceProvider.GetRequiredService<IJobStorage>();
         
         // Run job threads
         var jobRunnerThreads = Enumerable.Range(0, Configuration.JobRunnerThreadsCount).Select(_ => PerformAvailableJobsAsync(stoppingToken));
         await Task.WhenAll(jobRunnerThreads);
         
         _logger.LogInformation("Shutting down job runner service");
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "Error while executing job runner threads");
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
            _logger.LogError(ex, "Error while performing available jobs");
         }
      }
   }

   private async Task PerformNextJobAsync(CancellationToken cancellationToken)
   {
      try
      {
         var jobBusItem = await _jobStorage.WaitForNextJobAsync(cancellationToken);
         
         if (jobBusItem is null)
            return;

         await PerformJob(jobBusItem, cancellationToken);
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
      }
   }

   private async Task PerformJob(JobStoreItem jobBusItem, CancellationToken cancellationToken)
   {
      var startTime = Stopwatch.GetTimestamp();

      await using var scope = _services.CreateAsyncScope();
      var job = (IJob)scope.ServiceProvider.GetRequiredService(jobBusItem.JobType);
      
      // OpenTelemetry tracing
      using var activity = ActivitySource.StartActivity();

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
}