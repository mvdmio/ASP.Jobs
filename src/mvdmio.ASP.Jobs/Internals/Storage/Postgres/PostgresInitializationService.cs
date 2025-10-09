using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

internal sealed class PostgresInitializationService : IHostedService
{
   private readonly DatabaseConnection _db;
   private readonly PostgresJobInstanceRepository _repository;

   public PostgresInitializationService(
      [FromKeyedServices("Jobs")] DatabaseConnectionFactory dbConnectionFactory,
      IOptions<PostgresJobStorageConfiguration> configuration,
      PostgresJobInstanceRepository repository
   ) {
      _db = dbConnectionFactory.ForConnectionString(configuration.Value.DatabaseConnectionString);
      _repository = repository;
   }
   
   public async Task StartAsync(CancellationToken cancellationToken)
   {
      await RunDbMigrations(cancellationToken);
      await _repository.RegisterInstance(cancellationToken);
   }

   public async Task StopAsync(CancellationToken cancellationToken)
   {
      await _repository.ReleaseStartedJobs(cancellationToken);
      await _repository.UnregisterInstance(cancellationToken);
   }

   private async Task RunDbMigrations(CancellationToken ct = default)
   {
      var migrationRunner = new DatabaseMigrator(_db, GetType().Assembly);
      await migrationRunner.MigrateDatabaseToLatestAsync(ct);
   }
}