using System;
using System.Threading;
using System.Threading.Tasks;

namespace mvdmio.ASP.Jobs.Internals.JobBus;

internal interface IJobBus
{
   Task AddJobAsync<TJob, TParameters>(TParameters parameters, DateTimeOffset performAt, CancellationToken cancellationToken = default) where TJob : IJob<TParameters>;
   Task AddJobAsync(JobBusItem item, DateTimeOffset performAt, CancellationToken cancellationToken = default);
   Task<JobBusItem?> GetNextJobAsync(CancellationToken cancellationToken);
}