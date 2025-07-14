using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

[assembly:AssemblyFixture(typeof(PostgresFixture))]

namespace mvdmio.ASP.Jobs.Tests.Integration.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
   private readonly PostgreSqlContainer _dbContainer;
   private readonly DatabaseConnectionFactory _databaseConnectionFactory;

   public string ConnectionString => _dbContainer.GetConnectionString();
   public DatabaseConnection DatabaseConnection => _databaseConnectionFactory.ForConnectionString(ConnectionString);
   
   public PostgresFixture()
   {
      _databaseConnectionFactory = new DatabaseConnectionFactory();
      _dbContainer = new PostgreSqlBuilder().Build();
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
}