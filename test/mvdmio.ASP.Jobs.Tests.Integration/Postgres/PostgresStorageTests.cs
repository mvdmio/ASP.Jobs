using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.Database.PgSQL;
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

   private List<JobData> GetJobsFromDatabase() => _db.Dapper.Query<JobData>("SELECT * FROM mvdmio.jobs").ToList();
}
