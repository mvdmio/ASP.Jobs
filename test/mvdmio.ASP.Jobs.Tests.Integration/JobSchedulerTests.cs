using AwesomeAssertions;
using AwesomeAssertions.Equivalency;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration;

public abstract class JobSchedulerTests
{
   private readonly TestClock _clock;
   private readonly ServiceProvider _services;

   // Set by derived classes
   private IJobStorage _jobStorage = null!;
   private JobScheduler _scheduler = null!;

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   protected JobSchedulerTests()
   {
      _clock = new TestClock();
      _services = new JobTestServices().Services.BuildServiceProvider();
   }

   public sealed class PostgresJobSchedulerTests : JobSchedulerTests, IAsyncLifetime
   {
      private readonly PostgresFixture _fixture;
      private readonly PostgresStorageHarness _harness;

      public PostgresJobSchedulerTests(PostgresFixture fixture)
      {
         _fixture = fixture;
         _harness = new PostgresStorageHarness(fixture);

         // Reuse the harness clock so that scheduler/storage/test all see the same UtcNow.
         _harness.Clock.UtcNow = _clock.UtcNow;

         _jobStorage = _harness.Storage;
         _scheduler = new JobScheduler(_services, _jobStorage, _clock);
      }

      public async ValueTask InitializeAsync()
      {
         await _fixture.ResetAsync();
         await _harness.InstanceRepository.RegisterInstance(CancellationToken);
      }

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
   }

   public sealed class InMemoryJobSchedulerTests : JobSchedulerTests
   {
      public InMemoryJobSchedulerTests()
      {
         _jobStorage = new InMemoryJobStorage(_clock);
         _scheduler = new JobScheduler(_services, _jobStorage, _clock);
      }
   }

   [Fact]
   public async Task PerformNow_SuccessfulJob()
   {
      // Arrange
      var job1 = new TestJob.Parameters {
         Delay = TimeSpan.Zero
      };

      // Act
      await _scheduler.PerformNowAsync<TestJob, TestJob.Parameters>(job1, CancellationToken);

      // Assert
      job1.Executed.Should().BeTrue();
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task PerformNow_FailedJob()
   {
      // Arrange
      var job1 = new TestJob.Parameters {
         Delay = TimeSpan.Zero,
         ThrowOnExecute = new Exception("Test exception")
      };

      // Act
      var action = () => _scheduler.PerformNowAsync<TestJob, TestJob.Parameters>(job1, CancellationToken);
      await action.Should().ThrowAsync<Exception>();

      // Assert
      job1.Executed.Should().BeFalse();
      job1.Crashed.Should().BeTrue();
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
   }

   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public async Task PerformAsap_StoresJob(bool withOptions)
   {
      // Arrange
      var job1 = new TestJob.Parameters();
      var options = withOptions
         ? new JobScheduleOptions { JobName = "TestJob1", Group = "TestGroup" }
         : null;

      // Act
      if (options is null)
         await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(job1, CancellationToken);
      else
         await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(job1, options, CancellationToken);

      // Assert
      await AssertSingleScheduledJobAsync(
         expectedParameters: job1,
         expectedOptions: options ?? new JobScheduleOptions(),
         expectedPerformAt: _clock.UtcNow,
         excludeJobName: !withOptions);
   }

   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public async Task PerformAt_StoresJob(bool withOptions)
   {
      // Arrange
      var dateTime = _clock.UtcNow.AddSeconds(1);
      var job1 = new TestJob.Parameters();
      var options = withOptions
         ? new JobScheduleOptions { JobName = "TestJob1", Group = "TestGroup" }
         : null;

      // Act
      if (options is null)
         await _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(dateTime, job1, CancellationToken);
      else
         await _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(dateTime, job1, options, CancellationToken);

      // Assert
      await AssertSingleScheduledJobAsync(
         expectedParameters: job1,
         expectedOptions: options ?? new JobScheduleOptions(),
         expectedPerformAt: dateTime,
         excludeJobName: !withOptions);
   }

   [Fact]
   public async Task PerformCron_Once()
   {
      // Arrange
      var cronExpression = CronExpression.Daily;
      var job1 = new TestJob.Parameters();

      // Act
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(cronExpression, job1, false, CancellationToken);

      // Assert
      await AssertScheduledJobsAsync(
         new[] {
            BuildExpectedCron(job1, cronExpression, "cron_TestJob_00***")
         });
   }

   [Fact]
   public async Task PerformCron_Twice_WithSameCronExpression()
   {
      // Arrange
      var cronExpression = CronExpression.Daily;
      var job1 = new TestJob.Parameters();

      // Act
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(cronExpression, job1, false, CancellationToken);
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(cronExpression, job1, false, CancellationToken);

      // Assert
      await AssertScheduledJobsAsync(
         new[] {
            BuildExpectedCron(job1, cronExpression, "cron_TestJob_00***")
         });
   }

   [Fact]
   public async Task PerformCron_Twice_WithDifferentCronExpression()
   {
      // Arrange
      var cronExpression1 = CronExpression.Daily;
      var cronExpression2 = CronExpression.EverySecond;
      var job1 = new TestJob.Parameters();

      // Act
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(cronExpression1, job1, false, CancellationToken);
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(cronExpression2, job1, false, CancellationToken);

      // Assert
      await AssertScheduledJobsAsync(
         new[] {
            BuildExpectedCron(job1, cronExpression1, "cron_TestJob_00***"),
            BuildExpectedCron(job1, cronExpression2, "cron_TestJob_******")
         });
   }

   private JobStoreItem BuildExpectedCron(TestJob.Parameters parameters, CronExpression cron, string jobName) =>
      new() {
         JobType = typeof(TestJob),
         Parameters = parameters,
         Options = new JobScheduleOptions { JobName = jobName },
         PerformAt = cron.GetNextOccurrence(_clock.UtcNow)!.Value,
         CronExpression = cron
      };

   private Task AssertSingleScheduledJobAsync(
      object expectedParameters,
      JobScheduleOptions expectedOptions,
      DateTime expectedPerformAt,
      bool excludeJobName)
   {
      var expected = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = expectedParameters,
         Options = expectedOptions,
         PerformAt = expectedPerformAt,
         CronExpression = null
      };

      return AssertScheduledJobsAsync(new[] { expected }, excludeJobName);
   }

   private async Task AssertScheduledJobsAsync(JobStoreItem[] expected, bool excludeJobName = false)
   {
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should()
         .BeEquivalentTo(
            expected,
            config =>
            {
               EquivalencyOptions<JobStoreItem> withoutId = config.Excluding(x => x.JobId);
               return excludeJobName ? withoutId.Excluding(x => x.Options.JobName) : withoutId;
            });
   }
}
