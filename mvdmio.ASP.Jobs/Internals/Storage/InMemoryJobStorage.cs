using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using Serilog;

namespace mvdmio.ASP.Jobs.Internals.Storage;

internal class InMemoryJobStorage : IJobStorage
{
   private readonly PriorityQueue<JobStoreItem, DateTime> _jobQueue = new();
   private readonly SemaphoreSlim _jobQueueLock = new(1, 1);
   
   public async Task QueueJobAsync(JobStoreItem item, DateTime performAtUtc, CancellationToken ct = default)
   {
      await _jobQueueLock.WaitAsync(ct);

      try
      {
         _jobQueue.Enqueue(item, performAtUtc);
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public async Task<JobStoreItem?> GetNextJobAsync(CancellationToken ct = default)
   {
      if (_jobQueue.Count is 0)
         return null;

      await _jobQueueLock.WaitAsync(ct);

      try
      {
         if (_jobQueue.TryPeek(out _, out var performAt) && performAt > DateTime.UtcNow)
            return null;

         if (_jobQueue.TryDequeue(out var job, out _))
         {
            return job;
         }

         return null;
      }
      catch (Exception e)
      {
         Log.Error(e, "Error while retrieving next job from queue");
         throw;
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   
}