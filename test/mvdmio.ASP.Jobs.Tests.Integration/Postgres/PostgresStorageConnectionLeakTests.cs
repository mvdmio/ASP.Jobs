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
      await _harness.Storage.InitializeAsync();
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

   /// <summary>
   /// Regression test for the "A command is already in progress" failure that surfaced after the
   /// initial leak fix attempted to cache a single <see cref="DatabaseConnection"/> wrapper.
   /// The wrapper holds a single shared NpgsqlConnection field, so reusing it across concurrent
   /// callers would cause two operations to race on the same physical connection. The storage
   /// must allow concurrent calls (the runner consumes jobs while the scheduler enqueues new
   /// ones, plus parallel PerformAsapAsync calls).
   /// </summary>
   [Fact]
   public async Task ScheduleJob_AllowsConcurrentCallers()
   {
      // Arrange
      const int concurrentCallers = 16;
      const int jobsPerCaller = 10;

      // Act - many concurrent schedulers writing through the shared storage.
      var tasks = Enumerable.Range(0, concurrentCallers).Select(callerIndex => Task.Run(async () => {
         for (var i = 0; i < jobsPerCaller; i++)
         {
            var job = JobStoreItemFactory.MakeTestJob(
               jobName: $"caller-{callerIndex}-job-{i}",
               performAt: _harness.Clock.UtcNow
            );
            await _harness.Storage.ScheduleJobAsync(job);
         }
      })).ToArray();

      // Assert - none of the concurrent callers should hit "A command is already in progress".
      var act = () => Task.WhenAll(tasks);
      await act.Should().NotThrowAsync();
   }

   /// <summary>
   /// Regression test ensuring that scheduling can proceed concurrently with the polling loop
   /// (the realistic production scenario: <see cref="JobRunnerService"/> is in WaitForNextJobAsync
   /// while incoming HTTP requests call IJobScheduler.PerformAsapAsync).
   /// </summary>
   [Fact]
   public async Task ScheduleJob_AllowsConcurrentScheduling_WhilePolling()
   {
      // Arrange - kick off a poll loop with a future job so it parks in the delay/listen race.
      var futureJob = JobStoreItemFactory.MakeTestJob(
         jobName: "FutureJob",
         performAt: _harness.Clock.UtcNow.AddSeconds(30)
      );
      await _harness.Storage.ScheduleJobAsync(futureJob);

      using var pollCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
      var pollTask = _harness.Storage.WaitForNextJobAsync(pollCts.Token);

      // Act - schedule many additional jobs concurrently.
      var scheduleTasks = Enumerable.Range(0, 25).Select(i => Task.Run(async () => {
         var job = JobStoreItemFactory.MakeTestJob(
            jobName: $"concurrent-job-{i}",
            performAt: _harness.Clock.UtcNow.AddSeconds(60)
         );
         await _harness.Storage.ScheduleJobAsync(job);
      })).ToArray();

      // Assert
      var act = () => Task.WhenAll(scheduleTasks);
      await act.Should().NotThrowAsync();

      // Drain the poll task so the test cleans up; cancellation is expected.
      await pollTask;
   }

   private async Task<int> CountConnectionsAsync()
   {
      var count = await _db.Dapper.QueryFirstAsync<long>(
         "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid()"
      );
      return (int)count;
   }
}
