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
   private readonly IClock _clock;
   private readonly IDictionary<string, JobStoreItem> _scheduledJobs = new Dictionary<string, JobStoreItem>();
   private readonly IDictionary<string, JobStoreItem> _inProgressJobs = new Dictionary<string, JobStoreItem>();
   private readonly SemaphoreSlim _jobQueueLock = new(1, 1);

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
         _scheduledJobs[item.Options.JobId] = item;
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
            _scheduledJobs[item.Options.JobId] = item;
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
         var job = _scheduledJobs
            .Where(x => x.Value.PerformAt <= _clock.UtcNow)
            .Where(x => x.Value.Options.Group is null || !GroupsInProgress.Contains(x.Value.Options.Group!))
            .Select(x => x.Value)
            .FirstOrDefault();

         if (job is null)
            return null;
         
         _scheduledJobs.Remove(job.Options.JobId);
         _inProgressJobs[job.Options.JobId] = job;
         
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
   
   public async Task FinalizeJobAsync(string jobId, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         _inProgressJobs.Remove(jobId);   
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }
}