using AwesomeAssertions;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.Database.PgSQL;
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
      private readonly PostgresJobInstanceRepository _jobInstanceRepository;

      public PostgresJobSchedulerTests(PostgresFixture fixture)
      {
         _fixture = fixture;
         var dbConnectionFactory = new DatabaseConnectionFactory();
         var configuration = new PostgresJobStorageConfiguration {
            InstanceId = "test-instance",
            ApplicationName = "test-application",
            DatabaseConnectionString = fixture.ConnectionString
         };

         _jobInstanceRepository = new PostgresJobInstanceRepository(dbConnectionFactory, Options.Create(configuration), _clock);
         _jobStorage = new PostgresJobStorage(dbConnectionFactory, Options.Create(configuration), _clock); 
         
         _scheduler = new JobScheduler(_services, _jobStorage, _clock);
      }
      
      public async ValueTask InitializeAsync()
      {
         await _fixture.ResetAsync();
         await _jobInstanceRepository.RegisterInstance(CancellationToken);
      }

      public ValueTask DisposeAsync()
      {
         return ValueTask.CompletedTask;
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
      
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should().HaveCount(0);
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
      
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should().HaveCount(0);
   }

   [Fact]
   public async Task PerformAsap_WithoutOptions()
   {
      // Arrange
      var job1 = new TestJob.Parameters();
      
      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(job1, CancellationToken);
      
      // Assert
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should()
         .BeEquivalentTo(
            [
               new JobStoreItem {
                  JobType = typeof(TestJob),
                  Parameters = job1,
                  Options = new JobScheduleOptions(),
                  PerformAt = _clock.UtcNow,
                  CronExpression = null
               }
            ],
            config => config.Excluding(x => x.JobId).Excluding(x => x.Options.JobName)
         );
   }
   
   [Fact]
   public async Task PerformAsap_WithOptions()
   {
      // Arrange
      var job1 = new TestJob.Parameters();
      var options = new JobScheduleOptions {
         JobName = "TestJob1",
         Group = "TestGroup"
      };
      
      // Act
      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(job1, options, CancellationToken);
      
      // Assert
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should()
         .BeEquivalentTo(
            [
               new JobStoreItem {
                  JobType = typeof(TestJob),
                  Parameters = job1,
                  Options = options,
                  PerformAt = _clock.UtcNow,
                  CronExpression = null
               }
            ],
            config => config.Excluding(x => x.JobId)
         );
   }
   
   [Fact]
   public async Task PerformAt_WithoutOptions()
   {
      // Arrange
      var dateTime = _clock.UtcNow.AddSeconds(1);
      var job1 = new TestJob.Parameters();
      
      // Act
      await _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(dateTime, job1, CancellationToken);
      
      // Assert
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should()
         .BeEquivalentTo(
            [
               new JobStoreItem {
                  JobType = typeof(TestJob),
                  Parameters = job1,
                  Options = new JobScheduleOptions(),
                  PerformAt = dateTime,
                  CronExpression = null
               }
            ],
            config => config.Excluding(x => x.JobId).Excluding(x => x.Options.JobName)
         );
   }
   
   [Fact]
   public async Task PerformAt_WithOptions()
   {
      // Arrange
      var dateTime = _clock.UtcNow.AddSeconds(1);
      var job1 = new TestJob.Parameters();
      var options = new JobScheduleOptions {
         JobName = "TestJob1",
         Group = "TestGroup"
      };
      
      // Act
      await _scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(dateTime, job1, options, CancellationToken);
      
      // Assert
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should()
         .BeEquivalentTo(
            [
               new JobStoreItem {
                  JobType = typeof(TestJob),
                  Parameters = job1,
                  Options = options,
                  PerformAt = dateTime,
                  CronExpression = null
               }
            ],
            config => config.Excluding(x => x.JobId)
         );
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
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should()
         .BeEquivalentTo(
            [
               new JobStoreItem {
                  JobType = typeof(TestJob),
                  Parameters = job1,
                  Options = new JobScheduleOptions {
                     JobName = "cron_TestJob_00***"
                  },
                  PerformAt = cronExpression.GetNextOccurrence(_clock.UtcNow)!.Value,
                  CronExpression = cronExpression
               }
            ],
            config => config.Excluding(x => x.JobId)
         );
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
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should()
         .BeEquivalentTo(
            [
               new JobStoreItem {
                  JobType = typeof(TestJob),
                  Parameters = job1,
                  Options = new JobScheduleOptions {
                     JobName = "cron_TestJob_00***"
                  },
                  PerformAt = cronExpression.GetNextOccurrence(_clock.UtcNow)!.Value,
                  CronExpression = cronExpression
               }
            ],
            config => config.Excluding(x => x.JobId)
         );
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
      var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
      scheduledJobs.Should()
         .BeEquivalentTo(
            [
               new JobStoreItem {
                  JobType = typeof(TestJob),
                  Parameters = job1,
                  Options = new JobScheduleOptions {
                     JobName = "cron_TestJob_00***"
                  },
                  PerformAt = cronExpression1.GetNextOccurrence(_clock.UtcNow)!.Value,
                  CronExpression = cronExpression1
               },
               new JobStoreItem {
                  JobType = typeof(TestJob),
                  Parameters = job1,
                  Options = new JobScheduleOptions {
                     JobName = "cron_TestJob_******"
                  },
                  PerformAt = cronExpression2.GetNextOccurrence(_clock.UtcNow)!.Value,
                  CronExpression = cronExpression2
               }
            ],
            config => config.Excluding(x => x.JobId)
         );
   }
}