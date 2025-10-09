using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(PostgresFixture))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true)]

namespace mvdmio.ASP.Jobs.Tests.Integration.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
   private readonly PostgreSqlContainer _dbContainer;
   
   public string ConnectionString => _dbContainer.GetConnectionString();
   public DatabaseConnectionFactory DatabaseConnectionFactory { get; }
   public DatabaseConnection DatabaseConnection => DatabaseConnectionFactory.ForConnectionString(ConnectionString);
   
   public PostgresFixture()
   {
      _dbContainer = new PostgreSqlBuilder().Build();
      
      DatabaseConnectionFactory = new DatabaseConnectionFactory();
   }
   
   public async ValueTask InitializeAsync()
   {
      await _dbContainer.StartAsync();
      
      var migrator = new DatabaseMigrator(DatabaseConnection, typeof(PostgresJobStorage).Assembly);
      await migrator.MigrateDatabaseToLatestAsync();
   }

   public async ValueTask DisposeAsync()
   {
      await _dbContainer.StopAsync();
      await _dbContainer.DisposeAsync();
   }

   public async Task ResetAsync()
   {
      await DatabaseConnection.Dapper.ExecuteAsync("TRUNCATE TABLE mvdmio.jobs CASCADE");
      await DatabaseConnection.Dapper.ExecuteAsync("TRUNCATE TABLE mvdmio.job_instances CASCADE");
   }
}