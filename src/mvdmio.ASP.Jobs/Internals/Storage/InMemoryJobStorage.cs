using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Utils;

namespace mvdmio.ASP.Jobs.Internals.Storage;

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

         if (item.PerformAt <= _clock.UtcNow && (item.Options.Group is null || !GroupsInProgress.Contains(item.Options.Group!)))
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
         var now = _clock.UtcNow;
         var shouldWake = false;
         foreach (var item in items)
         {
            _scheduledJobs[item.Options.JobName] = item;
            if (item.PerformAt <= now && (item.Options.Group is null || !GroupsInProgress.Contains(item.Options.Group!)))
               shouldWake = true;
         }

         if (shouldWake)
            SendWakeSignal();
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public async Task<JobStoreItem?> WaitForNextJobAsync(TimeSpan? maxWaitTime = null, CancellationToken ct = default)
   {
      var startTime = _clock.UtcNow;

      while (true)
      {
         ct.ThrowIfCancellationRequested();

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

         // If we have a max wait time, check if we should exit
         if (maxWaitTime.HasValue)
         {
            var elapsed = _clock.UtcNow - startTime;
            if (elapsed >= maxWaitTime.Value)
               return null;
         }

         await SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(maxWaitTime, startTime, now, ct);
      }
   }

   public async Task FinalizeJobAsync(JobStoreItem job, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         _inProgressJobs.Remove(job.JobId);

         if (job.Options.Group is not null)
            SendWakeSignal(); // A job group has become free for the next job. 
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

   private void SendWakeSignal()
   {
      _wakeWaiters.TrySetResult(null);
      _wakeWaiters = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
   }

   private async Task SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(TimeSpan? maxWaitTime, DateTime startTime, DateTime now, CancellationToken ct)
   {
      var candidates = _scheduledJobs.Values.Where(x => x.PerformAt > now).ToList();

      TimeSpan? delta = candidates.Count > 0 ? candidates.Min(x => x.PerformAt) - now : null;

      // Remaining time until maxWaitTime expires
      TimeSpan? remaining = maxWaitTime.HasValue ? maxWaitTime.Value - (_clock.UtcNow - startTime) : null;

      // Effective waitDuration: min(delta, remaining), or one of them, or null (indefinite)
      TimeSpan? waitDuration;
      if (delta.HasValue && remaining.HasValue)
         waitDuration = delta < remaining ? delta : remaining;
      else if (delta.HasValue)
         waitDuration = delta.Value;
      else if (remaining.HasValue)
         waitDuration = remaining.Value;
      else
         waitDuration = null;

      if (waitDuration.HasValue && waitDuration.Value <= TimeSpan.Zero)
         return;

      // Await signal or timeout
      var currentWake = _wakeWaiters;
      if (waitDuration.HasValue)
      {
         var delayTask = Task.Delay(waitDuration.Value, ct);
         _ = await Task.WhenAny(currentWake.Task, delayTask);
      }
      else
      {
         await currentWake.Task;
      }
   }
}