using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace mvdmio.ASP.Jobs.Internals.JobBus;

internal class InMemoryJobBus : IJobBus
{
   private readonly SemaphoreSlim _jobQueueLock;
   private readonly PriorityQueue<JobBusItem, DateTimeOffset> _jobQueue;
   
   public InMemoryJobBus()
   {
      _jobQueueLock = new SemaphoreSlim(1, 1);
      _jobQueue = new PriorityQueue<JobBusItem, DateTimeOffset>();
   }
   
   public async Task AddJobAsync<TJob, TParameters> (TParameters parameters, DateTimeOffset performAt, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      await AddJobAsync(
         new JobBusItem {
            JobType = typeof(TJob),
            Parameters = parameters!
         },
         performAt,
         cancellationToken
      );
   }

   public async Task AddJobAsync(JobBusItem item, DateTimeOffset performAt, CancellationToken cancellationToken = default)
   {
      await _jobQueueLock.WaitAsync(cancellationToken);

      try
      {
         _jobQueue.Enqueue(item, performAt);
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   public async Task<JobBusItem?> GetNextJobAsync(CancellationToken cancellationToken)
   {
      if (_jobQueue.Count is 0)
         return null;
      
      await _jobQueueLock.WaitAsync(cancellationToken);

      try
      {
         if (_jobQueue.TryPeek(out _, out var performAt) && performAt > DateTimeOffset.Now)
            return null;

         if (_jobQueue.TryDequeue(out var job, out _))
            return job;

         return null;
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }
}