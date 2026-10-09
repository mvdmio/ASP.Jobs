using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;
using NSubstitute;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
///    Tests that PostgreSQL storage enforces <see cref="JobScheduleOptions.Group"/>: a due job whose group already has a
///    claimed job is not claimed. Mirrors the group tests of <c>InMemoryJobStorageTests</c>.
/// </summary>
public sealed class PostgresJobGroupTests : IAsyncLifetime
{
   private const string Group = "test-group";

   private readonly PostgresFixture _fixture;
   private readonly CancellationTokenSource _cts;
   private readonly PostgresStorageHarness _harness;
   private readonly DatabaseConnection _db;
   private readonly List<CancellationTokenSource> _waitTokenSources = [];

   private TestClock Clock => _harness.Clock;
   private PostgresJobStorage Storage => _harness.Storage;

   private CancellationToken CancellationToken => _cts.Token;

   public PostgresJobGroupTests(PostgresFixture fixture)
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
      await _harness.InstanceRepository.RegisterInstance(CancellationToken);
   }

   public ValueTask DisposeAsync()
   {
      foreach (var cts in _waitTokenSources)
         cts.Dispose();

      _cts.Dispose();
      return ValueTask.CompletedTask;
   }

   [Fact]
   public async Task WaitForNextJob_SkipsGroupMate_UntilTheRunningJobIsFinalized()
   {
      // Arrange
      var first = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-2));
      var second = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-1));
      await Storage.ScheduleJobsAsync([second, first], CancellationToken);

      // Act & Assert - the earlier job is claimed first.
      var firstClaim = await Storage.WaitForNextJobAsync(CancellationToken);
      firstClaim!.JobId.Should().Be(first.JobId);

      // While it is claimed, its group-mate is not.
      var blockedClaim = await Storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromMilliseconds(500)));
      blockedClaim.Should().BeNull();

      // Once it is finalized, the group-mate is claimed.
      await Storage.FinalizeJobAsync(firstClaim, CancellationToken);
      var secondClaim = await Storage.WaitForNextJobAsync(CancellationToken);
      secondClaim!.JobId.Should().Be(second.JobId);
   }

   [Fact]
   public async Task WaitForNextJob_ClaimsUngroupedJobsAndOtherGroups_WhileAGroupIsBusy()
   {
      // Arrange
      var running = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-3));
      var groupMate = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-2));
      var ungrouped = JobStoreItemFactory.MakeTestJob(performAt: Clock.UtcNow.AddMinutes(-1));
      var otherGroup = JobStoreItemFactory.MakeTestJob(group: "other-group", performAt: Clock.UtcNow);
      await Storage.ScheduleJobsAsync([running, groupMate, ungrouped, otherGroup], CancellationToken);

      // Act
      var claims = new List<Guid?>();
      for (var i = 0; i < 3; i++)
      {
         var claim = await Storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromMilliseconds(500)));
         claims.Add(claim?.JobId);
      }

      // Assert
      claims.Should().Equal(running.JobId, ungrouped.JobId, otherGroup.JobId);
   }

   [Fact]
   public async Task WaitForNextJob_IsNotBlocked_ByTheSameGroupUnderAnotherApplicationName()
   {
      // Arrange - another application holds a claim in a group with the same name.
      var otherApplication = new PostgresStorageHarness(_fixture, instanceId: "other-instance", applicationName: "other-application");
      await otherApplication.Storage.InitializeAsync(CancellationToken);
      await otherApplication.InstanceRepository.RegisterInstance(CancellationToken);
      await otherApplication.Storage.ScheduleJobAsync(JobStoreItemFactory.MakeTestJob(group: Group, performAt: otherApplication.Clock.UtcNow), CancellationToken);
      var otherClaim = await otherApplication.Storage.WaitForNextJobAsync(CancellationToken);
      otherClaim.Should().NotBeNull();

      var job = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow);
      await Storage.ScheduleJobAsync(job, CancellationToken);

      // Act
      var claim = await Storage.WaitForNextJobAsync(CancellationToken);

      // Assert
      claim!.JobId.Should().Be(job.JobId);
   }

   [Fact]
   public async Task FinalizeJob_SendsExactlyOneNotification_ForAGroupedJob()
   {
      // Arrange
      await Storage.ScheduleJobAsync(JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow), CancellationToken);
      var claim = await Storage.WaitForNextJobAsync(CancellationToken);
      await using var listener = await JobsUpdatedListener.StartAsync(_fixture.ConnectionString, CancellationToken);

      // Act
      await Storage.FinalizeJobAsync(claim!, CancellationToken);

      // Assert
      var notifications = await listener.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), CancellationToken);
      notifications.Should().Be(1);
   }

   [Fact]
   public async Task FinalizeJob_SendsNoNotification_ForAnUngroupedJob()
   {
      // Arrange
      await Storage.ScheduleJobAsync(JobStoreItemFactory.MakeTestJob(performAt: Clock.UtcNow), CancellationToken);
      var claim = await Storage.WaitForNextJobAsync(CancellationToken);
      await using var listener = await JobsUpdatedListener.StartAsync(_fixture.ConnectionString, CancellationToken);

      // Act
      await Storage.FinalizeJobAsync(claim!, CancellationToken);

      // Assert
      var notifications = await listener.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), CancellationToken);
      notifications.Should().Be(0);
   }

   [Fact]
   public async Task WaitForNextJob_AlreadyWaiting_ReturnsGroupMateSoonAfterTheRunningJobIsFinalized()
   {
      // Arrange
      var first = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-2));
      var second = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-1));
      await Storage.ScheduleJobsAsync([first, second], CancellationToken);
      var firstClaim = await Storage.WaitForNextJobAsync(CancellationToken);

      var waitTask = Storage.WaitForNextJobAsync(CancellationToken);
      await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);
      waitTask.IsCompleted.Should().BeFalse();

      // Act
      await Storage.FinalizeJobAsync(firstClaim!, CancellationToken);

      // Assert - the notification wakes the wait well before the token's 10 second timeout.
      var secondClaim = await waitTask.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken);
      secondClaim!.JobId.Should().Be(second.JobId);
   }

   [Fact]
   public async Task WaitForNextJob_Sleeps_WhenTheOnlyDueJobIsABlockedGroupMate()
   {
      // Arrange - every pass reads the clock once, so the clock's calls count the passes.
      var clock = Substitute.For<IClock>();
      clock.UtcNow.Returns(Clock.UtcNow);
      var storage = new PostgresJobStorage(_fixture.DatabaseConnectionFactory, Options.Create(_harness.Configuration), NullLoggerFactory.Instance, clock);
      await storage.InitializeAsync(CancellationToken);

      await storage.ScheduleJobsAsync(
         [
            JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-2)),
            JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-1))
         ],
         CancellationToken
      );
      (await storage.WaitForNextJobAsync(CancellationToken)).Should().NotBeNull();
      clock.ClearReceivedCalls();

      // Act
      var claim = await storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromMilliseconds(500)));

      // Assert - a spinning loop would read the clock hundreds of times; a stray jobs_updated may add one pass.
      claim.Should().BeNull();
      clock.ReceivedCalls().Count().Should().BeInRange(1, 2);
   }

   [Fact]
   public async Task Initialization_CreatesANonUniquePartialIndexOnStartedGroupRows()
   {
      // Act
      var index = await _db.Dapper.QueryFirstOrDefaultAsync<IndexRow>(
         """
         SELECT ix.indisunique AS IsUnique, pg_get_indexdef(ix.indexrelid) AS Definition
         FROM pg_index ix
         JOIN pg_class c ON c.oid = ix.indexrelid
         JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE n.nspname = 'mvdmio'
           AND c.relname = 'idx_jobs__application__job_group__started'
         """,
         ct: CancellationToken
      );

      // Assert
      index.Should().NotBeNull();
      index!.IsUnique.Should().BeFalse();
      index.Definition.Should().Be(
         "CREATE INDEX idx_jobs__application__job_group__started ON mvdmio.jobs USING btree (application_name, job_group) WHERE ((started_at IS NOT NULL) AND (job_group IS NOT NULL))"
      );
   }

   [Fact]
   public async Task WaitForNextJob_ClaimsGroupMate_AfterTheRunningJobIsRescheduledForARetry()
   {
      // Arrange
      var first = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-2));
      var second = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-1));
      await Storage.ScheduleJobsAsync([first, second], CancellationToken);
      var firstClaim = await Storage.WaitForNextJobAsync(CancellationToken);

      // Act - the running job fails and is rescheduled a minute from now.
      var rescheduled = await Storage.TryScheduleRetryAsync(firstClaim!, Clock.UtcNow.AddMinutes(1), CancellationToken);
      var secondClaim = await Storage.WaitForNextJobAsync(CancellationToken);

      // Assert - the group-mate is claimed next.
      rescheduled.Should().BeTrue();
      secondClaim!.JobId.Should().Be(second.JobId);

      // Once the retry comes due, it waits while its group-mate runs, and is claimed after it is finalized.
      Clock.UtcNow = Clock.UtcNow.AddMinutes(2);
      var blockedClaim = await Storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromMilliseconds(500)));
      blockedClaim.Should().BeNull();

      await Storage.FinalizeJobAsync(secondClaim, CancellationToken);
      var retryClaim = await Storage.WaitForNextJobAsync(CancellationToken);
      retryClaim!.JobId.Should().Be(first.JobId);
   }

   [Theory]
   [InlineData(Group, 1)]
   [InlineData(null, 0)]
   public async Task TryScheduleRetry_NotifiesOnlyForAGroupedJob_WhenTheRetryIsSuperseded(string? group, int expectedNotifications)
   {
      // Arrange - a newer pending job of the same name supersedes the retry, so the claimed row is deleted.
      await Storage.ScheduleJobAsync(JobStoreItemFactory.MakeTestJob(jobName: "chain", group: group, performAt: Clock.UtcNow), CancellationToken);
      var claim = await Storage.WaitForNextJobAsync(CancellationToken);
      await Storage.ScheduleJobAsync(JobStoreItemFactory.MakeTestJob(jobName: "chain", group: group, performAt: Clock.UtcNow.AddHours(1)), CancellationToken);
      await using var listener = await JobsUpdatedListener.StartAsync(_fixture.ConnectionString, CancellationToken);

      // Act
      var rescheduled = await Storage.TryScheduleRetryAsync(claim!, Clock.UtcNow.AddMinutes(1), CancellationToken);

      // Assert
      rescheduled.Should().BeFalse();
      var notifications = await listener.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), CancellationToken);
      notifications.Should().Be(expectedNotifications);
   }

   [Fact]
   public async Task CleanupOldInstances_FreesTheGroupAndNotifies_WhenAnExpiredInstanceHeldAGroupedClaim()
   {
      // Arrange - an instance whose heartbeat expired ten minutes ago still holds a claim in the group.
      var dead = await StartDeadInstanceAsync();
      var running = JobStoreItemFactory.MakeTestJob(group: Group, performAt: dead.Clock.UtcNow);
      await dead.Storage.ScheduleJobAsync(running, CancellationToken);
      (await dead.Storage.WaitForNextJobAsync(CancellationToken))!.JobId.Should().Be(running.JobId);

      var groupMate = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-1));
      await Storage.ScheduleJobAsync(groupMate, CancellationToken);

      var blockedClaim = await Storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromMilliseconds(500)));
      blockedClaim.Should().BeNull();

      await using var listener = await JobsUpdatedListener.StartAsync(_fixture.ConnectionString, CancellationToken);

      // Act
      await _harness.InstanceRepository.CleanupOldInstances(CancellationToken);

      // Assert - the reset job is the earliest in the now free group, so it is claimed first.
      var notifications = await listener.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), CancellationToken);
      notifications.Should().Be(1);

      var claim = await Storage.WaitForNextJobAsync(CancellationToken);
      claim!.JobId.Should().Be(running.JobId);
   }

   [Fact]
   public async Task CleanupOldInstances_SendsNoNotification_WhenNoResetRowHadAGroup()
   {
      // Arrange
      var dead = await StartDeadInstanceAsync();
      await dead.Storage.ScheduleJobAsync(JobStoreItemFactory.MakeTestJob(performAt: dead.Clock.UtcNow), CancellationToken);
      (await dead.Storage.WaitForNextJobAsync(CancellationToken)).Should().NotBeNull();
      await using var listener = await JobsUpdatedListener.StartAsync(_fixture.ConnectionString, CancellationToken);

      // Act
      await _harness.InstanceRepository.CleanupOldInstances(CancellationToken);

      // Assert - the claim was reset, yet nothing was sent.
      var notifications = await listener.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), CancellationToken);
      notifications.Should().Be(0);
      (await _db.Dapper.QueryFirstAsync<long>("SELECT COUNT(*) FROM mvdmio.jobs WHERE started_at IS NOT NULL", ct: CancellationToken)).Should().Be(0);
   }

   [Theory]
   [InlineData(Group, 1)]
   [InlineData(null, 0)]
   public async Task ReleaseStartedJobs_NotifiesOnlyWhenAReleasedRowHadAGroup(string? group, int expectedNotifications)
   {
      // Arrange
      await Storage.ScheduleJobAsync(JobStoreItemFactory.MakeTestJob(group: group, performAt: Clock.UtcNow), CancellationToken);
      (await Storage.WaitForNextJobAsync(CancellationToken)).Should().NotBeNull();
      await using var listener = await JobsUpdatedListener.StartAsync(_fixture.ConnectionString, CancellationToken);

      // Act
      await _harness.InstanceRepository.ReleaseStartedJobs(CancellationToken);

      // Assert
      var notifications = await listener.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), CancellationToken);
      notifications.Should().Be(expectedNotifications);
   }

   [Fact]
   public async Task WaitForNextJob_ReturnsGroupMate_WhenAGroupedUnresolvableJobIsDueAhead()
   {
      // Arrange
      await UnresolvableJobRows.InsertAsync(_db, _harness.Configuration.ApplicationName, "unresolvable", Clock.UtcNow.AddMinutes(-2), group: Group, ct: CancellationToken);
      var groupMate = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-1));
      await Storage.ScheduleJobAsync(groupMate, CancellationToken);

      // Act - the wait defers the unresolvable job, which frees the group.
      var claim = await Storage.WaitForNextJobAsync(CancellationToken);

      // Assert
      claim!.JobId.Should().Be(groupMate.JobId);
   }

   [Theory]
   [InlineData(Group, 1)]
   [InlineData(null, 0)]
   public async Task WaitForNextJob_NotifiesOnlyForAGroupedJob_WhenItDeletesAnUnresolvableJobPastItsGrace(string? group, int expectedNotifications)
   {
      // Arrange
      await UnresolvableJobRows.InsertAsync(_db, _harness.Configuration.ApplicationName, "unresolvable", Clock.UtcNow, group, Clock.UtcNow.AddMinutes(-10), CancellationToken);
      await using var listener = await JobsUpdatedListener.StartAsync(_fixture.ConnectionString, CancellationToken);

      // Act
      var claim = await Storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromMilliseconds(500)));

      // Assert
      claim.Should().BeNull();
      (await _db.Dapper.QueryFirstAsync<long>("SELECT COUNT(*) FROM mvdmio.jobs", ct: CancellationToken)).Should().Be(0);

      var notifications = await listener.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), CancellationToken);
      notifications.Should().Be(expectedNotifications);
   }

   [Fact]
   public async Task WaitForNextJob_TwoInstancesClaimingConcurrently_NeverHoldTwoClaimsInOneGroup()
   {
      // Arrange
      const int jobCount = 20;
      var peer = new PostgresStorageHarness(_fixture, instanceId: "peer-instance");
      await peer.Storage.InitializeAsync(CancellationToken);
      await peer.InstanceRepository.RegisterInstance(CancellationToken);

      var jobs = Enumerable.Range(0, jobCount)
         .Select(i => JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-1).AddSeconds(i)))
         .ToList();
      await Storage.ScheduleJobsAsync(jobs, CancellationToken);

      using var allClaimed = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      var claimedInOrder = new List<Guid>();
      var held = 0;
      var maxHeld = 0;

      async Task WorkAsync(PostgresJobStorage storage)
      {
         while (true)
         {
            var claim = await storage.WaitForNextJobAsync(allClaimed.Token);
            if (claim is null)
               return;

            var nowHeld = Interlocked.Increment(ref held);
            InterlockedMax(ref maxHeld, nowHeld);

            lock (claimedInOrder)
            {
               claimedInOrder.Add(claim.JobId);
               if (claimedInOrder.Count == jobCount)
                  allClaimed.Cancel();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken);

            // Release the count before finalizing: a peer can only claim once the finalize has committed.
            Interlocked.Decrement(ref held);
            await storage.FinalizeJobAsync(claim, CancellationToken);
         }
      }

      // Act
      await Task.WhenAll(WorkAsync(Storage), WorkAsync(peer.Storage));

      // Assert
      maxHeld.Should().Be(1);
      claimedInOrder.Should().Equal(jobs.Select(x => x.JobId));
   }

   [Fact]
   public async Task WaitForNextJob_ClaimsNeitherTheEarliestJobNorItsGroupMate_WhileAnotherConnectionHoldsTheEarliestRow()
   {
      // Arrange
      var earliest = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-3));
      var groupMate = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-2));
      var ungrouped = JobStoreItemFactory.MakeTestJob(performAt: Clock.UtcNow.AddMinutes(-1));
      await Storage.ScheduleJobsAsync([earliest, groupMate, ungrouped], CancellationToken);

      // Act
      JobStoreItem? firstClaim = null;
      JobStoreItem? secondClaim = null;
      await WhileAPeerTransactionHoldsAsync(
         "SELECT 1 FROM mvdmio.jobs WHERE id = :id FOR UPDATE",
         new Dictionary<string, object?> { { "id", earliest.JobId } },
         async () => {
            firstClaim = await Storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromMilliseconds(500)));
            secondClaim = await Storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromMilliseconds(500)));
         }
      );

      // Assert
      firstClaim!.JobId.Should().Be(ungrouped.JobId);
      secondClaim.Should().BeNull();

      // Once the row lock is gone, the earliest job is claimed.
      var claim = await Storage.WaitForNextJobAsync(CancellationToken);
      claim!.JobId.Should().Be(earliest.JobId);
   }

   [Fact]
   public async Task WaitForNextJob_WaitsBetweenPassesInsteadOfSpinning_WhileAnotherConnectionHoldsTheEarliestRow()
   {
      // Arrange - every pass reads the clock once, so the clock's calls count the passes.
      var clock = Substitute.For<IClock>();
      clock.UtcNow.Returns(Clock.UtcNow);
      var storage = new PostgresJobStorage(_fixture.DatabaseConnectionFactory, Options.Create(_harness.Configuration), NullLoggerFactory.Instance, clock);
      await storage.InitializeAsync(CancellationToken);

      var earliest = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-2));
      var groupMate = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-1));
      await storage.ScheduleJobsAsync([earliest, groupMate], CancellationToken);

      // Act
      JobStoreItem? claim = null;
      await WhileAPeerTransactionHoldsAsync(
         "SELECT 1 FROM mvdmio.jobs WHERE id = :id FOR UPDATE",
         new Dictionary<string, object?> { { "id", earliest.JobId } },
         async () => claim = await storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromSeconds(1)))
      );

      // Assert - about ten passes in one second at the contended-claim retry delay; a spinning loop makes hundreds.
      claim.Should().BeNull();
      clock.ReceivedCalls().Count().Should().BeLessThan(20);
   }

   [Fact]
   public async Task WaitForNextJob_SkipsAGroupAndReturnsOtherWork_WhileAnotherTransactionHoldsTheGroupLock()
   {
      // Arrange
      var grouped = JobStoreItemFactory.MakeTestJob(group: Group, performAt: Clock.UtcNow.AddMinutes(-2));
      var ungrouped = JobStoreItemFactory.MakeTestJob(performAt: Clock.UtcNow.AddMinutes(-1));
      await Storage.ScheduleJobsAsync([grouped, ungrouped], CancellationToken);

      // Act - the earlier grouped job is passed over in the same call.
      JobStoreItem? claim = null;
      await WhileAPeerTransactionHoldsAsync(
         $"SELECT pg_advisory_xact_lock({PostgresJobClaimer.GroupLockKeyArguments})",
         new Dictionary<string, object?> {
            { "application_name", _harness.Configuration.ApplicationName },
            { "job_group", Group }
         },
         async () => claim = await Storage.WaitForNextJobAsync(CancelledAfter(TimeSpan.FromMilliseconds(500)))
      );

      // Assert
      claim!.JobId.Should().Be(ungrouped.JobId);

      // Once the lock is gone, the grouped job is claimed.
      var groupedClaim = await Storage.WaitForNextJobAsync(CancellationToken);
      groupedClaim!.JobId.Should().Be(grouped.JobId);
   }

   /// <summary>
   ///    Runs <paramref name="whileHeld"/> while a separate connection holds an open transaction that has run
   ///    <paramref name="lockSql"/>, then rolls that transaction back so whatever it locked is released.
   /// </summary>
   private async Task WhileAPeerTransactionHoldsAsync(string lockSql, Dictionary<string, object?> parameters, Func<Task> whileHeld)
   {
      await using var peerDb = _fixture.DatabaseConnectionFactory.BuildConnection(_fixture.ConnectionString);
      await peerDb.BeginTransactionAsync(ct: CancellationToken);

      try
      {
         await peerDb.Dapper.ExecuteAsync(lockSql, parameters, ct: CancellationToken);
         await whileHeld();
      }
      finally
      {
         await peerDb.RollbackTransactionAsync(CancellationToken);
      }
   }

   private static void InterlockedMax(ref int target, int value)
   {
      var current = Volatile.Read(ref target);
      while (value > current)
      {
         var previous = Interlocked.CompareExchange(ref target, value, current);
         if (previous == current)
            return;

         current = previous;
      }
   }

   /// <summary>
   ///    Registers a second Worker Instance of this application whose last heartbeat was ten minutes ago, so the next
   ///    cleanup pass treats it as expired. Its clock stays at that moment.
   /// </summary>
   private async Task<PostgresStorageHarness> StartDeadInstanceAsync()
   {
      var dead = new PostgresStorageHarness(_fixture, instanceId: "dead-instance");
      dead.Clock.UtcNow = Clock.UtcNow.AddMinutes(-10);
      await dead.Storage.InitializeAsync(CancellationToken);
      await dead.InstanceRepository.RegisterInstance(CancellationToken);
      return dead;
   }

   private CancellationToken CancelledAfter(TimeSpan delay)
   {
      var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      cts.CancelAfter(delay);
      _waitTokenSources.Add(cts);
      return cts.Token;
   }

   private sealed record IndexRow(bool IsUnique, string Definition);
}
