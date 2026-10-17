using AwesomeAssertions;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.Database.PgSQL;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
///    Tests that a Claim registers its Worker Instance again, so a <c>job_instances</c> row removed by a peer's
///    cleanup never makes the Claim fail with the <c>jobs_started_by_fkey</c> foreign-key error.
/// </summary>
public sealed class PostgresJobClaimRegistrationTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly CancellationTokenSource _cts;
   private readonly PostgresStorageHarness _harness;
   private readonly DatabaseConnection _db;

   private TestClock Clock => _harness.Clock;

   private CancellationToken CancellationToken => _cts.Token;

   public PostgresJobClaimRegistrationTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      _db = fixture.DatabaseConnection;
      _harness = new PostgresStorageHarness(fixture);

      _cts.CancelAfter(TimeSpan.FromSeconds(10));
   }

   public async ValueTask InitializeAsync()
   {
      await _fixture.ResetAsync();
      await _harness.Storage.InitializeAsync(CancellationToken);
   }

   public ValueTask DisposeAsync()
   {
      _cts.Dispose();
      return ValueTask.CompletedTask;
   }

   [Fact]
   public async Task WaitForNextJob_RegistersTheInstanceAgain_WhenCleanupRemovedItsRow()
   {
      // Arrange - the instance's row ages past the timeout and its own cleanup removes it, as a peer's cleanup would.
      await _harness.InstanceRepository.RegisterInstance(CancellationToken);
      var job = JobStoreItemFactory.MakeTestJob(performAt: Clock.UtcNow);
      await _harness.Storage.ScheduleJobAsync(job, CancellationToken);

      Clock.UtcNow = Clock.UtcNow.AddMinutes(6);
      await _harness.InstanceRepository.CleanupOldInstances(CancellationToken);
      (await GetInstanceRowsAsync()).Should().BeEmpty();

      // Act
      var claim = await _harness.Storage.WaitForNextJobAsync(CancellationToken);

      // Assert
      claim!.JobId.Should().Be(job.JobId);

      var startedBy = await _db.Dapper.QueryFirstAsync<string>("SELECT started_by FROM mvdmio.jobs WHERE id = :id", new Dictionary<string, object?> { { "id", job.JobId } }, ct: CancellationToken);
      startedBy.Should().Be("test-instance");

      var rows = await GetInstanceRowsAsync();
      rows.Should().ContainSingle().Which.Should().Be(new InstanceRow("test-instance", "test-application", Clock.UtcNow));
   }

   [Fact]
   public async Task WaitForNextJob_Claims_WhenAnotherApplicationsCleanupRemovedItsRow()
   {
      // Arrange - both instances have been silent for ten minutes; B heartbeats, then its cleanup removes A's row.
      var applicationB = new PostgresStorageHarness(_fixture, instanceId: "instance-b", applicationName: "application-b");
      await applicationB.Storage.InitializeAsync(CancellationToken);

      var tenMinutesAgo = DateTime.UtcNow.AddMinutes(-10);
      Clock.UtcNow = tenMinutesAgo;
      applicationB.Clock.UtcNow = tenMinutesAgo;
      await _harness.InstanceRepository.RegisterInstance(CancellationToken);
      await applicationB.InstanceRepository.RegisterInstance(CancellationToken);

      var now = DateTime.UtcNow;
      Clock.UtcNow = now;
      applicationB.Clock.UtcNow = now;
      await applicationB.InstanceRepository.RegisterInstance(CancellationToken);
      await applicationB.InstanceRepository.CleanupOldInstances(CancellationToken);
      (await GetInstanceRowsAsync()).Select(r => r.InstanceId).Should().Equal("instance-b");

      var job = JobStoreItemFactory.MakeTestJob(performAt: Clock.UtcNow);
      await _harness.Storage.ScheduleJobAsync(job, CancellationToken);

      // Act
      var claim = await _harness.Storage.WaitForNextJobAsync(CancellationToken);

      // Assert
      claim!.JobId.Should().Be(job.JobId);
      (await GetInstanceRowsAsync()).Select(r => r.InstanceId).Should().BeEquivalentTo("test-instance", "instance-b");
   }

   [Fact]
   public async Task WaitForNextJob_RefreshesLastSeenAtToTheClaimTime_WhenTheRowIsFresh()
   {
      // Arrange
      await _harness.InstanceRepository.RegisterInstance(CancellationToken);
      var job = JobStoreItemFactory.MakeTestJob(performAt: Clock.UtcNow);
      await _harness.Storage.ScheduleJobAsync(job, CancellationToken);

      Clock.UtcNow = Clock.UtcNow.AddMinutes(1);

      // Act
      var claim = await _harness.Storage.WaitForNextJobAsync(CancellationToken);

      // Assert
      claim!.JobId.Should().Be(job.JobId);
      (await GetInstanceRowsAsync()).Should().ContainSingle().Which.Should().Be(new InstanceRow("test-instance", "test-application", Clock.UtcNow));
   }

   [Fact]
   public async Task WaitForNextJob_WritesNoInstanceRow_WhenNothingIsDue()
   {
      // Act
      using var pollToken = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      pollToken.CancelAfter(TimeSpan.FromMilliseconds(500));
      var claim = await _harness.Storage.WaitForNextJobAsync(pollToken.Token);

      // Assert
      claim.Should().BeNull();
      (await GetInstanceRowsAsync()).Should().BeEmpty();
   }

   private async Task<List<InstanceRow>> GetInstanceRowsAsync()
   {
      var rows = await _db.Dapper.QueryAsync<InstanceRow>(
         "SELECT instance_id AS InstanceId, application_name AS ApplicationName, last_seen_at AS LastSeenAt FROM mvdmio.job_instances ORDER BY instance_id",
         ct: CancellationToken
      );

      return rows.ToList();
   }

   private sealed record InstanceRow(string InstanceId, string ApplicationName, DateTime LastSeenAt);
}
