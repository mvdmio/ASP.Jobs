using System.Globalization;
using AwesomeAssertions;
using AwesomeAssertions.Equivalency;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Integration.Jobs;
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

      var jobTestServices = new JobTestServices();
      jobTestServices.Services.RegisterJob<TestJob_Underscored>();
      _services = jobTestServices.Services.BuildServiceProvider();
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
         await _harness.Storage.InitializeAsync(CancellationToken);
         await _harness.InstanceRepository.RegisterInstance(CancellationToken);
      }

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;

      [Fact]
      public async Task PerformCron_LeavesTheCronScheduleOfAnotherApplicationAlone()
      {
         // Arrange - two applications share the database; the other one schedules TestJob on Daily.
         var otherHarness = new PostgresStorageHarness(_fixture, "other-instance", "other-application");
         await otherHarness.Storage.InitializeAsync(CancellationToken);
         var otherScheduler = new JobScheduler(_services, otherHarness.Storage, _clock);
         await otherScheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.Daily, new TestJob.Parameters(), false, CancellationToken);

         // Act
         await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.Daily, new TestJob.Parameters(), false, CancellationToken);
         await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.EverySecond, new TestJob.Parameters(), false, CancellationToken);

         // Assert
         (await GetJobNamesByApplicationAsync()).Should().Equal(
            "other-application/cron_TestJob_00***",
            "test-application/cron_TestJob_******"
         );
      }

      [Fact]
      public async Task PerformCron_ReplacesACronRowStoredWithAnotherAssemblyVersion()
      {
         // Arrange - a waiting CRON row of TestJob, as an older build of the assembly stored it. The scheduler cannot
         // make such a row, because its job_type carries the assembly version that is running now.
         const string olderBuildJobType = "mvdmio.ASP.Jobs.Tests.Unit.Utils.TestJob, mvdmio.ASP.Jobs.Tests.Unit, Version=0.0.1.0, Culture=neutral, PublicKeyToken=null";
         await RawJobRows.InsertAsync(
            _fixture.DatabaseConnection,
            _harness.Configuration.ApplicationName,
            "cron_TestJob_*****",
            _clock.UtcNow,
            ct: CancellationToken,
            jobType: olderBuildJobType,
            cronExpression: "* * * * *"
         );

         // Act
         await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.Daily, new TestJob.Parameters(), false, CancellationToken);

         // Assert
         (await GetJobNamesByApplicationAsync()).Should().Equal("test-application/cron_TestJob_00***");
      }

      private async Task<string[]> GetJobNamesByApplicationAsync()
      {
         var names = await _fixture.DatabaseConnection.Dapper.QueryAsync<string>(
            "SELECT application_name || '/' || job_name FROM mvdmio.jobs ORDER BY application_name, job_name",
            ct: CancellationToken
         );

         return names.ToArray();
      }

      [Fact]
      public async Task PerformAsap_Batch_RaisesExactlyOneJobsUpdatedNotification()
      {
         // Arrange
         var parameters = DistinctParameters(3);
         await using var listener = await JobsUpdatedListener.StartAsync(_fixture.ConnectionString, CancellationToken);

         // Act
         await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

         // Assert
         var notifications = await listener.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), CancellationToken);
         notifications.Should().Be(1);
      }

      [Fact]
      public async Task PerformAsap_BatchWithOptions_RaisesExactlyOneJobsUpdatedNotification()
      {
         // Arrange
         var jobs = new[] { NamedJob("a", 1, "g"), NamedJob("b", 2, "g"), NamedJob("c", 3, "g") };
         await using var listener = await JobsUpdatedListener.StartAsync(_fixture.ConnectionString, CancellationToken);

         // Act
         await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(jobs, CancellationToken);

         // Assert
         var notifications = await listener.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), CancellationToken);
         notifications.Should().Be(1);
      }
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
   public async Task PerformAsap_Batch_StoresEveryJobWithTheAmbientCultureCapturedBeforeTheCall()
   {
      // Arrange
      var parameters = DistinctParameters(3);

      // Act
      using (new ThreadCultureScope("nl-NL", "fr-FR"))
      {
         await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);
      }

      // Assert
      await AssertBatchScheduledAsync(parameters, _clock.UtcNow, "nl-NL", "fr-FR");
   }

   [Fact]
   public async Task PerformAsap_BatchWithCulture_StoresEveryJobWithThatCulture()
   {
      // Arrange
      var parameters = DistinctParameters(3);

      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, new CultureInfo("de-DE"), CancellationToken);

      // Assert
      await AssertBatchScheduledAsync(parameters, _clock.UtcNow, "de-DE", "de-DE");
   }

   [Fact]
   public async Task PerformAt_Batch_StoresEveryJobWithTheAmbientCultureCapturedBeforeTheCall()
   {
      // Arrange
      var performAt = _clock.UtcNow.AddMinutes(5);
      var parameters = DistinctParameters(3);

      // Act
      using (new ThreadCultureScope("nl-NL", "fr-FR"))
      {
         await _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(performAt, parameters, CancellationToken);
      }

      // Assert
      await AssertBatchScheduledAsync(parameters, performAt, "nl-NL", "fr-FR");
   }

   [Fact]
   public async Task PerformAt_BatchWithCulture_StoresEveryJobWithThatCulture()
   {
      // Arrange
      var performAt = _clock.UtcNow.AddMinutes(5);
      var parameters = DistinctParameters(3);

      // Act
      await _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(performAt, parameters, new CultureInfo("de-DE"), CancellationToken);

      // Assert
      await AssertBatchScheduledAsync(parameters, performAt, "de-DE", "de-DE");
   }

   [Fact]
   public async Task PerformAsap_Batch_StoresNoJob_WhenOnJobScheduledAsyncThrowsForOneJob()
   {
      // Arrange
      var parameters = new[] {
         new TestJob.Parameters(),
         new TestJob.Parameters { ThrowInOnJobScheduledAsync = new InvalidOperationException("Hook veto") },
         new TestJob.Parameters()
      };

      // Act
      var action = () => _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Assert
      await action.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("Hook veto");
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task PerformAt_Batch_StoresNoJob_WhenOnJobScheduledAsyncThrowsForOneJob()
   {
      // Arrange
      var parameters = new[] {
         new TestJob.Parameters(),
         new TestJob.Parameters { ThrowInOnJobScheduledAsync = new InvalidOperationException("Hook veto") }
      };

      // Act
      var action = () => _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(_clock.UtcNow.AddMinutes(5), parameters, CancellationToken);

      // Assert
      await action.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("Hook veto");
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task PerformAsap_Batch_StoresNoJob_WhenCancelledFromTheFirstJobsOnJobScheduledAsync()
   {
      // Arrange
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      var parameters = new[] {
         new TestJob.Parameters { OnJobScheduledCallback = () => cts.Cancel() },
         new TestJob.Parameters()
      };

      // Act
      var action = () => _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, cts.Token);

      // Assert
      await action.Should().ThrowAsync<OperationCanceledException>();
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task PerformAsap_EmptyBatch_StoresNothing()
   {
      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(Array.Empty<TestJob.Parameters>(), CancellationToken);

      // Assert
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task PerformAsap_BatchWithNullItem_ThrowsAndStoresNothing()
   {
      // Arrange
      var hookRan = false;
      var parameters = new[] { new TestJob.Parameters { OnJobScheduledCallback = () => hookRan = true }, null! };

      // Act
      var action = () => _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Assert
      await action.Should().ThrowExactlyAsync<ArgumentNullException>().WithParameterName("parameters");
      hookRan.Should().BeFalse();
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task PerformAsap_BatchWithOptions_StoresEveryJobWithItsOwnNameAndGroup()
   {
      // Arrange
      var jobs = new[] { NamedJob("a", 1, "g"), NamedJob("b", 2, "g"), NamedJob("c", 3, "g") };

      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(jobs, CancellationToken);

      // Assert
      (await GetStoredJobsAsync()).Should().BeEquivalentTo(
         [
            new StoredJob("a", "g", TimeSpan.FromSeconds(1), _clock.UtcNow),
            new StoredJob("b", "g", TimeSpan.FromSeconds(2), _clock.UtcNow),
            new StoredJob("c", "g", TimeSpan.FromSeconds(3), _clock.UtcNow)
         ]
      );
   }

   [Fact]
   public async Task PerformAsap_BatchWithOptions_ReplacesAWaitingJobWithTheSameName()
   {
      // Arrange
      var (waiting, waitingOptions) = NamedJob("a", 1);
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(waiting, waitingOptions, CancellationToken);

      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>([NamedJob("a", 2)], CancellationToken);

      // Assert
      (await GetStoredJobsAsync()).Should().BeEquivalentTo([new StoredJob("a", null, TimeSpan.FromSeconds(2), _clock.UtcNow)]);
   }

   [Fact]
   public async Task PerformAsap_BatchWithOptions_DoesNotReplaceAJobThatHasStarted()
   {
      // Arrange - the job named "a" has been claimed by a Worker Instance.
      var (started, startedOptions) = NamedJob("a", 1);
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(started, startedOptions, CancellationToken);
      (await _jobStorage.WaitForNextJobAsync(CancellationToken)).Should().NotBeNull();

      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>([NamedJob("a", 2)], CancellationToken);

      // Assert - the new job is stored next to the started one.
      (await GetStoredJobsAsync()).Should().BeEquivalentTo([new StoredJob("a", null, TimeSpan.FromSeconds(2), _clock.UtcNow)]);
      var inProgress = (await _jobStorage.GetInProgressJobsAsync(CancellationToken)).ToArray();
      inProgress.Should().ContainSingle().Which.Parameters.Should().BeOfType<TestJob.Parameters>().Which.Delay.Should().Be(TimeSpan.FromSeconds(1));
   }

   [Fact]
   public async Task PerformAsap_BatchWithOptions_StoresTheLastJobWhenTwoShareAName()
   {
      // Arrange
      var jobs = new[] { NamedJob("a", 1), NamedJob("a", 2) };

      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(jobs, CancellationToken);

      // Assert
      (await GetStoredJobsAsync()).Should().BeEquivalentTo([new StoredJob("a", null, TimeSpan.FromSeconds(2), _clock.UtcNow)]);
   }

   [Fact]
   public async Task PerformAsap_BatchWithOptions_ThrowsAndStoresNothing_WhenAJobHasNullOptions()
   {
      // Arrange
      var hookRan = false;
      var jobs = new List<(TestJob.Parameters Parameters, JobScheduleOptions Options)> {
         (new TestJob.Parameters { OnJobScheduledCallback = () => hookRan = true }, new JobScheduleOptions { JobName = "a" }),
         (new TestJob.Parameters(), null!)
      };

      // Act
      var action = () => _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(jobs, CancellationToken);

      // Assert
      await action.Should().ThrowExactlyAsync<ArgumentNullException>().WithParameterName("jobs");
      hookRan.Should().BeFalse();
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task PerformAsap_BatchWithOptions_ThrowsAndStoresNothing_WhenAJobHasNullParameters()
   {
      // Arrange
      var jobs = new List<(TestJob.Parameters Parameters, JobScheduleOptions Options)> {
         NamedJob("a", 1),
         (null!, new JobScheduleOptions { JobName = "b" })
      };

      // Act
      var action = () => _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(jobs, CancellationToken);

      // Assert
      await action.Should().ThrowExactlyAsync<ArgumentNullException>().WithParameterName("jobs");
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
   }

   [Fact]
   public async Task PerformAt_BatchWithOptions_StoresEveryJobAtThatTime()
   {
      // Arrange
      var performAt = _clock.UtcNow.AddHours(1);
      var jobs = new[] { NamedJob("a", 1), NamedJob("b", 2) };

      // Act
      await _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(performAt, jobs, CancellationToken);

      // Assert
      (await GetStoredJobsAsync()).Should().BeEquivalentTo(
         [
            new StoredJob("a", null, TimeSpan.FromSeconds(1), performAt),
            new StoredJob("b", null, TimeSpan.FromSeconds(2), performAt)
         ]
      );
   }

   [Fact]
   public async Task PerformAsap_BatchWithOptionsAndCulture_StoresEveryJobWithThatCulture()
   {
      // Arrange
      var jobs = new[] { NamedJob("a", 1), NamedJob("b", 2) };

      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(jobs, new CultureInfo("nl-NL"), CancellationToken);

      // Assert
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().HaveCount(2).And.OnlyContain(x => x.CultureName == "nl-NL" && x.UICultureName == "nl-NL");
   }

   [Fact]
   public async Task PerformAt_BatchWithOptionsAndCulture_StoresEveryJobWithThatCultureAtThatTime()
   {
      // Arrange
      var performAt = _clock.UtcNow.AddHours(1);
      var jobs = new[] { NamedJob("a", 1), NamedJob("b", 2) };

      // Act
      await _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(performAt, jobs, new CultureInfo("nl-NL"), CancellationToken);

      // Assert
      var stored = (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).ToArray();
      stored.Should().HaveCount(2).And.OnlyContain(x => x.CultureName == "nl-NL" && x.UICultureName == "nl-NL" && x.PerformAt == performAt);
   }

   [Fact]
   public async Task PerformAsap_BatchWithOptions_StoresEveryJobWithTheAmbientCultureCapturedBeforeTheCall()
   {
      // Arrange
      var jobs = new[] { NamedJob("a", 1), NamedJob("b", 2) };

      // Act
      using (new ThreadCultureScope("nl-NL", "fr-FR"))
      {
         await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(jobs, CancellationToken);
      }

      // Assert
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Should().HaveCount(2).And.OnlyContain(x => x.CultureName == "nl-NL" && x.UICultureName == "fr-FR");
   }

   [Fact]
   public async Task PerformAsap_BatchWithOptions_RunsJobsOfOneGroupOneAtATime()
   {
      // Arrange
      var jobs = new[] { NamedJob("a", 1, "g"), NamedJob("b", 2, "g") };
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(jobs, CancellationToken);

      // Act - the first job is claimed and the second one waits for it.
      var firstClaim = await _jobStorage.WaitForNextJobAsync(CancellationToken);
      using var blockedCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      blockedCts.CancelAfter(TimeSpan.FromMilliseconds(500));
      var blockedClaim = await _jobStorage.WaitForNextJobAsync(blockedCts.Token);

      // Assert
      firstClaim.Should().NotBeNull();
      blockedClaim.Should().BeNull();

      // Act - once the first job is finished, the second one is claimed.
      await _jobStorage.FinalizeJobAsync(firstClaim!, CancellationToken);
      var secondClaim = await _jobStorage.WaitForNextJobAsync(CancellationToken);

      // Assert
      secondClaim.Should().NotBeNull();
      secondClaim!.JobId.Should().NotBe(firstClaim!.JobId);
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
      var job2 = new TestJob.Parameters { Delay = TimeSpan.FromSeconds(7) };

      // Act
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(cronExpression, job1, false, CancellationToken);
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(cronExpression, job2, false, CancellationToken);

      // Assert
      await AssertScheduledJobsAsync(
         new[] {
            BuildExpectedCron(job2, cronExpression, "cron_TestJob_00***")
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
            BuildExpectedCron(job1, cronExpression2, "cron_TestJob_******")
         });
   }

   [Fact]
   public async Task PerformCron_WithRunImmediately_ReplacesTheCronScheduleWithAnotherExpression()
   {
      // Arrange
      var job1 = new TestJob.Parameters();

      // Act
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.Daily, job1, false, CancellationToken);
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.EverySecond, job1, true, CancellationToken);

      // Assert
      var expected = BuildExpectedCron(job1, CronExpression.EverySecond, "cron_TestJob_******");
      await AssertScheduledJobsAsync(
         new[] {
            new JobStoreItem {
               JobType = expected.JobType,
               Parameters = expected.Parameters,
               Options = expected.Options,
               PerformAt = _clock.UtcNow,
               CronExpression = expected.CronExpression
            }
         });
   }

   [Fact]
   public async Task PerformCron_WithCultureOverload_ReplacesTheCronScheduleWithAnotherExpression()
   {
      // Arrange
      var job1 = new TestJob.Parameters();

      // Act
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>("0 0 * * *", job1, new CultureInfo("nl-NL"), false, CancellationToken);
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>("0 9,17 * * *", job1, new CultureInfo("nl-NL"), false, CancellationToken);

      // Assert
      var scheduledJobs = (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).ToArray();
      scheduledJobs.Select(x => x.Options.JobName).Should().Equal("cron_TestJob_09,17***");
   }

   [Fact]
   public async Task PerformCron_LeavesOneOffJobsOfTheSameClassAlone()
   {
      // Arrange
      var asapJob = new TestJob.Parameters { Delay = TimeSpan.FromSeconds(1) };
      var atJob = new TestJob.Parameters { Delay = TimeSpan.FromSeconds(2) };
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(asapJob, CancellationToken);
      await _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(_clock.UtcNow.AddHours(1), atJob, CancellationToken);

      // Act
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.Daily, new TestJob.Parameters(), false, CancellationToken);
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.EverySecond, new TestJob.Parameters(), false, CancellationToken);

      // Assert
      var scheduledJobs = (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).ToArray();
      scheduledJobs.Where(x => x.CronExpression is null).Select(x => x.Parameters).Should().BeEquivalentTo(new object[] { asapJob, atJob });
      scheduledJobs.Where(x => x.CronExpression is not null).Select(x => x.Options.JobName).Should().Equal("cron_TestJob_******");
   }

   [Fact]
   public async Task PerformCron_LeavesACronRunThatAWorkerInstanceHasStartedAlone()
   {
      // Arrange - the Daily occurrence is due now and a Worker Instance has started it.
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.Daily, new TestJob.Parameters(), true, CancellationToken);
      var startedJob = await _jobStorage.WaitForNextJobAsync(CancellationToken);
      startedJob.Should().NotBeNull();

      // Act
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.EverySecond, new TestJob.Parameters(), false, CancellationToken);

      // Assert
      (await _jobStorage.GetInProgressJobsAsync(CancellationToken)).Select(x => x.Options.JobName).Should().Equal("cron_TestJob_00***");
      (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).Select(x => x.Options.JobName).Should().Equal("cron_TestJob_******");
   }

   [Fact]
   public async Task PerformCron_LeavesTheCronScheduleOfAClassWhoseNameStartsWithThisClassNameAlone()
   {
      // Arrange - TestJob_Underscored has the CRON row name cron_TestJob_Underscored_00***.
      await _scheduler.PerformCronAsync<TestJob_Underscored, TestJob.Parameters>(CronExpression.Daily, new TestJob.Parameters(), false, CancellationToken);

      // Act
      await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.EverySecond, new TestJob.Parameters(), false, CancellationToken);

      // Assert
      var scheduledJobs = (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).ToArray();
      scheduledJobs.Select(x => x.Options.JobName).Should().BeEquivalentTo("cron_TestJob_Underscored_00***", "cron_TestJob_******");
   }

   private JobStoreItem BuildExpectedCron(TestJob.Parameters parameters, CronExpression cron, string jobName) =>
      new() {
         JobType = typeof(TestJob),
         Parameters = parameters,
         Options = new JobScheduleOptions { JobName = jobName },
         PerformAt = cron.GetNextOccurrence(_clock.UtcNow)!.Value,
         CronExpression = cron
      };

   // Each item differs by its Delay, so the equivalency check pairs every stored job with its own parameters.
   private static TestJob.Parameters[] DistinctParameters(int count) =>
      Enumerable.Range(1, count).Select(i => new TestJob.Parameters { Delay = TimeSpan.FromSeconds(i) }).ToArray();

   // The Delay tells the jobs apart, like DistinctParameters does.
   private static (TestJob.Parameters Parameters, JobScheduleOptions Options) NamedJob(string name, int delaySeconds, string? group = null) =>
      (new TestJob.Parameters { Delay = TimeSpan.FromSeconds(delaySeconds) }, new JobScheduleOptions { JobName = name, Group = group });

   private async Task<StoredJob[]> GetStoredJobsAsync()
   {
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      return scheduledJobs
         .Select(x => new StoredJob(x.Options.JobName, x.Options.Group, ((TestJob.Parameters)x.Parameters).Delay, x.PerformAt))
         .ToArray();
   }

   private sealed record StoredJob(string Name, string? Group, TimeSpan? Delay, DateTime PerformAt);

   private async Task AssertBatchScheduledAsync(TestJob.Parameters[] parameters, DateTime expectedPerformAt, string expectedCulture, string expectedUICulture)
   {
      var expected = parameters.Select(x => new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = x,
         Options = new JobScheduleOptions(),
         PerformAt = expectedPerformAt,
         CronExpression = null,
         CultureName = expectedCulture,
         UICultureName = expectedUICulture
      });

      var scheduledJobs = (await _jobStorage.GetScheduledJobsAsync(CancellationToken)).ToArray();
      scheduledJobs.Should().BeEquivalentTo(expected, config => config.Excluding(x => x.JobId).Excluding(x => x.Options.JobName));
      scheduledJobs.Select(x => x.Options.JobName).Should().OnlyHaveUniqueItems();
   }

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
               // Culture is auto-captured (ambient for asap/at, invariant for cron) and host-dependent, so it is
               // excluded here like JobId; its round-trip is covered by the dedicated culture tests.
               EquivalencyOptions<JobStoreItem> withoutId = config
                  .Excluding(x => x.JobId)
                  .Excluding(x => x.CultureName)
                  .Excluding(x => x.UICultureName);
               return excludeJobName ? withoutId.Excluding(x => x.Options.JobName) : withoutId;
            });
   }
}
