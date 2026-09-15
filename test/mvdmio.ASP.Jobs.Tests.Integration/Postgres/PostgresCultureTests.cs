using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
///    Tests that verify a job's Captured Culture round-trips through the real PostgreSQL database.
/// </summary>
public sealed class PostgresCultureTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly PostgresStorageHarness _harness;
   private readonly DatabaseConnection _db;
   private readonly ServiceProvider _services;
   private readonly JobScheduler _scheduler;

   private PostgresJobStorage Storage => _harness.Storage;

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public PostgresCultureTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _harness = new PostgresStorageHarness(fixture);
      _db = fixture.DatabaseConnection;

      var services = new ServiceCollection();
      services.AddSingleton<TestJobRetryPolicyProvider>();
      services.RegisterJob<TestJob>();
      _services = services.BuildServiceProvider();

      _scheduler = new JobScheduler(_services, Storage, _harness.Clock);
   }

   public async ValueTask InitializeAsync()
   {
      await _fixture.ResetAsync();
      await _harness.Storage.InitializeAsync(CancellationToken);
      await _harness.InstanceRepository.RegisterInstance(CancellationToken);
   }

   public ValueTask DisposeAsync() => ValueTask.CompletedTask;

   [Fact]
   public async Task ExplicitCulture_RoundTripsThroughDatabase()
   {
      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(new TestJob.Parameters(), new CultureInfo("nl-NL"), CancellationToken);

      // Assert
      var stored = (await Storage.GetScheduledJobsAsync(CancellationToken)).Single();
      stored.CultureName.Should().Be("nl-NL");
      stored.UICultureName.Should().Be("nl-NL");
   }

   [Fact]
   public async Task AmbientCulture_RoundTripsBothValuesIndependently()
   {
      using var _ = new ThreadCultureScope("nl-NL", "de-DE");

      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(new TestJob.Parameters(), CancellationToken);

      var stored = (await Storage.GetScheduledJobsAsync(CancellationToken)).Single();
      stored.CultureName.Should().Be("nl-NL");
      stored.UICultureName.Should().Be("de-DE");
   }

   [Fact]
   public async Task RowsWithNullCultureColumns_RemainValidWithNoCapturedCulture()
   {
      // Arrange - omit culture/ui_culture so they stay NULL, as for rows written before culture capture.
      await _db.Dapper.ExecuteAsync(
         """
         INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at)
         VALUES (:id, :job_type, :parameters_json, :parameters_type, NULL, :application_name, :job_name, NULL, :perform_at)
         """,
         new Dictionary<string, object?> {
            { "id", Guid.NewGuid() },
            { "job_type", typeof(TestJob).AssemblyQualifiedName },
            { "parameters_json", new TypedQueryParameter("{}", NpgsqlDbType.Jsonb) },
            { "parameters_type", typeof(TestJob.Parameters).AssemblyQualifiedName },
            { "application_name", _harness.Configuration.ApplicationName },
            { "job_name", "pre-feature-job" },
            { "perform_at", _harness.Clock.UtcNow }
         },
         ct: CancellationToken
      );

      // Assert - null columns map to no Captured Culture (distinct from empty-string invariant).
      var stored = (await Storage.GetScheduledJobsAsync(CancellationToken)).Single();
      stored.CultureName.Should().BeNull();
      stored.UICultureName.Should().BeNull();
   }
}
