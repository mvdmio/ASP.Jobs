using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
///    Tests for the Resolution Grace behaviour: a Worker Instance that Claims a due job whose job class or
///    parameters class it cannot load defers the job instead of deleting it. See
///    docs/adr/0005-unresolvable-jobs-are-deferred-then-deleted-on-a-clock.md.
///    An Unresolvable row cannot be created through the scheduling API (every live type resolves), so such rows
///    are written with raw SQL through the fixture's database connection - matching the existing Postgres storage
///    tests' approach for rows that need to be shaped directly. Resolvable rows go through the storage itself.
/// </summary>
public sealed class PostgresUnresolvableJobTests : IAsyncLifetime
{
   private const string UnresolvableJobType = "mvdmio.NoSuchNamespace.NoSuchJob, mvdmio.NoSuchAssembly";
   private const string UnresolvableParametersType = "mvdmio.NoSuchNamespace.NoSuchParameters, mvdmio.NoSuchAssembly";

   private readonly PostgresFixture _fixture;
   private readonly CancellationTokenSource _cts;
   private readonly PostgresStorageHarness _harness;
   private readonly DatabaseConnection _db;

   private TestClock Clock => _harness.Clock;
   private PostgresJobStorage Storage => _harness.Storage;

   private CancellationToken CancellationToken => _cts.Token;

   public PostgresUnresolvableJobTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      _db = fixture.DatabaseConnection;
      _harness = new PostgresStorageHarness(fixture);

