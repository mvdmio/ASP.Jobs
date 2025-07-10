using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Utils;
using Serilog;

namespace mvdmio.ASP.Jobs.Internals.Storage;

internal sealed class InMemoryJobStorage : IJobStorage
{
   private readonly IClock _clock;
   private readonly IDictionary<string, JobStoreItem> _inProgressJobs = new Dictionary<string, JobStoreItem>();
   private readonly SemaphoreSlim _jobQueueLock = new(1, 1);
   private readonly IDictionary<string, JobStoreItem> _scheduledJobs = new Dictionary<string, JobStoreItem>();

   internal IEnumerable<JobStoreItem> ScheduledJobs => _scheduledJobs.Values;
   internal IEnumerable<JobStoreItem> InProgressJobs => _inProgressJobs.Values;

   private IEnumerable<string> GroupsInProgress => _inProgressJobs.Where(x => x.Value.Options.Group is not null).Select(x => x.Value.Options.Group!).Distinct();

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
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public async Task<JobStoreItem?> StartNextJobAsync(CancellationToken ct = default)
   {
      if (_scheduledJobs.Count is 0)
         return null;

      await _jobQueueLock.WaitAsync(ct);

      try
      {
         var job = _scheduledJobs.Where(x => x.Value.PerformAt <= _clock.UtcNow).Where(x => x.Value.Options.Group is null || !GroupsInProgress.Contains(x.Value.Options.Group!)).OrderBy(x => x.Value.PerformAt).Select(x => x.Value).FirstOrDefault();

         if (job is null)
            return null;

         _scheduledJobs.Remove(job.Options.JobName);
         _inProgressJobs[job.Options.JobName] = job;

         return job;
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while retrieving next job");
         throw;
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public async Task FinalizeJobAsync(string jobName, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         _inProgressJobs.Remove(jobName);
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   internal async Task WaitForAllJobsFinishedAsync(CancellationToken ct = default)
   {
      while (ct.IsCancellationRequested == false)
      {
         if (_scheduledJobs.Count == 0 && _inProgressJobs.Count == 0 && _jobQueueLock.CurrentCount == 1)
            return;

         await Task.Delay(1, ct);
      }
   }
}