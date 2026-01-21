using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;

namespace mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

/// <summary>
///    Interface for job storage implementations that handle persisting and retrieving scheduled jobs.
/// </summary>
internal interface IJobStorage
{
   /// <summary>
   ///    Schedules a job for execution.
   /// </summary>
   /// <param name="jobItem">The job item to schedule.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default);

   /// <summary>
   ///    Schedules multiple jobs for execution.
   /// </summary>
   /// <param name="items">The job items to schedule.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default);

   /// <summary>
   ///    Retrieves and claims the next job that is ready for execution.
   ///    The job is marked as 'in progress' to prevent other workers from claiming it.
   ///    This method will block until a job is available or cancellation is requested.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>The next available job, or null if cancellation was requested.</returns>
   /// <exception cref="OperationCanceledException">Thrown when cancellation is requested while waiting.</exception>
   Task<JobStoreItem?> WaitForNextJobAsync(CancellationToken ct = default);

   /// <summary>
   ///    Removes a job from storage after it has been executed or has failed.
   /// </summary>
   /// <param name="job">The job to finalize and remove.</param>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   Task FinalizeJobAsync(JobStoreItem job, CancellationToken ct = default);

   /// <summary>
   ///    Retrieves all jobs that are scheduled but not yet in progress.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A collection of scheduled job items.</returns>
   Task<IEnumerable<JobStoreItem>> GetScheduledJobsAsync(CancellationToken ct = default);

   /// <summary>
   ///    Retrieves all jobs that are currently being executed.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A collection of in-progress job items.</returns>
   Task<IEnumerable<JobStoreItem>> GetInProgressJobsAsync(CancellationToken ct = default);
}