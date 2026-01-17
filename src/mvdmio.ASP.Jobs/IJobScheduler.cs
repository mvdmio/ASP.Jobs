using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Interface for a job scheduler.
/// </summary>
[PublicAPI]
public interface IJobScheduler
{
   /// <summary>
   ///    Schedule a job to be performed immediately.
   /// </summary>
   public Task PerformNowAsync<TJob, TParameters>(TParameters parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class, new();

   /// <summary>
   ///    Schedule a job to be performed as soon as possible.
   /// </summary>
   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class, new();

   /// <summary>
   ///    Schedule multiple jobs to be performed as soon as possible.
   /// </summary>
   public Task PerformAsapAsync<TJob, TParameters>(IEnumerable<TParameters> parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class, new();

   /// <summary>
   ///    Schedule a job to be performed as soon as possible.
   /// </summary>
   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, JobScheduleOptions options, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class, new();

   /// <summary>
   ///    Schedule a job to be performed at a given time.
   /// </summary>
   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class, new();

   /// <summary>
   ///    Schedule multiple jobs to be performed at a given time.
   /// </summary>
   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, IEnumerable<TParameters> parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class, new();

   /// <summary>
   ///    Schedule a job to be performed at a given time.
   /// </summary>
   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, JobScheduleOptions options, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class, new();

   /// <summary>
   ///    Schedule a job to be performed repeatedly on a given CRON schedule.
   /// </summary>
   public Task PerformCronAsync<TJob, TParameters>(string cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class, new();

   /// <summary>
   ///    Schedule a job to be performed repeatedly on a given CRON schedule.
   /// </summary>
   public Task PerformCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class, new();
   
   /// <summary>
   ///   Check if any job of the specified type is currently scheduled.
   /// </summary>
   public Task<bool> IsJobScheduledAsync<TJob>(CancellationToken ct = default) where TJob : IJob;

   /// <summary>
   ///   Retrieve the jobs of the specified type that are currently scheduled.     
   /// </summary>
   Task<IEnumerable<ScheduledJobInfo>> GetScheduledJobsAsync<TJob>(CancellationToken ct = default) where TJob : IJob;
   
   /// <summary>
   ///   Retrieve the jobs that are currently scheduled.     
   /// </summary>
   public Task<IEnumerable<ScheduledJobInfo>> GetScheduledJobsAsync(CancellationToken ct = default);
}