      _cts.CancelAfter(TimeSpan.FromSeconds(1));
   }

   public async ValueTask InitializeAsync()
   {
      await _fixture.ResetAsync();
      await _harness.Storage.InitializeAsync(CancellationToken);
      await _harness.InstanceRepository.RegisterInstance(CancellationToken);
   }

   public ValueTask DisposeAsync() => ValueTask.CompletedTask;

   [Fact]
   public async Task WaitForNextJob_ReturnsResolvableJob_WhenUnresolvableJobIsDueEarlier()
   {
      // Arrange - the Unresolvable job is due earlier, so the Claim query reaches it first.
      var unresolvablePerformAt = Clock.UtcNow.Subtract(TimeSpan.FromMinutes(10));
      await InsertUnresolvableJobAsync("UnresolvableJob", unresolvablePerformAt);

      var resolvableJob = JobStoreItemFactory.MakeTestJob(jobName: "ResolvableJob", performAt: Clock.UtcNow);
      await Storage.ScheduleJobAsync(resolvableJob, CancellationToken);

      // Act
      var claimedJob = await Storage.WaitForNextJobAsync(CancellationToken);

      // Assert - the resolvable job is returned...
      claimedJob.Should().NotBeNull();
      claimedJob!.JobId.Should().Be(resolvableJob.JobId);

      // ...and the Unresolvable row is still exactly where it was: unclaimed, due, scheduled time and
      // attempt untouched, stamped with the moment it was found Unresolvable.
      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(2);

      var unresolvableRow = jobs.Single(x => x.JobName == "UnresolvableJob");
      unresolvableRow.StartedAt.Should().BeNull();
      unresolvableRow.StartedBy.Should().BeNull();
      unresolvableRow.PerformAt.Should().BeCloseTo(unresolvablePerformAt, TimeSpan.FromSeconds(1));
      unresolvableRow.Attempt.Should().Be(0);
      unresolvableRow.UnresolvableSince.Should().NotBeNull();
      unresolvableRow.UnresolvableSince!.Value.Should().BeCloseTo(Clock.UtcNow, TimeSpan.FromSeconds(1));
   }

   [Fact]
   public async Task WaitForNextJob_DoesNotReclaimSkippedJob_WhenCalledAgainWithinResolutionGraceWindow()
   {
      // Arrange
      await InsertUnresolvableJobAsync("UnresolvableJob", Clock.UtcNow);

      // Act - first call defers the job and, with nothing else pending, returns null once the token fires.
      var firstResult = await Storage.WaitForNextJobAsync(CancellationToken);

      // A second call, still inside the window, must not take the job it just skipped.
      using var secondCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      secondCts.CancelAfter(TimeSpan.FromMilliseconds(300));
      var secondResult = await Storage.WaitForNextJobAsync(secondCts.Token);

      // Assert
      firstResult.Should().BeNull();
      secondResult.Should().BeNull();

      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs[0].StartedAt.Should().BeNull();
      jobs[0].StartedBy.Should().BeNull();
   }

   [Fact]
   public async Task WaitForNextJob_KeepsOriginalStamp_WhenDeferredAgainAfterTheSkipEntryExpires()
   {
      // Arrange
      var performAt = Clock.UtcNow;
      await InsertUnresolvableJobAsync("UnresolvableJob", performAt);

      await Storage.WaitForNextJobAsync(CancellationToken);
      var originalStamp = GetJobsFromDatabase().Single().UnresolvableSince;
      originalStamp.Should().NotBeNull();

      // Act - move the storage clock past the Resolution Grace window, so this instance's own skip entry has
      // expired and it re-Claims the same row.
      Clock.UtcNow = Clock.UtcNow.AddMinutes(6);

      using var secondCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      secondCts.CancelAfter(TimeSpan.FromMilliseconds(300));
      await Storage.WaitForNextJobAsync(secondCts.Token);

      // Assert - the stamp is unchanged: it is only ever written when it was null.
      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs[0].StartedAt.Should().BeNull();
      jobs[0].PerformAt.Should().BeCloseTo(performAt, TimeSpan.FromSeconds(1));
      jobs[0].UnresolvableSince!.Value.Should().BeCloseTo(originalStamp!.Value, TimeSpan.FromSeconds(1));
   }

   [Fact]
   public async Task WaitForNextJob_WithOnlyUnresolvableJobsDue_ReturnsNull_WithoutDeletingAnyRow()
   {
      // Arrange - every due row is Unresolvable and inside the window.
      await InsertUnresolvableJobAsync("UnresolvableJobOne", Clock.UtcNow);
      await InsertUnresolvableJobAsync("UnresolvableJobTwo", Clock.UtcNow);

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      cts.CancelAfter(TimeSpan.FromMilliseconds(500));

      // Act
      var result = await Storage.WaitForNextJobAsync(cts.Token);

      // Assert - nothing is deleted, and the call actually returned (rather than spinning hot on the Claim
      // query for the whole Resolution Grace window) once cancellation fired.
      result.Should().BeNull();
      GetJobsFromDatabase().Should().HaveCount(2);
   }

   [Fact]
   public async Task WaitForNextJob_RaisesJobsUpdatedNotification_WhenDeferringAnUnresolvableJob()
   {
      // Arrange
      await InsertUnresolvableJobAsync("UnresolvableJob", Clock.UtcNow);

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      cts.CancelAfter(TimeSpan.FromSeconds(5));

      // Act - start listening before triggering the defer so the NOTIFY is not missed.
      var listenTask = _db.WaitAsync("jobs_updated", TimeSpan.FromSeconds(5), cts.Token);
      await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);
      var waitTask = Storage.WaitForNextJobAsync(cts.Token);

      // Assert
      var wasNotified = await listenTask;
      wasNotified.Should().BeTrue();

      await waitTask;
   }

   [Fact]
   public async Task WaitForNextJob_DeletesTheClaimedRow_WhenAnotherPendingJobWithSameNameExists()
   {
      // Arrange - the rolling-deploy shape: an old instance holds the Claim on a job it cannot load, while a new
      // instance has already re-enqueued the same job name.
      await InsertUnresolvableJobAsync("SharedJobName", Clock.UtcNow);

      var supersedingJob = JobStoreItemFactory.MakeTestJob(jobName: "SharedJobName", performAt: Clock.UtcNow.AddHours(1));
      await Storage.ScheduleJobAsync(supersedingJob, CancellationToken);

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      cts.CancelAfter(TimeSpan.FromMilliseconds(300));

      // Act
      var result = await Storage.WaitForNextJobAsync(cts.Token);

      // Assert - the Unresolvable Claimed row is deleted rather than released, and the newer pending row is
      // untouched: same id, same perform_at, Claim fields still null, and it remains the only pending row for
      // that job name.
      result.Should().BeNull();

      var jobs = GetJobsFromDatabase();
      jobs.Should().ContainSingle();
      jobs[0].Id.Should().Be(supersedingJob.JobId);
      jobs[0].JobName.Should().Be("SharedJobName");
      jobs[0].PerformAt.Should().BeCloseTo(supersedingJob.PerformAt, TimeSpan.FromSeconds(1));
      jobs[0].StartedAt.Should().BeNull();
      jobs[0].StartedBy.Should().BeNull();
   }

   [Fact]
   public async Task WaitForNextJob_ContinuesTheWaitLoop_AfterSupersedingAnUnresolvableClaim()
   {
      // Arrange - the superseded row is due earlier, so the Claim query reaches it first; another resolvable job
      // is also due.
      await InsertUnresolvableJobAsync("SharedJobName", Clock.UtcNow.Subtract(TimeSpan.FromMinutes(10)));

      var supersedingJob = JobStoreItemFactory.MakeTestJob(jobName: "SharedJobName", performAt: Clock.UtcNow.AddHours(1));
      await Storage.ScheduleJobAsync(supersedingJob, CancellationToken);

      var otherResolvableJob = JobStoreItemFactory.MakeTestJob(jobName: "OtherResolvableJob", performAt: Clock.UtcNow);
      await Storage.ScheduleJobAsync(otherResolvableJob, CancellationToken);

      // Act - the wait loop supersedes (deletes) the Unresolvable row and carries on to return the other due job.
      var result = await Storage.WaitForNextJobAsync(CancellationToken);

      // Assert
      result.Should().NotBeNull();
      result!.JobId.Should().Be(otherResolvableJob.JobId);

      var jobs = GetJobsFromDatabase();
      jobs.Should().ContainSingle(x => x.JobName == "SharedJobName" && x.Id == supersedingJob.JobId);
   }

   [Fact]
   public async Task WaitForNextJob_SupersedesSafely_UnderConcurrentSchedulingRace()
   {
      // Repeats a "defer vs. fresh schedule" race under real concurrency: the NOT EXISTS guard usually catches
      // the conflict, but occasionally the fresh schedule's INSERT lands between the guard's check and the
      // UPDATE's commit, which must fall back to the unique-violation path. Either way, the partial unique index
      // must guarantee exactly one not-started row per name.
      for (var i = 0; i < 5; i++)
      {
         var jobName = $"RaceJob-{i}";
         // Use TestContext's own token for setup, not the class-level CancellationToken field: that one is bound
         // to a single 1-second budget for the whole test, and this loop's per-iteration wait calls (below)
         // consume most of it across 5 iterations.
         await InsertUnresolvableJobAsync(jobName, Clock.UtcNow, ct: TestContext.Current.CancellationToken);

         // Not-yet-due, so the wait loop's own Claim query can never pick this row up after superseding the
         // Unresolvable one - keeping this test isolated to the guard/unique-violation race, the same shape as
         // TryScheduleRetryAsync_ShouldSupersedeSafely_UnderConcurrentSchedulingRace above.
         var supersedingJob = JobStoreItemFactory.MakeTestJob(jobName: jobName, performAt: Clock.UtcNow.AddHours(1));

         using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
         cts.CancelAfter(TimeSpan.FromMilliseconds(300));

         var waitTask = Storage.WaitForNextJobAsync(cts.Token);
         var scheduleTask = Storage.ScheduleJobAsync(supersedingJob, TestContext.Current.CancellationToken);

         await Task.WhenAll(waitTask, scheduleTask);

         var pendingJobsForName = _db.Dapper.Query<JobData>(
            "SELECT * FROM mvdmio.jobs WHERE job_name = :job_name AND started_at IS NULL",
            new Dictionary<string, object?> { { "job_name", jobName } }
         ).ToList();

         pendingJobsForName.Should().HaveCount(1);
      }
   }

   [Fact]
   public async Task Listing_SkipsUnresolvableRows_AndLeavesThemInTheTable()
   {
      // Arrange
      await InsertUnresolvableJobAsync("UnresolvablePending", Clock.UtcNow.AddHours(1));

      var resolvableJob = JobStoreItemFactory.MakeTestJob(jobName: "ResolvablePending", performAt: Clock.UtcNow.AddHours(1));
      await Storage.ScheduleJobAsync(resolvableJob, CancellationToken);

      // Act / Assert - GetScheduledJobsAsync skips the row it cannot load but never deletes it.
      var scheduled = (await Storage.GetScheduledJobsAsync(CancellationToken)).ToList();
      scheduled.Should().ContainSingle(x => x.JobId == resolvableJob.JobId);
      GetJobsFromDatabase().Should().HaveCount(2);

      // Arrange - simulate an in-progress row whose type cannot be loaded either.
      await _db.Dapper.ExecuteAsync(
         """
         UPDATE mvdmio.jobs
         SET started_at = :now, started_by = :instance_id
         WHERE job_name = :job_name
         """,
         new Dictionary<string, object?> {
            { "now", Clock.UtcNow },
            { "instance_id", _harness.Configuration.InstanceId },
            { "job_name", "UnresolvablePending" }
         },
         ct: CancellationToken
      );

      // Act / Assert - GetInProgressJobsAsync does the same.
      var inProgress = await Storage.GetInProgressJobsAsync(CancellationToken);
      inProgress.Should().BeEmpty();
      GetJobsFromDatabase().Should().HaveCount(2);
   }

   [Fact]
   public async Task UnresolvableSinceColumn_DefaultsToNull_ForRowsThatDoNotSpecifyIt()
   {
      // Regression guard for the _202609151200_AddUnresolvableSince migration: a row written before the column
      // existed (or by any insert that omits it) reads back as null rather than failing.
      await _db.Dapper.ExecuteAsync(
         """
         INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at)
         VALUES (:id, :job_type, :parameters_json, :parameters_type, NULL, :application_name, :job_name, NULL, :perform_at)
         """,
         new Dictionary<string, object?> {
            { "id", Guid.NewGuid() },
            { "job_type", typeof(object).AssemblyQualifiedName },
            { "parameters_json", new TypedQueryParameter("{}", NpgsqlDbType.Jsonb) },
            { "parameters_type", typeof(object).AssemblyQualifiedName },
            { "application_name", _harness.Configuration.ApplicationName },
            { "job_name", "LegacyRow" },
            { "perform_at", Clock.UtcNow }
         },
         ct: CancellationToken
      );

      GetJobsFromDatabase().Single().UnresolvableSince.Should().BeNull();
   }

   private async Task InsertUnresolvableJobAsync(string jobName, DateTime performAt, DateTime? unresolvableSince = null, CancellationToken? ct = null)
   {
      await _db.Dapper.ExecuteAsync(
         """
         INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, unresolvable_since)
         VALUES (:id, :job_type, :parameters_json, :parameters_type, NULL, :application_name, :job_name, NULL, :perform_at, :unresolvable_since)
         """,
         new Dictionary<string, object?> {
            { "id", Guid.NewGuid() },
            { "job_type", UnresolvableJobType },
            { "parameters_json", new TypedQueryParameter("{}", NpgsqlDbType.Jsonb) },
            { "parameters_type", UnresolvableParametersType },
            { "application_name", _harness.Configuration.ApplicationName },
            { "job_name", jobName },
            { "perform_at", performAt },
            { "unresolvable_since", unresolvableSince }
         },
         ct: ct ?? CancellationToken
      );
   }

   private List<JobData> GetJobsFromDatabase() => _db.Dapper.Query<JobData>("SELECT * FROM mvdmio.jobs").ToList();
}
