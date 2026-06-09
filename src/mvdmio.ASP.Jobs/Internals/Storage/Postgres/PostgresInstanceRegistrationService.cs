using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    Hosted service that performs Instance Registration: it announces the current Worker Instance in
///    <c>job_instances</c> on application start and releases any claimed jobs and unregisters on shutdown.
///    Depends on Initialization (migrations) having already completed, which is guaranteed by ordering:
///    <see cref="JobInitializationHostedService"/> runs in <c>StartingAsync</c>, before any <c>StartAsync</c>.
/// </summary>
internal sealed class PostgresInstanceRegistrationService : IHostedService
{
   private readonly PostgresJobInstanceRepository _repository;

   /// <summary>
   ///    Initializes a new instance of the <see cref="PostgresInstanceRegistrationService"/> class.
   /// </summary>
   /// <param name="repository">The job instance repository.</param>
   public PostgresInstanceRegistrationService(PostgresJobInstanceRepository repository)
   {
      _repository = repository;
   }
   
   /// <inheritdoc />
   public async Task StartAsync(CancellationToken cancellationToken)
   {
      await _repository.RegisterInstance(cancellationToken);
   }

   /// <inheritdoc />
   public async Task StopAsync(CancellationToken cancellationToken)
   {
      await _repository.ReleaseStartedJobs(cancellationToken);
      await _repository.UnregisterInstance(cancellationToken);
   }
}