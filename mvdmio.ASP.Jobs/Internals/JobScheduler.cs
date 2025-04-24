using System;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using Serilog;

namespace mvdmio.ASP.Jobs.Internals;

internal class JobScheduler : IJobScheduler
{
   private readonly IServiceProvider _services;
   private readonly IJobStorage _jobStorage;

   public JobScheduler(IServiceProvider services, IJobStorage jobStorage)
   {
      _services = services;
      _jobStorage = jobStorage;
   }

   public async Task PerformNowAsync<TJob, TParameters>(TParameters parameters, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      try
      {
         var job = GetJobFromDi<TJob, TParameters>();

         await job.OnJobScheduledAsync(parameters, cancellationToken);
         await job.ExecuteAsync(parameters, cancellationToken);
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while scheduling job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
         throw;
      }
   }

   public async Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, JobScheduleOptions? options = null, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      ArgumentNullException.ThrowIfNull(parameters);

      try
      {
         var job = GetJobFromDi<TJob, TParameters>();
         
         await job.OnJobScheduledAsync(parameters, cancellationToken);
         await _jobStorage.ScheduleJobAsync(
            new JobStoreItem {
               JobType = typeof(TJob),
               PerformAt = DateTime.UtcNow,
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

   public async Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, JobScheduleOptions? options = null, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      ArgumentNullException.ThrowIfNull(parameters);

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

   public async Task PerformCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, JobScheduleOptions? options = null, bool runImmediately = false, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      ArgumentNullException.ThrowIfNull(parameters);

      try
      {
         
         var job = GetJobFromDi<TJob, TParameters>();
         await job.OnJobScheduledAsync(parameters, cancellationToken);

         if(runImmediately)
         {
            var jobItem = new JobStoreItem {
               JobType = typeof(TJob),
               PerformAt = DateTime.UtcNow,
               Parameters = parameters,
               Options = options ?? new JobScheduleOptions(),
               CronExpression = cronExpression
            };
            
            await _jobStorage.ScheduleJobAsync(jobItem, cancellationToken);   
         }
         else
         {
            var nextOccurence = cronExpression.GetNextOccurrence(DateTime.UtcNow);
            if (nextOccurence is null)
               throw new InvalidOperationException("CRON expression does not have a next occurrence.");

            var jobItem = new JobStoreItem {
               JobType = typeof(TJob),
               PerformAt = nextOccurence.Value,
               Parameters = parameters,
               Options = options ?? new JobScheduleOptions(),
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

   private TJob GetJobFromDi<TJob, TParameters>() where TJob : IJob<TParameters>
   {
      var scope = _services.CreateScope();
      var job = scope.ServiceProvider.GetRequiredService<TJob>();
      return job;
   }
}