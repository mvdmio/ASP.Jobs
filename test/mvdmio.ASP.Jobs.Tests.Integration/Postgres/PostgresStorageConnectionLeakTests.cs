using AwesomeAssertions;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.Database.PgSQL;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
/// Regression tests for connection-pool exhaustion (Postgres error 53300) reported by consumers of the library.
/// Before the fix, every iteration of the storage poll loop that took the "delay won the race" branch in
/// <c>SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt</c> leaked a LISTEN connection until <c>max_connections</c>
/// was reached.
/// </summary>
public sealed class PostgresStorageConnectionLeakTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly PostgresStorageHarness _harness;
   private readonly DatabaseConnection _db;

   public PostgresStorageConnectionLeakTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _db = fixture.DatabaseConnection;
      _harness = new PostgresStorageHarness(fixture);
   }

   public async ValueTask InitializeAsync()
   {
      await _fixture.ResetAsync();
      await _harness.InstanceRepository.RegisterInstance();
   }

   public ValueTask DisposeAsync() => ValueTask.CompletedTask;

   [Fact]
   public async Task WaitForNextJob_DoesNotLeakConnections_WhenJobIsScheduledInTheFuture()
   {
      // Arrange - schedule a job in the (near) future so the poll loop falls into the
      // "wait for delay or notification" branch on every iteration.
      var futureJob = JobStoreItemFactory.MakeTestJob(
         jobName: "FutureJob",
         performAt: _harness.Clock.UtcNow.AddMilliseconds(150)
      );
      await _harness.Storage.ScheduleJobAsync(futureJob);

      var baselineConnections = await CountConnectionsAsync();

      // Act - run the poll loop for ~3 seconds. With the leak, each iteration would add a connection.
      // WaitForNextJobAsync swallows OCE internally and returns null on cancellation, so no try/catch is needed.
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
      await _harness.Storage.WaitForNextJobAsync(cts.Token);

      // Allow a brief moment for any in-flight close to settle.
      await Task.Delay(TimeSpan.FromMilliseconds(250));

      var connectionsAfter = await CountConnectionsAsync();

      // Assert - we should not be accumulating connections per iteration.
      // Allow a small slack (heartbeat / harness connections) but anything beyond ~5 above baseline
      // indicates the LISTEN connections are leaking.
      (connectionsAfter - baselineConnections).Should().BeLessThan(5,
         "the polling loop must not leak a connection per iteration when a future job exists");
   }

   [Fact]
   public async Task WaitForNextJob_ReleasesConnections_OnCancellation()
   {
      // Arrange
      var baselineConnections = await CountConnectionsAsync();

      // Schedule a future job to drive the loop through the delay/listen race.
      var futureJob = JobStoreItemFactory.MakeTestJob(
         jobName: "FutureJob",
         performAt: _harness.Clock.UtcNow.AddMilliseconds(150)
      );
      await _harness.Storage.ScheduleJobAsync(futureJob);

      // Act - WaitForNextJobAsync swallows OCE internally and returns null on cancellation.
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
      await _harness.Storage.WaitForNextJobAsync(cts.Token);

      // Give Npgsql a moment to release pooled connectors after cancellation.
      await Task.Delay(TimeSpan.FromMilliseconds(500));

      // Assert
      var connectionsAfter = await CountConnectionsAsync();
      (connectionsAfter - baselineConnections).Should().BeLessThanOrEqualTo(2,
         "the connection count should return to baseline after the polling loop is cancelled");
   }

   private async Task<int> CountConnectionsAsync()
   {
      var count = await _db.Dapper.QueryFirstAsync<long>(
         "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid()"
      );
      return (int)count;
   }
}
