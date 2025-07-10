using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

internal sealed class PostgresMigrationService : IHostedService
{
   private readonly PostgresJobStorageConfiguration _configuration;
   private readonly ILogger<PostgresMigrationService> _logger;

   public PostgresMigrationService(PostgresJobStorageConfiguration configuration, ILogger<PostgresMigrationService> logger)
   {
      _configuration = configuration;
      _logger = logger;
   }
   
   public async Task StartAsync(CancellationToken cancellationToken)
   {
      try
      {
         var dbConnection = new DatabaseConnection(_configuration.ConnectionString);
         var migrationRunner = new DatabaseMigrator(dbConnection, GetType().Assembly);

         await migrationRunner.MigrateDatabaseToLatestAsync(cancellationToken);
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "Error while running migrations for Postgres Job Storage");  
      }
   }

   public Task StopAsync(CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }
}