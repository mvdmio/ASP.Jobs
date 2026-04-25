using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

public sealed class PostgresJobInstanceRepositoryTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly DatabaseConnection _db;
   private readonly PostgresStorageHarness _harness;

   private TestClock Clock => _harness.Clock;
   private PostgresJobInstanceRepository Repository => _harness.InstanceRepository;
   private string InstanceId
   {
      get => _harness.Configuration.InstanceId;
      set => _harness.Configuration.InstanceId = value;
   }

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public PostgresJobInstanceRepositoryTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _db = fixture.DatabaseConnection;
      _harness = new PostgresStorageHarness(fixture);
   }

   public async ValueTask InitializeAsync() => await _fixture.ResetAsync();
   public ValueTask DisposeAsync() => ValueTask.CompletedTask;

   [Fact]
   public async Task RegisterInstance_ShouldInsertNewInstance_WhenInstanceIsNotRegisteredYet()
   {
      // Act
      await Repository.RegisterInstance(CancellationToken);

      // Assert
      await AssertSingleInstanceAsync("test-instance", Clock.UtcNow);
   }

   [Fact]
   public async Task RegisterInstance_ShouldUpdateExistingInstance_WhenInstanceIsAlreadyRegistered()
   {
      // Arrange
      await Repository.RegisterInstance(CancellationToken);

      // Act
      Clock.UtcNow = DateTime.UtcNow;
      await Repository.RegisterInstance(CancellationToken);

      // Assert
      await AssertSingleInstanceAsync("test-instance", Clock.UtcNow);
   }

   [Fact]
   public async Task UnregisterInstance_ShouldRemoveInstance_WhenInstanceIsRegistered()
   {
      // Arrange
      await Repository.RegisterInstance(CancellationToken);

      // Act
      await Repository.UnregisterInstance(CancellationToken);

      // Assert
      (await Repository.GetInstances(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task UnregisterInstance_ShouldNotThrow_WhenInstanceIsNotRegistered()
   {
      // Act
      await Repository.UnregisterInstance(CancellationToken);

      // Assert
      (await Repository.GetInstances(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task UpdateLastSeenAt_ShouldUpdateLastSeenAt_WhenInstanceIsRegistered()
   {
      // Arrange
      await Repository.RegisterInstance(CancellationToken);

      // Act
      Clock.UtcNow = DateTime.UtcNow.AddMinutes(5);
      await Repository.UpdateLastSeenAt(CancellationToken);

      // Assert
      await AssertSingleInstanceAsync("test-instance", Clock.UtcNow);
   }

   [Fact]
   public async Task UpdateLastSeenAt_ShouldRegisterInstance_WhenInstanceIsNotRegisteredYet()
   {
      // Act
      await Repository.UpdateLastSeenAt(CancellationToken);

      // Assert
      await AssertSingleInstanceAsync("test-instance", Clock.UtcNow);
   }

   [Fact]
   public async Task CleanupOldInstances_ShouldRemoveExpiredInstances_WhenThereAreExpiredInstances()
   {
      // Arrange
      await RegisterInstanceAtAsync("test-instance-1", DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)));
      await RegisterInstanceAtAsync("test-instance-2", DateTime.UtcNow);

      // Act
      await Repository.CleanupOldInstances(CancellationToken);

      // Assert
      await AssertSingleInstanceAsync("test-instance-2", Clock.UtcNow);
   }

   [Fact]
   public async Task CleanupOldInstances_ShouldNotRemoveInstances_WhenThereAreNoExpiredInstances()
   {
      // Arrange
      await RegisterInstanceAtAsync("test-instance-1", DateTime.UtcNow);

      // Act
      await Repository.CleanupOldInstances(CancellationToken);

      // Assert
      await AssertSingleInstanceAsync("test-instance-1", Clock.UtcNow);
   }

   [Fact]
   public async Task ReleaseStartedJobs_ShouldSetStartedAtToNull_WhenInstanceHasStartedJobs()
   {
      // Arrange
      await Repository.RegisterInstance(CancellationToken);
      await InsertStartedJob(Clock.UtcNow, InstanceId);

      // Act
      await Repository.ReleaseStartedJobs(CancellationToken);

      // Assert
      var jobs = await GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      AssertUnstarted(jobs[0]);
   }

   [Fact]
   public async Task ReleaseStartedJobs_ShouldNotReleaseJobsFromOtherInstances()
   {
      // Arrange
      await Repository.RegisterInstance(CancellationToken);
      await InsertStartedJob(Clock.UtcNow, InstanceId);
      await InsertStartedJob(Clock.UtcNow, InstanceId);

      // Act
      InstanceId = "other-instance";
      await Repository.ReleaseStartedJobs(CancellationToken);

      // Assert
      var jobs = await GetJobsFromDatabase();
      jobs.Should().HaveCount(2);
      jobs.Should().AllSatisfy(j =>
      {
         j.StartedAt.Should().Be(Clock.UtcNow);
         j.StartedBy.Should().Be("test-instance");
      });
   }

   [Fact]
   public async Task CleanupOldInstances_ShouldReleaseStartedJobs_WhenExpiredInstanceHasStartedJobs()
   {
      // Arrange
      await RegisterInstanceAtAsync("expired-instance", DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)));
      await InsertStartedJob(Clock.UtcNow, "expired-instance", "job-1");

      Clock.UtcNow = DateTime.UtcNow;

      // Act
      await Repository.CleanupOldInstances(CancellationToken);

      // Assert - the started job should be reset to unstarted
      await AssertSingleUnstartedJobAsync("job-1");
   }

   [Fact]
   public async Task CleanupOldInstances_ShouldDeleteStaleJob_WhenUnstartedDuplicateAlreadyExists()
   {
      // Arrange - expired instance with a started job, plus an unstarted duplicate (e.g. CRON re-enqueue)
      await RegisterInstanceAtAsync("expired-instance", DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)));
      await InsertStartedJob(Clock.UtcNow, "expired-instance", "duplicate-job");
      await InsertStartedJob(null, null, "duplicate-job");

      Clock.UtcNow = DateTime.UtcNow;

      // Act - should NOT throw a unique constraint violation
      await Repository.CleanupOldInstances(CancellationToken);

      // Assert - only the unstarted job should remain; the stale in-progress one should be deleted
      await AssertSingleUnstartedJobAsync("duplicate-job");
   }

   [Fact]
   public async Task CleanupOldInstances_ShouldKeepExactlyOneSurvivor_WhenMultipleStaleJobsCollideOnSameKey()
   {
      // Arrange - two stale in-progress rows for the same (application_name, job_name) and no unstarted peer.
      // Without the fix this triggers a 23505 duplicate key violation on idxu_jobs__application__job_name__not_started
      // when the recovery NULLs started_at.
      var earlierPerformAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10));
      var laterPerformAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(8));

      await RegisterInstanceAtAsync("expired-instance-a", earlierPerformAt);
      await InsertStartedJob(Clock.UtcNow, "expired-instance-a", "colliding-job");

      await RegisterInstanceAtAsync("expired-instance-b", laterPerformAt);
      await InsertStartedJob(Clock.UtcNow, "expired-instance-b", "colliding-job");

      Clock.UtcNow = DateTime.UtcNow;

      // Act - should NOT throw a unique constraint violation
      await Repository.CleanupOldInstances(CancellationToken);

      // Assert - exactly one surviving row (the earliest-due one), reset to unstarted; both expired instances removed.
      var jobs = await AssertSingleUnstartedJobAsync("colliding-job");
      jobs[0].PerformAt.Should().BeCloseTo(earlierPerformAt, TimeSpan.FromMilliseconds(1));

      (await Repository.GetInstances(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task CleanupOldInstances_ShouldDeleteAllStaleRows_WhenUnstartedPeerExistsAndMultipleStaleCollide()
   {
      // Arrange - two stale in-progress rows for the same key AND an unstarted peer already exists.
      // All stale rows must be dropped; the unstarted peer must remain untouched.
      await RegisterInstanceAtAsync("expired-instance-a", DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)));
      await InsertStartedJob(Clock.UtcNow, "expired-instance-a", "colliding-job");

      await RegisterInstanceAtAsync("expired-instance-b", DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(8)));
      await InsertStartedJob(Clock.UtcNow, "expired-instance-b", "colliding-job");

      // Unstarted peer (e.g. CRON re-enqueue while the old runs were stuck)
      await InsertStartedJob(null, null, "colliding-job");

      Clock.UtcNow = DateTime.UtcNow;

      // Act
      await Repository.CleanupOldInstances(CancellationToken);

      // Assert - only the unstarted peer remains.
      await AssertSingleUnstartedJobAsync("colliding-job");
   }

   [Fact]
   public async Task ReleaseStartedJobs_ShouldResetJob_WhenNoUnstartedDuplicateExists()
   {
      // Arrange
      await Repository.RegisterInstance(CancellationToken);
      await InsertStartedJob(Clock.UtcNow, InstanceId, "unique-job");

      // Act
      await Repository.ReleaseStartedJobs(CancellationToken);

      // Assert
      await AssertSingleUnstartedJobAsync("unique-job");
   }

   [Fact]
   public async Task ReleaseStartedJobs_ShouldDeleteStaleJob_WhenUnstartedDuplicateAlreadyExists()
   {
      // Arrange
      await Repository.RegisterInstance(CancellationToken);
      await InsertStartedJob(Clock.UtcNow, InstanceId, "duplicate-job");
      await InsertStartedJob(null, null, "duplicate-job"); // unstarted duplicate (CRON re-schedule)

      // Act - should NOT throw a unique constraint violation
      await Repository.ReleaseStartedJobs(CancellationToken);

      // Assert
      await AssertSingleUnstartedJobAsync("duplicate-job");
   }

   [Fact]
   public async Task ReleaseStartedJobs_ShouldKeepExactlyOneSurvivor_WhenMultipleStaleJobsCollideOnSameKey()
   {
      // Arrange - two in-progress rows owned by this instance with identical (application_name, job_name)
      // and no unstarted peer. Without the fix the second NULL update violates the partial unique index.
      var earlierPerformAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10));
      var laterPerformAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(8));

      await Repository.RegisterInstance(CancellationToken);

      Clock.UtcNow = earlierPerformAt;
      await InsertStartedJob(Clock.UtcNow, InstanceId, "colliding-job");
      Clock.UtcNow = laterPerformAt;
      await InsertStartedJob(Clock.UtcNow, InstanceId, "colliding-job");

      // Act - should NOT throw a unique constraint violation
      await Repository.ReleaseStartedJobs(CancellationToken);

      // Assert - exactly one surviving row (the earliest-due one), reset to unstarted.
      var jobs = await AssertSingleUnstartedJobAsync("colliding-job");
      jobs[0].PerformAt.Should().BeCloseTo(earlierPerformAt, TimeSpan.FromMilliseconds(1));
   }

   // -------- Helpers --------

   private async Task AssertSingleInstanceAsync(string expectedInstanceId, DateTime expectedLastSeenAt)
   {
      var instances = (await Repository.GetInstances(CancellationToken)).ToList();
      instances.Should().HaveCount(1);
      instances[0].InstanceId.Should().Be(expectedInstanceId);
      instances[0].LastSeenAt.Should().Be(expectedLastSeenAt);
   }

   private async Task<List<JobData>> AssertSingleUnstartedJobAsync(string expectedJobName)
   {
      var jobs = await GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs[0].JobName.Should().Be(expectedJobName);
      AssertUnstarted(jobs[0]);
      return jobs;
   }

   private static void AssertUnstarted(JobData job)
   {
      job.StartedAt.Should().BeNull();
      job.StartedBy.Should().BeNull();
   }

   private async Task RegisterInstanceAtAsync(string instanceId, DateTime lastSeenAt)
   {
      InstanceId = instanceId;
      Clock.UtcNow = lastSeenAt;
      await Repository.RegisterInstance(CancellationToken);
   }

   private async Task<List<JobData>> GetJobsFromDatabase()
   {
      return (await _db.Dapper.QueryAsync<JobData>("SELECT * FROM mvdmio.jobs;", ct: CancellationToken)).ToList();
   }

   private Task InsertStartedJob(DateTime? startedAt, string? startedBy, string jobName = "TestJobName")
   {
      return _db.Dapper.ExecuteAsync(
         """
         INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, started_at, started_by)
         VALUES (:id, :job_type, :parameters_json, :parameters_type, :cron_expression, :application_name, :job_name, :job_group, :perform_at, :started_at, :started_by)
         """,
         new Dictionary<string, object?> {
            { "id", Guid.NewGuid() },
            { "job_type", typeof(TestJob).AssemblyQualifiedName },
            { "parameters_json", new TypedQueryParameter("{}", NpgsqlDbType.Jsonb) },
            { "parameters_type", typeof(TestJob.Parameters).AssemblyQualifiedName },
            { "cron_expression", null },
            { "application_name", _harness.Configuration.ApplicationName },
            { "job_name", jobName },
            { "job_group", "TestJobGroup" },
            { "perform_at", Clock.UtcNow },
            { "started_at", startedAt },
            { "started_by", startedBy }
         },
         ct: CancellationToken
      );
   }
}
