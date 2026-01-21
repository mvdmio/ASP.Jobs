using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Interface for a job scheduler that manages scheduling and querying of jobs.
/// </summary>
[PublicAPI]
public interface IJobScheduler
{
   /// <summary>
   ///    Schedule a job to be performed immediately and synchronously.
   /// </summary>
   /// <typeparam name="TJob">The type of job to execute.</typeparam>
   /// <typeparam name="TParameters">The type of parameters for the job.</typeparam>
   /// <param name="parameters">The parameters to pass to the job.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task PerformNowAsync<TJob, TParameters>(TParameters parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class;

   /// <summary>
   ///    Schedule a job to be performed as soon as possible.
   /// </summary>
   /// <typeparam name="TJob">The type of job to execute.</typeparam>
   /// <typeparam name="TParameters">The type of parameters for the job.</typeparam>
   /// <param name="parameters">The parameters to pass to the job.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class;

   /// <summary>
   ///    Schedule multiple jobs to be performed as soon as possible.
   /// </summary>
   /// <typeparam name="TJob">The type of job to execute.</typeparam>
   /// <typeparam name="TParameters">The type of parameters for the job.</typeparam>
   /// <param name="parameters">The collection of parameters, one for each job instance to schedule.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task PerformAsapAsync<TJob, TParameters>(IEnumerable<TParameters> parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class;

   /// <summary>
   ///    Schedule a job to be performed as soon as possible with custom options.
   /// </summary>
   /// <typeparam name="TJob">The type of job to execute.</typeparam>
   /// <typeparam name="TParameters">The type of parameters for the job.</typeparam>
   /// <param name="parameters">The parameters to pass to the job.</param>
   /// <param name="options">The scheduling options for the job.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, JobScheduleOptions options, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class;

   /// <summary>
   ///    Schedule a job to be performed at a given time.
   /// </summary>
   /// <typeparam name="TJob">The type of job to execute.</typeparam>
   /// <typeparam name="TParameters">The type of parameters for the job.</typeparam>
   /// <param name="performAtUtc">The UTC time at which to perform the job.</param>
   /// <param name="parameters">The parameters to pass to the job.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class;

   /// <summary>
   ///    Schedule multiple jobs to be performed at a given time.
   /// </summary>
   /// <typeparam name="TJob">The type of job to execute.</typeparam>
   /// <typeparam name="TParameters">The type of parameters for the job.</typeparam>
   /// <param name="performAtUtc">The UTC time at which to perform the jobs.</param>
   /// <param name="parameters">The collection of parameters, one for each job instance to schedule.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, IEnumerable<TParameters> parameters, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class;

   /// <summary>
   ///    Schedule a job to be performed at a given time with custom options.
   /// </summary>
   /// <typeparam name="TJob">The type of job to execute.</typeparam>
   /// <typeparam name="TParameters">The type of parameters for the job.</typeparam>
   /// <param name="performAtUtc">The UTC time at which to perform the job.</param>
   /// <param name="parameters">The parameters to pass to the job.</param>
   /// <param name="options">The scheduling options for the job.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, JobScheduleOptions options, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class;

   /// <summary>
   ///    Schedule a job to be performed repeatedly on a given CRON schedule.
   /// </summary>
   /// <typeparam name="TJob">The type of job to execute.</typeparam>
   /// <typeparam name="TParameters">The type of parameters for the job.</typeparam>
   /// <param name="cronExpression">The CRON expression defining the schedule.</param>
   /// <param name="parameters">The parameters to pass to the job.</param>
   /// <param name="runImmediately">If true, the job will run immediately in addition to the scheduled times.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task PerformCronAsync<TJob, TParameters>(string cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class;

   /// <summary>
   ///    Schedule a job to be performed repeatedly on a given CRON schedule.
   /// </summary>
   /// <typeparam name="TJob">The type of job to execute.</typeparam>
   /// <typeparam name="TParameters">The type of parameters for the job.</typeparam>
   /// <param name="cronExpression">The CRON expression defining the schedule.</param>
   /// <param name="parameters">The parameters to pass to the job.</param>
   /// <param name="runImmediately">If true, the job will run immediately in addition to the scheduled times.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task PerformCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken ct = default)
      where TJob : Job<TParameters>
      where TParameters : class;
   
   /// <summary>
   ///    Check if any job of the specified type is currently scheduled.
   /// </summary>
   /// <typeparam name="TJob">The type of job to check for.</typeparam>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>True if at least one job of the specified type is scheduled; otherwise, false.</returns>
   public Task<bool> IsJobScheduledAsync<TJob>(CancellationToken ct = default) where TJob : IJob;

   /// <summary>
   ///    Retrieve the jobs of the specified type that are currently scheduled.
   /// </summary>
   /// <typeparam name="TJob">The type of job to retrieve.</typeparam>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A collection of scheduled job information for jobs of the specified type.</returns>
   Task<IEnumerable<ScheduledJobInfo>> GetScheduledJobsAsync<TJob>(CancellationToken ct = default) where TJob : IJob;
   
   /// <summary>
   ///    Retrieve all jobs that are currently scheduled.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A collection of scheduled job information for all scheduled jobs.</returns>
   public Task<IEnumerable<ScheduledJobInfo>> GetScheduledJobsAsync(CancellationToken ct = default);
}
