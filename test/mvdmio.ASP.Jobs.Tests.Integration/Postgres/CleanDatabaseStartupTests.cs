using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using mvdmio.Database.PgSQL;
using Testcontainers.PostgreSql;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
/// Regression guard for the original clean-database crash: starting a host configured with Postgres storage against an
/// empty database used to throw because Instance Registration ran before the migrations that create <c>job_instances</c>.
/// With eager Initialization (in <c>StartingAsync</c>, before any <c>StartAsync</c>), startup must now succeed: the schema
/// is created and the Worker Instance is registered.
/// </summary>
public sealed class CleanDatabaseStartupTests : IAsyncLifetime
{
   // A dedicated, *unmigrated* container so we exercise a genuinely clean database (the shared PostgresFixture is
   // migrated up front, so it cannot reproduce the clean-boot scenario).
   private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:18.1").Build();

   public async ValueTask InitializeAsync() => await _dbContainer.StartAsync();

   public async ValueTask DisposeAsync()
   {
      await _dbContainer.StopAsync();
      await _dbContainer.DisposeAsync();
   }

   [Fact]
   public async Task HostStartup_OnCleanDatabase_Succeeds_MigratesSchema_AndRegistersInstance()
   {
      // Arrange
      var connectionString = _dbContainer.GetConnectionString();

      var builder = Host.CreateApplicationBuilder();
      builder.Services.AddJobs(options => options.UsePostgresStorage("clean-startup-test", connectionString));

      using var host = builder.Build();

      // Act - on a clean database this used to throw during startup.
      var start = async () => await host.StartAsync(TestContext.Current.CancellationToken);
      await start.Should().NotThrowAsync("host startup must run Initialization before Instance Registration on a clean database");

      try
      {
         // Assert - migrations ran (the schema exists) and the Worker Instance registered itself.
         using var connectionFactory = new DatabaseConnectionFactory();
         var db = connectionFactory.BuildConnection(connectionString);

         var jobsTable = await db.Dapper.QueryFirstOrDefaultAsync<string?>("SELECT to_regclass('mvdmio.jobs')::text", ct: TestContext.Current.CancellationToken);
         jobsTable.Should().NotBeNull("Initialization must create the jobs table");

         var registeredInstances = await db.Dapper.QueryFirstAsync<long>("SELECT COUNT(*) FROM mvdmio.job_instances", ct: TestContext.Current.CancellationToken);
         registeredInstances.Should().BeGreaterThan(0, "the Worker Instance must be registered after Initialization");
      }
      finally
      {
         await host.StopAsync(TestContext.Current.CancellationToken);
      }
   }
}
