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

internal class InMemoryJobStorage : IJobStorage
{
   private class JobState
   {
      public DateTime? StartedAt { get; set; }
   }
   
   private readonly IClock _clock;
   private readonly IDictionary<string, (JobStoreItem item, JobState state)> _jobs = new Dictionary<string, (JobStoreItem, JobState)>();
   private readonly SemaphoreSlim _jobQueueLock = new(1, 1);

   internal IEnumerable<JobStoreItem> Jobs => _jobs.Values.Select(x => x.item);
   
   private IEnumerable<string> GroupsInProgress => _jobs
      .Where(x => x.Value.state.StartedAt is not null)
      .Where(x => x.Value.item.Options.Group is not null)
      .Select(x => x.Value.item.Options.Group!)
      .Distinct();
   
   public InMemoryJobStorage()
      : this(SystemClock.Instance)
   {
   }

   internal InMemoryJobStorage(IClock clock)
   {
      _clock = clock;
   }
   
   public async Task AddJobAsync(JobStoreItem item, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         _jobs[item.Options.JobId] = (item, new JobState());
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public async Task RemoveJobAsync(string jobId, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         _jobs.Remove(jobId);   
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public async Task<JobStoreItem?> GetNextJobAsync(CancellationToken ct = default)
   {
      if (_jobs.Count is 0)
         return null;

      await _jobQueueLock.WaitAsync(ct);

      try
      {
         var job = _jobs
            .Where(x => x.Value.item.PerformAt <= _clock.UtcNow)
            .Where(x => x.Value.state.StartedAt is null)
            .Where(x => x.Value.item.Options.Group is null || !GroupsInProgress.Contains(x.Value.item.Options.Group!))
            .Select(x => x.Value.item)
            .FirstOrDefault();

         if (job is null)
            return null;
         
         _jobs[job.Options.JobId].state.StartedAt = _clock.UtcNow;

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
}