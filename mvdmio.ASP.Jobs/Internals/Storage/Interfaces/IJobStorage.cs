using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;

namespace mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

internal interface IJobStorage
{
   /// <summary>
   /// Queue a job for execution.
   /// </summary>
   Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default);

   /// <summary>
   /// Queue multiple jobs for execution.
   /// </summary>
   Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default);

   /// <summary>
   /// Remove the job from storage. Either because it has been executed successfully or because it has failed.
   /// </summary>
   Task FinalizeJobAsync(string jobId, CancellationToken ct = default);

   /// <summary>
   /// Retrieve the next job that may be executed and mark it as 'in progress'.
   /// Calling this method will not return the same job twice.
   /// This method will not return jobs that are scheduled for the future.
   /// This method will return null if there are no jobs available.
   /// </summary>
   Task<JobStoreItem?> StartNextJobAsync(CancellationToken ct);
}