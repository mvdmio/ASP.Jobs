using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using mvdmio.Database.PgSQL;
using Testcontainers.PostgreSql;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
/// Checks that every connection the job storage opens, the dedicated LISTEN connection included, carries the job runner's
/// pool name (<c>&lt;entry assembly&gt;.Jobs</c>) in <c>pg_stat_activity.application_name</c>.
/// </summary>
public sealed class JobsPoolNameTests : IAsyncLifetime
{
   // A dedicated container, so the only other client connections to the database are the host's.
   private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:18.1").Build();

   public async ValueTask InitializeAsync() => await _dbContainer.StartAsync();

   public async ValueTask DisposeAsync()
   {
      await _dbContainer.StopAsync();
      await _dbContainer.DisposeAsync();
   }

   [Fact]
   public async Task JobStorageConnections_CarryTheEntryAssemblyNamePlusJobs()
   {
      // Arrange
      var ct = TestContext.Current.CancellationToken;
      var connectionString = _dbContainer.GetConnectionString();
      var expectedName = Assembly.GetEntryAssembly()!.GetName().Name + ".Jobs";

      var builder = Host.CreateApplicationBuilder();
      builder.Services.AddJobs(options => options.UsePostgresStorage("pool-name-test", connectionString));

      using var host = builder.Build();
      await host.StartAsync(ct);

      try
      {
         using var connectionFactory = new DatabaseConnectionFactory(new DatabaseConnectionFactorySettings { ApplicationName = "pool-name-test-observer" });
         await using var db = connectionFactory.BuildConnection(connectionString);

         // Keep one connection open, so pg_backend_pid() always excludes the observer itself.
         await db.OpenAsync(ct);

         // Act - wait until the runner sits in its LISTEN.
         var deadline = DateTime.UtcNow.AddSeconds(30);
         while (await CountListenConnectionsAsync(db, ct) == 0)
         {
            if (DateTime.UtcNow > deadline)
               throw new TimeoutException("The job runner never reached its LISTEN within 30 seconds.");

            await Task.Delay(100, ct);
         }

         var applicationNames = await db.Dapper.QueryAsync<string>(
            """
            SELECT application_name
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND backend_type = 'client backend'
              AND pid <> pg_backend_pid()
            """,
            ct: ct
         );

         // Assert
         applicationNames.Should().NotBeEmpty().And.AllBe(expectedName);
         (await CountListenConnectionsAsync(db, ct, expectedName)).Should().BeGreaterThan(0, "the LISTEN connection must carry the pool name");
      }
      finally
      {
         await host.StopAsync(ct);
      }
   }

   private static async Task<long> CountListenConnectionsAsync(DatabaseConnection db, CancellationToken ct, string? applicationName = null)
   {
      return await db.Dapper.QueryFirstAsync<long>(
         """
         SELECT COUNT(*)
         FROM pg_stat_activity
         WHERE datname = current_database()
           AND pid <> pg_backend_pid()
           AND query ILIKE 'LISTEN%jobs_updated%'
           AND (:application_name::text IS NULL OR application_name = :application_name)
         """,
         new Dictionary<string, object?> { ["application_name"] = applicationName },
         ct: ct
      );
   }
}
