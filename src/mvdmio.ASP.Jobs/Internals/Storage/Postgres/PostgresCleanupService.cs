using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

internal sealed class PostgresCleanupService : BackgroundService
{
   private readonly PostgresJobInstanceRepository _repository;
   private readonly PeriodicTimer _timer;

   public PostgresCleanupService(PostgresJobInstanceRepository repository)
   {
      _repository = repository;
      _timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
   }
   
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