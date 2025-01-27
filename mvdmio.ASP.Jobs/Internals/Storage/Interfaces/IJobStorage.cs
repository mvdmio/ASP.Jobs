using System;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using mvdmio.ASP.Jobs.Internals.Storage.Data;

namespace mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

internal interface IJobStorage
{
   Task AddJobAsync<TJob, TParameters>(TParameters parameters, DateTime performAtUtc, CancellationToken ct = default) where TJob : IJob<TParameters>;
   Task AddCronJobAsync<TJob, TParameters>(TParameters parameters, CronExpression cronExpression, bool runImmediately = false, CancellationToken ct = default) where TJob : IJob<TParameters>;
   Task<JobStoreItem?> GetNextJobAsync(CancellationToken ct);
}