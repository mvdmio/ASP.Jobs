using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;

namespace mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

internal interface IJobStorage
{
   /// <summary>
   ///    Queue a job for execution.
   /// </summary>
   Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default);

   /// <summary>
   ///    Queue multiple jobs for execution.
   /// </summary>
   Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default);

   /// <summary>
   ///    Retrieve the next job that may be executed and mark it as 'in progress'.
   ///    Calling this method will not return the same job twice.
   ///    This method will not return jobs that are scheduled for the future.
   ///    This method will block until a new job is available.
   /// </summary>
   /// <exception cref="OperationCanceledException">Thrown when cancellation is requested while waiting.</exception>
   Task<JobStoreItem?> WaitForNextJobAsync(TimeSpan? maxWaitTime = null, CancellationToken ct = default);

   /// <summary>
   ///    Remove the job from storage. Either because it has been executed successfully or because it has failed.
   /// </summary>
   Task FinalizeJobAsync(JobStoreItem job, CancellationToken ct = default);

   /// <summary>
   ///   Retrieve all jobs that are scheduled to be executed in the future.
   /// </summary>
   Task<IEnumerable<JobStoreItem>> GetScheduledJobsAsync(CancellationToken ct = default);

   /// <summary>
   ///   Retrieve all jobs that are currently executing.
   /// </summary>
   /// <returns></returns>
   Task<IEnumerable<JobStoreItem>> GetInProgressJobsAsync(CancellationToken ct = default);
}