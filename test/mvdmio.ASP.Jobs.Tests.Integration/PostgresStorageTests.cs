using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration;

public sealed class PostgresStorageTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly TestClock _clock;
   
   private readonly PostgresJobStorage _storage;

   public PostgresStorageTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _clock = new TestClock();
      
      _storage = new PostgresJobStorage(
         new PostgresJobStorageConfiguration {
            DatabaseConnection = _fixture.DatabaseConnection
         },
         _clock
      );
   }
   
   public async ValueTask InitializeAsync()
   {
      await _fixture.DatabaseConnection.BeginTransactionAsync();
   }

   public async ValueTask DisposeAsync()
   {
      await _fixture.DatabaseConnection.RollbackTransactionAsync();
   }
   
   [Fact]
   public async Task ScheduleJob_BasicJob()
   {
      // Arrange
      var jobStoreItem = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = new TestJob.Parameters {
            Delay = TimeSpan.Zero
         },
         Options = new JobScheduleOptions(),
         PerformAt = _clock.UtcNow
      };
      
      // Act
      await _storage.ScheduleJobAsync(jobStoreItem, TestContext.Current.CancellationToken);
      
      // Assert
      var jobs = GetJobsFromDatabase();
      jobs.Count.Should().Be(1);
      jobs.Select(x => x.ToJobStoreItem()).Should().BeEquivalentTo([jobStoreItem]);
   }
   
   [Fact]
   public async Task ScheduleJob_ComplexJob()
   {
      // Arrange
      var jobStoreItem = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = new TestJob.Parameters {
            Delay = TimeSpan.Zero
         },
         Options = new JobScheduleOptions {
            JobName = "ComplexJob",
            Group = "ComplexGroup",
         },
         PerformAt = _clock.UtcNow
      };
      
      // Act
      await _storage.ScheduleJobAsync(jobStoreItem, TestContext.Current.CancellationToken);
      
      // Assert
      var jobs = GetJobsFromDatabase();
      jobs.Count.Should().Be(1);
      jobs.Select(x => x.ToJobStoreItem()).Should().BeEquivalentTo([jobStoreItem]);
   }

   [Fact]
   public async Task ScheduleJob_ShouldUpdateNotStartedJob()
   {
      // Arrange
      var jobStoreItem1 = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = new TestJob.Parameters {
            Delay = TimeSpan.Zero
         },
         Options = new JobScheduleOptions {
            JobName = "ComplexJob"
         },
         PerformAt = _clock.UtcNow.Subtract(TimeSpan.FromDays(1))
      };
      
      var jobStoreItem2 = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = new TestJob.Parameters {
            Delay = TimeSpan.Zero
         },
         Options = new JobScheduleOptions {
            JobName = "ComplexJob"
         },
         PerformAt = _clock.UtcNow
      };
      
      // Act
      await _storage.ScheduleJobAsync(jobStoreItem1, TestContext.Current.CancellationToken);
      await _storage.ScheduleJobAsync(jobStoreItem2, TestContext.Current.CancellationToken);
      
      // Assert
      var jobs = GetJobsFromDatabase();
      jobs.Count.Should().Be(1);
      jobs.Select(x => x.ToJobStoreItem()).Should().BeEquivalentTo([jobStoreItem2]);
   }
   
   [Fact]
   public async Task StartNextJob_ShouldReturnNull_WhenNoJobsAvailable()
   {
      // Act
      var job = await _storage.StartNextJobAsync(TestContext.Current.CancellationToken);
      
      // Assert
      job.Should().BeNull();
   }
   
   [Fact]
   public async Task StartNextJob_ShouldReturnNextJob_WhenAvailable()
   {
      // Arrange
      var job1 = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = new TestJob.Parameters {
            Delay = TimeSpan.Zero
         },
         Options = new JobScheduleOptions {
            JobName = "TestJob"
         },
         PerformAt = _clock.UtcNow
      };
      
      await _storage.ScheduleJobAsync(job1, TestContext.Current.CancellationToken);
      
      // Act
      var startedJob = await _storage.StartNextJobAsync(TestContext.Current.CancellationToken);
      
      // Assert
      startedJob.Should().BeEquivalentTo(job1);
      
      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs[0].StartedAt.Should().BeWithin(TimeSpan.FromSeconds(1)).Before(_clock.UtcNow);
   }
   
   [Fact]
   public async Task StartNextJob_ShouldNotStartSameJobTwice()
   {
      // Arrange
      var job1 = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = new TestJob.Parameters {
            Delay = TimeSpan.Zero
         },
         Options = new JobScheduleOptions {
            JobName = "TestJob"
         },
         PerformAt = _clock.UtcNow
      };
      
      await _storage.ScheduleJobAsync(job1, TestContext.Current.CancellationToken);
      
      // Act
      _ = await _storage.StartNextJobAsync(TestContext.Current.CancellationToken);
      var startedJob2 = await _storage.StartNextJobAsync(TestContext.Current.CancellationToken);
      
      // Assert
      startedJob2.Should().BeNull();
   }

   [Fact]
   public async Task FinalizeJob()
   {
      // Arrange
      var job1 = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = new TestJob.Parameters {
            Delay = TimeSpan.Zero
         },
         Options = new JobScheduleOptions {
            JobName = "TestJob"
         },
         PerformAt = _clock.UtcNow
      };
      
      await _storage.ScheduleJobAsync(job1, TestContext.Current.CancellationToken);
      var startedJob1 = await _storage.StartNextJobAsync(TestContext.Current.CancellationToken);
      
      // Act
      await _storage.FinalizeJobAsync(startedJob1!, TestContext.Current.CancellationToken);
      
      // Assert
      var jobs = GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs[0].CompletedAt.Should().BeWithin(TimeSpan.FromSeconds(1)).Before(_clock.UtcNow);
   }
   
   private IList<JobData> GetJobsFromDatabase()
   {
      return _fixture.DatabaseConnection.Dapper.Query<JobData>("SELECT * FROM mvdmio.jobs").ToList();
   }
}