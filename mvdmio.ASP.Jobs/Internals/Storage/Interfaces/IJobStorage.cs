using System;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;

namespace mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

internal interface IJobStorage
{
   Task QueueJobAsync(JobStoreItem jobItem, DateTime performAtUtc, CancellationToken ct = default);
   Task<JobStoreItem?> GetNextJobAsync(CancellationToken ct);
}