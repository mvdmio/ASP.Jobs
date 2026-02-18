using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Utils;

namespace mvdmio.ASP.Jobs.Internals.Storage;

/// <summary>
///    In-memory implementation of <see cref="IJobStorage"/> suitable for testing and single-instance deployments.
///    Jobs are stored in memory and are lost when the application restarts.
/// </summary>
internal sealed class InMemoryJobStorage : IJobStorage
{
   private readonly IClock _clock;

   private readonly IDictionary<Guid, JobStoreItem> _inProgressJobs = new Dictionary<Guid, JobStoreItem>();
   private readonly SemaphoreSlim _jobQueueLock = new(1, 1);
   private readonly IDictionary<string, JobStoreItem> _scheduledJobs = new Dictionary<string, JobStoreItem>();
   private TaskCompletionSource<object?> _wakeWaiters = new(TaskCreationOptions.RunContinuationsAsynchronously);

   internal IEnumerable<JobStoreItem> ScheduledJobs => _scheduledJobs.Values;
   internal IEnumerable<JobStoreItem> InProgressJobs => _inProgressJobs.Values;

   private IEnumerable<string> GroupsInProgress => _inProgressJobs
      .Where(x => x.Value.Options.Group is not null)
      .Select(x => x.Value.Options.Group!)
      .Distinct();

   public InMemoryJobStorage()
      : this(SystemClock.Instance)
   {
   }

   internal InMemoryJobStorage(IClock clock)
   {
      _clock = clock;
   }

   public async Task ScheduleJobAsync(JobStoreItem item, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         _scheduledJobs[item.Options.JobName] = item;
         SendWakeSignal();   
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public async Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         foreach (var item in items)
         {
            _scheduledJobs[item.Options.JobName] = item;
         }

         SendWakeSignal();
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public async Task<JobStoreItem?> WaitForNextJobAsync(CancellationToken ct = default)
   {
      try
      {
         while (true)
         {
            var now = _clock.UtcNow;

            await _jobQueueLock.WaitAsync(ct);

            try
            {
               // Find the next job that is ready to be processed
               var job = _scheduledJobs.Values
                  .Where(x => x.PerformAt <= now && (x.Options.Group is null || !GroupsInProgress.Contains(x.Options.Group!)))
                  .OrderBy(x => x.PerformAt)
                  .FirstOrDefault();

               if (job is not null)
               {
                  _scheduledJobs.Remove(job.Options.JobName);
                  _inProgressJobs[job.JobId] = job;
                  return job;
               }
            }
            finally
            {
               _jobQueueLock.Release();
            }

            if (ct.IsCancellationRequested)
               return null;
         
            await SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(now, ct);
         }  
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
         return null;
      }
   }

   public async Task FinalizeJobAsync(JobStoreItem job, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         _inProgressJobs.Remove(job.JobId);
         SendWakeSignal(); 
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public Task<IEnumerable<JobStoreItem>> GetScheduledJobsAsync(CancellationToken ct = default)
   {
      return Task.FromResult(ScheduledJobs);
   }

   public Task<IEnumerable<JobStoreItem>> GetInProgressJobsAsync(CancellationToken ct = default)
   {
      return Task.FromResult(InProgressJobs);
   }

   public async Task DeleteJobByIdAsync(Guid jobId, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         // Try to remove from scheduled jobs
         var scheduledJob = _scheduledJobs.Values.FirstOrDefault(x => x.JobId == jobId);
         if (scheduledJob is not null)
         {
            _scheduledJobs.Remove(scheduledJob.Options.JobName);
         }

         // Try to remove from in-progress jobs
         _inProgressJobs.Remove(jobId);
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   private void SendWakeSignal()
   {
      _wakeWaiters.TrySetResult(null);
      _wakeWaiters = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
   }

   private async Task SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(DateTime now, CancellationToken ct)
   {
      await _jobQueueLock.WaitAsync(ct);
      List<JobStoreItem> candidates;
      try
      {
         candidates = _scheduledJobs.Values.Where(x => x.PerformAt > now).ToList();
      }
      finally
      {
         _jobQueueLock.Release();
      }
      
      TimeSpan? timeUntilNextPerformAt = candidates.Count > 0 ? candidates.Min(x => x.PerformAt) - now : null;
      if (timeUntilNextPerformAt.HasValue && timeUntilNextPerformAt.Value <= TimeSpan.Zero)
         return;

      // Await signal or timeout
      var currentWake = _wakeWaiters;
      if (timeUntilNextPerformAt.HasValue)
      {
         var delayTask = Task.Delay(timeUntilNextPerformAt.Value, ct);
         _ = await Task.WhenAny(currentWake.Task, delayTask);
      }
      else
      {
         await currentWake.Task.WaitAsync(ct);
      }
   }
}