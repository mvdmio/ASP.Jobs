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
      jobs.Should().BeEquivalentTo([jobStoreItem]);
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
      jobs.Should().BeEquivalentTo([jobStoreItem]);
   } 

   private IList<JobStoreItem> GetJobsFromDatabase()
   {
      return _fixture.DatabaseConnection.Dapper.Query<JobData>("SELECT * FROM mvdmio.jobs").Select(x => x.ToJobStoreItem()).ToList();
   }
}