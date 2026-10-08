using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Utils;
using Serilog;

namespace mvdmio.ASP.Jobs.Internals;

/// <summary>
///    Internal implementation of <see cref="IJobScheduler"/> that manages scheduling and querying of jobs.
/// </summary>
internal sealed class JobScheduler : IJobScheduler
{
   private readonly IServiceProvider _services;
   private readonly IJobStorage _jobStorage;
   private readonly IClock _clock;
   
   /// <summary>
   ///    Initializes a new instance of the <see cref="JobScheduler"/> class.
   /// </summary>
   /// <param name="services">The service provider for resolving job instances.</param>
   /// <param name="jobStorage">The job storage implementation.</param>
   /// <param name="clock">The clock for time operations.</param>
   public JobScheduler(IServiceProvider services, IJobStorage jobStorage, IClock clock)
   {
      _services = services;
      _jobStorage = jobStorage;
      _clock = clock;
   }

   public async Task PerformNowAsync<TJob, TParameters>(TParameters parameters, CancellationToken ct = default) 
      where TJob : Job<TParameters>
      where TParameters : class
   {
      await using var scope = _services.CreateAsyncScope();
      var job = scope.ServiceProvider.GetRequiredService<TJob>();
      
      try
      {
         await job.OnJobScheduledAsync(parameters, ct);
         await job.ExecuteAsync(parameters, ct);
         await job.OnJobExecutedAsync(parameters, ct);
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
         
         await job.OnJobFailedAsync(parameters, e, ct);
         
         throw;
      }
   }

   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      return ScheduleAsapAsync<TJob, TParameters>(parameters, new JobScheduleOptions(), CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name, ct);
   }

   public async Task PerformAsapAsync<TJob, TParameters>(IEnumerable<TParameters> parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      // Capture the scheduling thread's culture once for the whole batch, before any await.
      var cultureName = CultureInfo.CurrentCulture.Name;
      var uiCultureName = CultureInfo.CurrentUICulture.Name;

      await ScheduleBatchAsync<TJob, TParameters>(parameters, performAtUtc: null, cultureName, uiCultureName, ct);
   }

   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, JobScheduleOptions? options = null, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      return ScheduleAsapAsync<TJob, TParameters>(parameters, options ?? new JobScheduleOptions(), CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name, ct);
   }

   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, CultureInfo culture, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      ArgumentNullException.ThrowIfNull(culture);
      return ScheduleAsapAsync<TJob, TParameters>(parameters, new JobScheduleOptions(), culture.Name, culture.Name, ct);
   }

   public async Task PerformAsapAsync<TJob, TParameters>(IEnumerable<TParameters> parameters, CultureInfo culture, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      ArgumentNullException.ThrowIfNull(culture);

      await ScheduleBatchAsync<TJob, TParameters>(parameters, performAtUtc: null, culture.Name, culture.Name, ct);
   }

   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, JobScheduleOptions options, CultureInfo culture, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      ArgumentNullException.ThrowIfNull(options);
      ArgumentNullException.ThrowIfNull(culture);
      return ScheduleAsapAsync<TJob, TParameters>(parameters, options, culture.Name, culture.Name, ct);
   }

   private async Task ScheduleAsapAsync<TJob, TParameters>(TParameters parameters, JobScheduleOptions options, string cultureName, string uiCultureName, CancellationToken ct)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      try
      {
         await using var scope = _services.CreateAsyncScope();
         var job = scope.ServiceProvider.GetRequiredService<TJob>();

         await job.OnJobScheduledAsync(parameters, ct);
         await _jobStorage.ScheduleJobAsync(
            new JobStoreItem {
               JobType = typeof(TJob),
               PerformAt = _clock.UtcNow,
               Parameters = parameters,
               Options = options,
               CultureName = cultureName,
               UICultureName = uiCultureName
            },
            ct
         );

         LogScheduledAsap<TJob>(parameters);
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
         throw;
      }
   }

   private static void LogScheduledAsap<TJob>(object parameters)
   {
      Log.Information("Scheduled job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
   }

   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      return ScheduleAtAsync<TJob, TParameters>(performAtUtc, parameters, new JobScheduleOptions(), CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name, ct);
   }

   public async Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, IEnumerable<TParameters> parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      // Capture the scheduling thread's culture once for the whole batch, before any await.
      var cultureName = CultureInfo.CurrentCulture.Name;
      var uiCultureName = CultureInfo.CurrentUICulture.Name;

      await ScheduleBatchAsync<TJob, TParameters>(parameters, performAtUtc, cultureName, uiCultureName, ct);
   }

   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, JobScheduleOptions? options = null, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      return ScheduleAtAsync<TJob, TParameters>(performAtUtc, parameters, options ?? new JobScheduleOptions(), CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name, ct);
   }

   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, CultureInfo culture, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      ArgumentNullException.ThrowIfNull(culture);
      return ScheduleAtAsync<TJob, TParameters>(performAtUtc, parameters, new JobScheduleOptions(), culture.Name, culture.Name, ct);
   }

   public async Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, IEnumerable<TParameters> parameters, CultureInfo culture, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      ArgumentNullException.ThrowIfNull(culture);

      await ScheduleBatchAsync<TJob, TParameters>(parameters, performAtUtc, culture.Name, culture.Name, ct);
   }

   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, JobScheduleOptions options, CultureInfo culture, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      ArgumentNullException.ThrowIfNull(options);
      ArgumentNullException.ThrowIfNull(culture);
      return ScheduleAtAsync<TJob, TParameters>(performAtUtc, parameters, options, culture.Name, culture.Name, ct);
   }

   private async Task ScheduleAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, JobScheduleOptions options, string cultureName, string uiCultureName, CancellationToken ct)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      try
      {
         await using var scope = _services.CreateAsyncScope();
         var job = scope.ServiceProvider.GetRequiredService<TJob>();

         await job.OnJobScheduledAsync(parameters, ct);
         await _jobStorage.ScheduleJobAsync(
            new JobStoreItem {
               JobType = typeof(TJob),
               PerformAt = performAtUtc,
               Parameters = parameters,
               Options = options,
               CultureName = cultureName,
               UICultureName = uiCultureName
            },
            ct
         );

         LogScheduledAt<TJob>(parameters, performAtUtc);
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
         throw;
      }
   }

   private static void LogScheduledAt<TJob>(object parameters, DateTime performAtUtc)
   {
      Log.Information("Scheduled Job: {JobType} with parameters: {@Parameters} to run at {Time}", typeof(TJob).Name, parameters, performAtUtc);
   }

   /// <summary>
   ///    Stores a batch of jobs in one storage write, completely or not at all. Each job runs its
   ///    <see cref="Job{TProperties}.OnJobScheduledAsync"/> hook in its own scope and gets its own job name.
   /// </summary>
   /// <param name="parameters">The parameters, one for each job to schedule.</param>
   /// <param name="performAtUtc">The UTC time to run the jobs at, or <c>null</c> to run them as soon as possible.</param>
   /// <param name="cultureName">The formatting culture name of the batch's Captured Culture.</param>
   /// <param name="uiCultureName">The UI culture name of the batch's Captured Culture.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   private async Task ScheduleBatchAsync<TJob, TParameters>(IEnumerable<TParameters> parameters, DateTime? performAtUtc, string cultureName, string uiCultureName, CancellationToken ct)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      var parameterList = parameters.ToList();
      if (parameterList.Any(x => x is null))
         throw new ArgumentNullException(nameof(parameters));

      if (parameterList.Count == 0)
         return;

      try
      {
         var items = new List<JobStoreItem>(parameterList.Count);
         foreach (var parameter in parameterList)
         {
            ct.ThrowIfCancellationRequested();

            await using (var scope = _services.CreateAsyncScope())
            {
               var job = scope.ServiceProvider.GetRequiredService<TJob>();
               await job.OnJobScheduledAsync(parameter, ct);
            }

            items.Add(
               new JobStoreItem {
                  JobType = typeof(TJob),
                  PerformAt = performAtUtc ?? _clock.UtcNow,
                  Parameters = parameter,
                  Options = new JobScheduleOptions(),
                  CultureName = cultureName,
                  UICultureName = uiCultureName
               }
            );
         }

         ct.ThrowIfCancellationRequested();
         await _jobStorage.ScheduleJobsAsync(items, ct);
      }
      // A cancellation the caller asked for is not a failure, so it propagates without an error log.
      catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
      {
         Log.Error(e, "Error while scheduling a batch of {Count} jobs: {JobType}", parameterList.Count, typeof(TJob).Name);
         throw;
      }

      foreach (var parameter in parameterList)
      {
         if (performAtUtc is null)
            LogScheduledAsap<TJob>(parameter);
         else
            LogScheduledAt<TJob>(parameter, performAtUtc.Value);
      }
   }

   public Task PerformCronAsync<TJob, TParameters>(string cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      return PerformCronAsync<TJob, TParameters>(CronExpression.Parse(cronExpression), parameters, runImmediately, ct);
   }

   public Task PerformCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      // CRON jobs default to the invariant culture rather than the scheduling thread's culture, since they are
      // typically registered at application startup where the thread culture is not meaningful.
      return ScheduleCronAsync<TJob, TParameters>(cronExpression, parameters, runImmediately, CultureInfo.InvariantCulture.Name, CultureInfo.InvariantCulture.Name, ct);
   }

   public Task PerformCronAsync<TJob, TParameters>(string cronExpression, TParameters parameters, CultureInfo culture, bool runImmediately = false, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      return PerformCronAsync<TJob, TParameters>(CronExpression.Parse(cronExpression), parameters, culture, runImmediately, ct);
   }

   public Task PerformCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, CultureInfo culture, bool runImmediately = false, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      ArgumentNullException.ThrowIfNull(culture);
      return ScheduleCronAsync<TJob, TParameters>(cronExpression, parameters, runImmediately, culture.Name, culture.Name, ct);
   }

   private async Task ScheduleCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, bool runImmediately, string cultureName, string uiCultureName, CancellationToken ct)
      where TJob : Job<TParameters>
      where TParameters : class
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      try
      {
         await using var scope = _services.CreateAsyncScope();
         var job = scope.ServiceProvider.GetRequiredService<TJob>();

         await job.OnJobScheduledAsync(parameters, ct);

         var normalizedCronExpression = cronExpression.ToString().Replace(" ", string.Empty);
         var scheduleOptions = new JobScheduleOptions {
            JobName = $"cron_{typeof(TJob).Name}_{normalizedCronExpression}" // CRON jobs may not be scheduled twice.
         };

         if (runImmediately)
         {
            var jobItem = new JobStoreItem {
               JobType = typeof(TJob),
               PerformAt = _clock.UtcNow,
               Parameters = parameters,
               Options = scheduleOptions,
               CronExpression = cronExpression,
               CultureName = cultureName,
               UICultureName = uiCultureName
            };

            await _jobStorage.ScheduleJobAsync(jobItem, ct);
         }
         else
         {
            var nextOccurence = cronExpression.GetNextOccurrence(_clock.UtcNow);
            if (nextOccurence is null)
               throw new InvalidOperationException("CRON expression does not have a next occurrence.");

            var jobItem = new JobStoreItem {
               JobType = typeof(TJob),
               PerformAt = nextOccurence.Value,
               Parameters = parameters,
               Options = scheduleOptions,
               CronExpression = cronExpression,
               CultureName = cultureName,
               UICultureName = uiCultureName
            };

            await _jobStorage.ScheduleJobAsync(jobItem, ct);
         }

         Log.Information("Scheduled Job: {JobType} with parameters: {@Parameters} to run on schedule {CronExpression}", typeof(TJob).Name, parameters, cronExpression.ToString());
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
         throw;
      }
   }

   public async Task<bool> IsJobScheduledAsync<TJob>(CancellationToken ct = default) where TJob : IJob
   {
      var scheduledJobs = await GetScheduledJobsAsync(ct);
      return scheduledJobs.Any(x => x.JobType == typeof(TJob));
   }

   public async Task<IEnumerable<ScheduledJobInfo>> GetScheduledJobsAsync<TJob>(CancellationToken ct = default) where TJob : IJob
   {
      var scheduledJobs = await GetScheduledJobsAsync(ct);
      return scheduledJobs.Where(x => x.JobType == typeof(TJob));
   }
   
   public async Task<IEnumerable<ScheduledJobInfo>> GetScheduledJobsAsync(CancellationToken ct = default)
   {
      var scheduledJobs = (await _jobStorage.GetScheduledJobsAsync(ct)).Select(x => new ScheduledJobInfo {
         JobId = x.JobId,
         JobType = x.JobType,
         Parameters = x.Parameters,
         PerformAt = x.PerformAt,
         InProgress = false
      });

      var inProgressJobs = (await _jobStorage.GetInProgressJobsAsync(ct)).Select(x => new ScheduledJobInfo {
         JobId = x.JobId,
         JobType = x.JobType,
         Parameters = x.Parameters,
         PerformAt = x.PerformAt,
         InProgress = true
      });

      return scheduledJobs.Concat(inProgressJobs);
   }
}