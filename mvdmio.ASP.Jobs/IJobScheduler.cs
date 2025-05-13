using System;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
/// Interface for a job scheduler.
/// </summary>
[PublicAPI]
public interface IJobScheduler
{
   /// <summary>
   /// Schedule a job to be performed immediately.
   /// </summary>
   public Task PerformNowAsync<TJob, TParameters>(TParameters parameters, CancellationToken cancellationToken = default)
      where TJob : Job<TParameters>;

   /// <summary>
   /// Schedule a job to be performed as soon as possible.
   /// </summary>
   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, CancellationToken cancellationToken = default)
      where TJob : Job<TParameters>;
   
   /// <summary>
   /// Schedule a job to be performed as soon as possible.
   /// </summary>
   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, JobScheduleOptions options, CancellationToken cancellationToken = default)
      where TJob : Job<TParameters>;

   /// <summary>
   /// Schedule a job to be performed at a given time.
   /// </summary>
   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, CancellationToken cancellationToken = default)
      where TJob : Job<TParameters>;
   
   /// <summary>
   /// Schedule a job to be performed at a given time.
   /// </summary>
   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, JobScheduleOptions options, CancellationToken cancellationToken = default)
      where TJob : Job<TParameters>;

   /// <summary>
   /// Schedule a job to be performed repeatedly on a given CRON schedule.
   /// </summary>
   public Task PerformCronAsync<TJob, TParameters>(string cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken cancellationToken = default)
      where TJob : Job<TParameters>;
   
   /// <summary>
   /// Schedule a job to be performed repeatedly on a given CRON schedule.
   /// </summary>
   public Task PerformCronAsync<TJob, TParameters>(string cronExpression, TParameters parameters, JobScheduleOptions options, bool runImmediately = false, CancellationToken cancellationToken = default)
      where TJob : Job<TParameters>;

   /// <summary>
   /// Schedule a job to be performed repeatedly on a given CRON schedule.
   /// </summary>
   public Task PerformCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken cancellationToken = default)
      where TJob : Job<TParameters>;
   
   /// <summary>
   /// Schedule a job to be performed repeatedly on a given CRON schedule.
   /// </summary>
   public Task PerformCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, JobScheduleOptions options, bool runImmediately = false, CancellationToken cancellationToken = default)
      where TJob : Job<TParameters>;
}