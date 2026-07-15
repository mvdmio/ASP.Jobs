using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

public sealed class PostgresStorageTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly CancellationTokenSource _cts;
   private readonly PostgresStorageHarness _harness;
   private readonly DatabaseConnection _db;

   private TestClock Clock => _harness.Clock;
   private PostgresJobStorage Storage => _harness.Storage;

   private CancellationToken CancellationToken => _cts.Token;

   public PostgresStorageTests(PostgresFixture fixture)
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
   public async Task ScheduleJob_BasicJob()
   {
      // Arrange
      var jobStoreItem = JobStoreItemFactory.MakeTestJob(performAt: Clock.UtcNow);

      // Act
      await Storage.ScheduleJobAsync(jobStoreItem, CancellationToken);

      // Assert
      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs.Select(x => x.ToJobStoreItem()).Should().BeEquivalentTo(new[] { jobStoreItem });
   }

   [Fact]
   public async Task ScheduleJob_ComplexJob()
   {
      // Arrange
      var jobStoreItem = JobStoreItemFactory.MakeTestJob(jobName: "ComplexJob", group: "ComplexGroup", performAt: Clock.UtcNow);

      // Act
      await Storage.ScheduleJobAsync(jobStoreItem, CancellationToken);

      // Assert
      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs.Select(x => x.ToJobStoreItem()).Should().BeEquivalentTo(new[] { jobStoreItem });
   }

   [Fact]
   public async Task ScheduleJob_ShouldUpdateNotStartedJob()
   {
      // Arrange
      var jobStoreItem1 = JobStoreItemFactory.MakeTestJob(jobName: "ComplexJob", performAt: Clock.UtcNow.Subtract(TimeSpan.FromDays(1)));
      var jobStoreItem2 = JobStoreItemFactory.MakeTestJob(jobName: "ComplexJob", performAt: Clock.UtcNow);

      // Act
      await Storage.ScheduleJobAsync(jobStoreItem1, CancellationToken);
      await Storage.ScheduleJobAsync(jobStoreItem2, CancellationToken);

      // Assert
      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs.Select(x => x.ToJobStoreItem()).Should().BeEquivalentTo(new[] { jobStoreItem2 });
   }

   [Fact]
   public async Task WaitForNextJob_ShouldReturnNull_WhenNoJobsAvailable()
   {
      // Act
      var job = await Storage.WaitForNextJobAsync(CancellationToken);

      // Assert
      job.Should().BeNull();
   }

   [Fact]
   public async Task WaitForNextJob_ShouldReturnNextJob_WhenAvailable()
   {
      // Arrange
      var job1 = JobStoreItemFactory.MakeTestJob(jobName: "TestJob", performAt: Clock.UtcNow);
      await Storage.ScheduleJobAsync(job1, CancellationToken);

      // Act
      var startedJob = await Storage.WaitForNextJobAsync(CancellationToken);

      // Assert
      startedJob.Should().BeEquivalentTo(job1);

      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs[0].StartedAt.Should().BeWithin(TimeSpan.FromSeconds(1)).Before(Clock.UtcNow);
   }

   [Fact]
   public async Task WaitForNextJob_ShouldNotStartSameJobTwice()
   {
      // Arrange
      var job1 = JobStoreItemFactory.MakeTestJob(jobName: "TestJob", performAt: Clock.UtcNow);
      await Storage.ScheduleJobAsync(job1, CancellationToken);

      // Act
      _ = await Storage.WaitForNextJobAsync(CancellationToken);
      var startedJob2 = await Storage.WaitForNextJobAsync(CancellationToken);

      // Assert
      startedJob2.Should().BeNull();
   }

   [Fact]
   public async Task FinalizeJob_ShouldDeleteJobFromDatabase()
   {
      // Arrange
      var job1 = JobStoreItemFactory.MakeTestJob(jobName: "TestJob", performAt: Clock.UtcNow);
      await Storage.ScheduleJobAsync(job1, CancellationToken);
      var startedJob1 = await Storage.WaitForNextJobAsync(CancellationToken);

      // Act
      await Storage.FinalizeJobAsync(startedJob1!, CancellationToken);

      // Assert
      GetJobsFromDatabase().Should().BeEmpty();
   }

   [Fact]
   public async Task TryScheduleRetryAsync_ShouldMoveJobBackToPending_WhenNoConflictingJobExists()
   {
      // Arrange
      var jobStoreItem = JobStoreItemFactory.MakeTestJob(jobName: "RetryJob", performAt: Clock.UtcNow);
      await Storage.ScheduleJobAsync(jobStoreItem, CancellationToken);
      var inProgress = await Storage.WaitForNextJobAsync(CancellationToken);

      var nextAttemptAt = Clock.UtcNow.AddMinutes(1);

      // Act
      var result = await Storage.TryScheduleRetryAsync(inProgress!, nextAttemptAt, CancellationToken);

      // Assert
      result.Should().BeTrue();

      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs[0].StartedAt.Should().BeNull();
      jobs[0].StartedBy.Should().BeNull();
      jobs[0].Attempt.Should().Be(1);
      jobs[0].PerformAt.Should().BeCloseTo(nextAttemptAt, TimeSpan.FromSeconds(1));
   }

   [Fact]
   public async Task TryScheduleRetryAsync_ShouldIncrementAttempt_OnEachRetry()
   {
      // Arrange
      var jobStoreItem = JobStoreItemFactory.MakeTestJob(jobName: "RetryJob", performAt: Clock.UtcNow);
      await Storage.ScheduleJobAsync(jobStoreItem, CancellationToken);

      // Act - retry twice
      var firstAttempt = await Storage.WaitForNextJobAsync(CancellationToken);
      await Storage.TryScheduleRetryAsync(firstAttempt!, Clock.UtcNow, CancellationToken);

      var secondAttempt = await Storage.WaitForNextJobAsync(CancellationToken);
      await Storage.TryScheduleRetryAsync(secondAttempt!, Clock.UtcNow, CancellationToken);

      // Assert
      GetJobsFromDatabase().Single().Attempt.Should().Be(2);
   }

   [Fact]
   public async Task TryScheduleRetryAsync_ShouldSupersedeChain_WhenAnotherPendingJobWithSameNameExists()
   {
      // Arrange
      var jobStoreItem = JobStoreItemFactory.MakeTestJob(jobName: "RetryJob", performAt: Clock.UtcNow);
      await Storage.ScheduleJobAsync(jobStoreItem, CancellationToken);
      var inProgress = await Storage.WaitForNextJobAsync(CancellationToken);

      // A newer job is scheduled under the same name while the original attempt is in progress.
      var supersedingJob = JobStoreItemFactory.MakeTestJob(jobName: "RetryJob", performAt: Clock.UtcNow.AddHours(1));
      await Storage.ScheduleJobAsync(supersedingJob, CancellationToken);

      // Act
      var result = await Storage.TryScheduleRetryAsync(inProgress!, Clock.UtcNow.AddMinutes(1), CancellationToken);

      // Assert
      result.Should().BeFalse();

      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs[0].Id.Should().Be(supersedingJob.JobId);
      jobs[0].StartedAt.Should().BeNull();
   }

   [Fact]
   public async Task TryScheduleRetryAsync_ShouldSupersedeSafely_UnderConcurrentSchedulingRace()
   {
      // Repeats a "retry vs. fresh schedule" race under real concurrency: the NOT EXISTS guard usually catches
      // the conflict, but occasionally the fresh schedule's INSERT lands between the guard's check and the
      // UPDATE's commit, which must fall back to the unique-violation path. Either way, the partial unique index
      // must guarantee exactly one not-started row per name.
      for (var i = 0; i < 5; i++)
      {
         var jobName = $"RaceJob-{i}";
         var jobStoreItem = JobStoreItemFactory.MakeTestJob(jobName: jobName, performAt: Clock.UtcNow);
         await Storage.ScheduleJobAsync(jobStoreItem, CancellationToken);
         var inProgress = await Storage.WaitForNextJobAsync(CancellationToken);

         var supersedingJob = JobStoreItemFactory.MakeTestJob(jobName: jobName, performAt: Clock.UtcNow);

         var retryTask = Storage.TryScheduleRetryAsync(inProgress!, Clock.UtcNow.AddMinutes(1), CancellationToken);
         var scheduleTask = Storage.ScheduleJobAsync(supersedingJob, CancellationToken);

         await Task.WhenAll(retryTask, scheduleTask);

         var pendingJobsForName = _db.Dapper.Query<JobData>(
            "SELECT * FROM mvdmio.jobs WHERE job_name = :job_name AND started_at IS NULL",
            new Dictionary<string, object?> { { "job_name", jobName } }
         ).ToList();

         pendingJobsForName.Should().HaveCount(1);
      }
   }

   [Fact]
   public async Task AttemptColumn_DefaultsToZero_ForRowsThatDoNotSpecifyIt()
   {
      // Regression guard for the _202607151200_AddRetryAttempt migration: it adds `attempt` as NOT NULL DEFAULT 0
      // specifically so that rows written before the column existed (or by any insert that omits it) don't need a
      // data rewrite - they pick up attempt = 0 for free.
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

      GetJobsFromDatabase().Single().Attempt.Should().Be(0);
   }

   private List<JobData> GetJobsFromDatabase() => _db.Dapper.Query<JobData>("SELECT * FROM mvdmio.jobs").ToList();
}
