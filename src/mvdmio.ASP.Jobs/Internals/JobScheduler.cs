﻿using System;
using System.Collections.Generic;
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

internal sealed class JobScheduler : IJobScheduler
{
   private readonly IServiceProvider _services;
   private readonly IJobStorage _jobStorage;
   private readonly IClock _clock;
   
   public JobScheduler(IServiceProvider services, IJobStorage jobStorage, IClock clock)
   {
      _services = services;
      _jobStorage = jobStorage;
      _clock = clock;
   }

   public async Task PerformNowAsync<TJob, TParameters>(TParameters parameters, CancellationToken cancellationToken = default) where TJob : Job<TParameters>
   {
      var job = GetJobFromDi<TJob, TParameters>();
      
      try
      {
         await job.OnJobScheduledAsync(parameters, cancellationToken);
         await job.ExecuteAsync(parameters, cancellationToken);
         await job.OnJobExecutedAsync(parameters, cancellationToken);
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
         
         await job.OnJobFailedAsync(parameters, e, cancellationToken);
         
         throw;
      }
   }

   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, CancellationToken cancellationToken = default) where TJob : Job<TParameters>
   {
      return PerformAsapAsync<TJob, TParameters>(parameters, new JobScheduleOptions(), cancellationToken);
   }

   public async Task PerformAsapAsync<TJob, TParameters>(IEnumerable<TParameters> parameters, CancellationToken cancellationToken = default) where TJob : Job<TParameters>
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      var enumeratedParameters = parameters.ToArray();
      if (enumeratedParameters.Length is 0)
         return;

      try
      {
         var job = GetJobFromDi<TJob, TParameters>();

         await _jobStorage.ScheduleJobsAsync(
            enumeratedParameters.Select(x => new JobStoreItem {
                  JobType = typeof(TJob),
                  PerformAt = _clock.UtcNow,
                  Parameters = x!,
                  Options = new JobScheduleOptions()
               }
            ),
            cancellationToken
         );

         await Task.WhenAll(enumeratedParameters.Select(x => job.OnJobScheduledAsync(x, cancellationToken)));

         Log.Information("Scheduled {Count} jobs of type {JobType}", enumeratedParameters.Length, typeof(TJob).Name);
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling {Count} jobs of type {JobType}", enumeratedParameters.Length, typeof(TJob).Name);
         throw;
      }
   }

   public async Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, JobScheduleOptions? options = null, CancellationToken cancellationToken = default) where TJob : Job<TParameters>
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      try
      {
         var job = GetJobFromDi<TJob, TParameters>();

         await job.OnJobScheduledAsync(parameters, cancellationToken);
         await _jobStorage.ScheduleJobAsync(
            new JobStoreItem {
               JobType = typeof(TJob),
               PerformAt = _clock.UtcNow,
               Parameters = parameters,
               Options = options ?? new JobScheduleOptions()
            },
            cancellationToken
         );

         Log.Information("Scheduled job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
         throw;
      }
   }

   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, CancellationToken cancellationToken = default) where TJob : Job<TParameters>
   {
      return PerformAtAsync<TJob, TParameters>(performAtUtc, parameters, new JobScheduleOptions(), cancellationToken);
   }

   public async Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, IEnumerable<TParameters> parameters, CancellationToken cancellationToken = default) where TJob : Job<TParameters>
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      var enumeratedParameters = parameters.ToArray();
      if (enumeratedParameters.Length is 0)
         return;

      try
      {
         var job = GetJobFromDi<TJob, TParameters>();

         await _jobStorage.ScheduleJobsAsync(
            enumeratedParameters.Select(x => new JobStoreItem {
                  JobType = typeof(TJob),
                  PerformAt = performAtUtc,
                  Parameters = x!,
                  Options = new JobScheduleOptions()
               }
            ),
            cancellationToken
         );

         await Task.WhenAll(enumeratedParameters.Select(x => job.OnJobScheduledAsync(x, cancellationToken)));

         Log.Information("Scheduled {Count} jobs of type {JobType} to run at {Time}", enumeratedParameters.Length, typeof(TJob).Name, performAtUtc);
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling {Count} jobs of type {JobType}", enumeratedParameters.Length, typeof(TJob).Name);
         throw;
      }
   }

   public async Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, JobScheduleOptions? options = null, CancellationToken cancellationToken = default) where TJob : Job<TParameters>
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      try
      {
         var job = GetJobFromDi<TJob, TParameters>();

         await job.OnJobScheduledAsync(parameters, cancellationToken);
         await _jobStorage.ScheduleJobAsync(
            new JobStoreItem {
               JobType = typeof(TJob),
               PerformAt = performAtUtc,
               Parameters = parameters,
               Options = options ?? new JobScheduleOptions()
            },
            cancellationToken
         );

         Log.Information("Scheduled Job: {JobType} with parameters: {@Parameters} to run at {Time}", typeof(TJob).Name, parameters, performAtUtc);
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
         throw;
      }
   }

   public Task PerformCronAsync<TJob, TParameters>(string cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken cancellationToken = default) where TJob : Job<TParameters>
   {
      return PerformCronAsync<TJob, TParameters>(CronExpression.Parse(cronExpression), parameters, runImmediately, cancellationToken);
   }

   public async Task PerformCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken cancellationToken = default) where TJob : Job<TParameters>
   {
      if (parameters is null)
         throw new ArgumentNullException(nameof(parameters));

      try
      {
         var job = GetJobFromDi<TJob, TParameters>();
         await job.OnJobScheduledAsync(parameters, cancellationToken);

         var normalizedCronExpression = cronExpression.ToString().Replace(" ", "");
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
               CronExpression = cronExpression
            };

            await _jobStorage.ScheduleJobAsync(jobItem, cancellationToken);
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
               CronExpression = cronExpression
            };

            await _jobStorage.ScheduleJobAsync(jobItem, cancellationToken);
         }

         Log.Information("Scheduled Job: {JobType} with parameters: {@Parameters} to run on schedule {CronExpression}", typeof(TJob).Name, parameters, cronExpression.ToString());
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
         throw;
      }
   }

   private TJob GetJobFromDi<TJob, TParameters>() where TJob : Job<TParameters>
   {
      var scope = _services.CreateScope();
      var job = scope.ServiceProvider.GetRequiredService<TJob>();
      return job;
   }
}