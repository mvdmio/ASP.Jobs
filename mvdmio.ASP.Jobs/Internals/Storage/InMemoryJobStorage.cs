using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

namespace mvdmio.ASP.Jobs.Internals.Storage;

internal class InMemoryJobStorage : IJobStorage
{
   private readonly PriorityQueue<JobStoreItem, DateTime> _jobQueue = new();
   private readonly SemaphoreSlim _jobQueueLock = new(1, 1);
   
   public async Task AddJobAsync<TJob, TParameters>(TParameters parameters, DateTime performAtUtc, CancellationToken ct = default)
      where TJob : IJob<TParameters>
   {
      await AddJobAsync(
         new JobStoreItem {
            JobType = typeof(TJob),
            Parameters = parameters!
         },
         performAtUtc,
         ct
      );
   }

   public async Task AddCronJobAsync<TJob, TParameters>(TParameters parameters, CronExpression cronExpression, bool runImmediately = false, CancellationToken ct = default) where TJob : IJob<TParameters>
   {
      var jobItem = new JobStoreItem {
         JobType = typeof(TJob),
         Parameters = parameters!,
         CronExpression = cronExpression
      };

      if(runImmediately)
         await AddJobAsync(jobItem, DateTime.UtcNow, ct);
      else
         await ScheduleNextOccurrence(jobItem, ct);
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
            if(job.CronExpression is not null)
               await ScheduleNextOccurrence(job, ct);

            return job;
         }
         
         return null;
      }
      finally
      {
         _jobQueueLock.Release();
      }
   }

   private async Task AddJobAsync(JobStoreItem item, DateTime performAtUtc, CancellationToken ct = default)
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

   private async Task ScheduleNextOccurrence(JobStoreItem jobItem, CancellationToken ct = default)
   {
      if (jobItem.CronExpression is null)
         return;

      var nextOccurrence = jobItem.CronExpression.GetNextOccurrence(DateTime.UtcNow);
      if(nextOccurrence is null)
         throw new InvalidOperationException("CRON expression does not have a next occurrence.");

      await AddJobAsync(jobItem, nextOccurrence.Value, ct);
   }
}