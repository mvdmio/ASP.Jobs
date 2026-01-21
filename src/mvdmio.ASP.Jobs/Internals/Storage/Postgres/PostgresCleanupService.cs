using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    Background service that periodically updates the instance heartbeat and cleans up stale instances.
///    Runs every minute to ensure job instances remain active and orphaned jobs are released.
/// </summary>
internal sealed class PostgresCleanupService : BackgroundService
{
   private readonly PostgresJobInstanceRepository _repository;
   private readonly PeriodicTimer _timer;

   /// <summary>
   ///    Initializes a new instance of the <see cref="PostgresCleanupService"/> class.
   /// </summary>
   /// <param name="repository">The job instance repository for managing instance registration.</param>
   public PostgresCleanupService(PostgresJobInstanceRepository repository)
   {
      _repository = repository;
      _timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
   }
   
   /// <inheritdoc />
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      while (!stoppingToken.IsCancellationRequested)
      {
         await _timer.WaitForNextTickAsync(stoppingToken);
         
         await _repository.UpdateLastSeenAt(stoppingToken);
         await _repository.CleanupOldInstances(stoppingToken);
      }
   }
}