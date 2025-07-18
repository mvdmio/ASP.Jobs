﻿using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

public sealed class PostgresJobInstanceRepositoryTests : IAsyncLifetime
{
   private readonly DatabaseConnection _db;
   private readonly TestClock _clock;
   private readonly PostgresJobStorageConfiguration _configuration;
   
   private readonly PostgresJobInstanceRepository _repository;
   
   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;
   
   public PostgresJobInstanceRepositoryTests(PostgresFixture fixture)
   {
      _db = fixture.DatabaseConnection;
      _clock = new TestClock();
      _configuration = new PostgresJobStorageConfiguration {
         InstanceId = "test-instance",
         ApplicationName = "test-application",
         DatabaseConnection = _db
      }; 
      
      _repository = new PostgresJobInstanceRepository(_configuration, _clock);
   }
   
   public async ValueTask InitializeAsync()
   {
      await _db.BeginTransactionAsync();
   }

   public async ValueTask DisposeAsync()
   {
      await _db.RollbackTransactionAsync();
   }
   
   [Fact]
   public async Task RegisterInstance_ShouldInsertNewInstance_WhenInstanceIsNotRegisteredYet()
   {
      // Arrange
      
      // Act
      await _repository.RegisterInstance(CancellationToken);
      
      // Assert
      var instances = (await _repository.GetInstances()).ToList();
      instances.Should().HaveCount(1);
      instances[0].InstanceId.Should().Be("test-instance");
      instances[0].LastSeenAt.Should().Be(_clock.UtcNow);
   }
   
   [Fact]
   public async Task RegisterInstance_ShouldUpdateExistingInstance_WhenInstanceIsAlreadyRegistered()
   {
      // Arrange
      await _repository.RegisterInstance(CancellationToken);
      
      // Act
      _clock.UtcNow = DateTime.UtcNow;
      await _repository.RegisterInstance(CancellationToken);
      
      // Assert
      var instances = (await _repository.GetInstances()).ToList();
      instances.Should().HaveCount(1);
      instances[0].InstanceId.Should().Be("test-instance");
      instances[0].LastSeenAt.Should().Be(_clock.UtcNow);
   }
   
   [Fact]
   public async Task UnregisterInstance_ShouldRemoveInstance_WhenInstanceIsRegistered()
   {
      // Arrange
      await _repository.RegisterInstance(CancellationToken);
      
      // Act
      await _repository.UnregisterInstance(CancellationToken);
      
      // Assert
      var instances = (await _repository.GetInstances()).ToList();
      instances.Should().BeEmpty();
   }
   
   [Fact]
   public async Task UnregisterInstance_ShouldNotThrow_WhenInstanceIsNotRegistered()
   {
      // Arrange
      
      // Act
      await _repository.UnregisterInstance(CancellationToken);
      
      // Assert
      var instances = (await _repository.GetInstances()).ToList();
      instances.Should().BeEmpty();
   }
   
   [Fact]
   public async Task UpdateLastSeenAt_ShouldUpdateLastSeenAt_WhenInstanceIsRegistered()
   {
      // Arrange
      await _repository.RegisterInstance(CancellationToken);
      
      // Act
      _clock.UtcNow = DateTime.UtcNow.AddMinutes(5);
      await _repository.UpdateLastSeenAt(CancellationToken);
      
      // Assert
      var instances = (await _repository.GetInstances()).ToList();
      instances.Should().HaveCount(1);
      instances[0].InstanceId.Should().Be("test-instance");
      instances[0].LastSeenAt.Should().Be(_clock.UtcNow);
   }
   
   [Fact]
   public async Task UpdateLastSeenAt_ShouldRegisterInstance_WhenInstanceIsNotRegisteredYet()
   {
      // Arrange
      
      // Act
      await _repository.UpdateLastSeenAt(CancellationToken);
      
      // Assert
      var instances = (await _repository.GetInstances()).ToList();
      instances.Should().HaveCount(1);
      instances[0].InstanceId.Should().Be("test-instance");
      instances[0].LastSeenAt.Should().Be(_clock.UtcNow);
   }
   
   [Fact]
   public async Task CleanupOldInstances_ShouldRemoveExpiredInstances_WhenThereAreExpiredInstances()
   {
      // Arrange
      _configuration.InstanceId = "test-instance-1";
      _clock.UtcNow = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10));
      await _repository.RegisterInstance(CancellationToken);

      _configuration.InstanceId = "test-instance-2";
      _clock.UtcNow = DateTime.UtcNow;
      await _repository.RegisterInstance(CancellationToken);
      
      // Act
      await _repository.CleanupOldInstances(CancellationToken);
      
      // Assert
      var instances = (await _repository.GetInstances()).ToList();
      instances.Should().HaveCount(1);
      instances[0].InstanceId.Should().Be("test-instance-2");
      instances[0].LastSeenAt.Should().Be(_clock.UtcNow);
   }
   
   [Fact]
   public async Task CleanupOldInstances_ShouldNotRemoveInstances_WhenThereAreNoExpiredInstances()
   {
      // Arrange
      _configuration.InstanceId = "test-instance-1";
      _clock.UtcNow = DateTime.UtcNow;
      await _repository.RegisterInstance(CancellationToken);
      
      // Act
      await _repository.CleanupOldInstances(CancellationToken);
      
      // Assert
      var instances = (await _repository.GetInstances()).ToList();
      instances.Should().HaveCount(1);
      instances[0].InstanceId.Should().Be("test-instance-1");
      instances[0].LastSeenAt.Should().Be(_clock.UtcNow);
   }
   
   [Fact]
   public async Task ReleaseStartedJobs_ShouldSetStartedAtToNull_WhenInstanceHasStartedJobs()
   {
      // Arrange
      await _repository.RegisterInstance(CancellationToken);
      await InsertStartedJob(_clock.UtcNow, _configuration.InstanceId);
      
      // Act
      await _repository.ReleaseStartedJobs(CancellationToken);
      
      // Assert
      var jobs = await GetJobsFromDatabase();
      jobs.Should().HaveCount(1);
      jobs[0].StartedAt.Should().BeNull();
      jobs[0].StartedBy.Should().BeNull();
   }
   
   [Fact]
   public async Task ReleaseStartedJobs_ShouldNotReleaseJobsFromOtherInstances()
   {
      // Arrange
      await _repository.RegisterInstance(CancellationToken);
      
      // Simulate a job started by this instance
      await InsertStartedJob(_clock.UtcNow, _configuration.InstanceId);
      await InsertStartedJob(_clock.UtcNow, _configuration.InstanceId);
      
      // Act
      _configuration.InstanceId = "other-instance"; 
      await _repository.ReleaseStartedJobs(CancellationToken);
      
      // Assert
      var jobs = await GetJobsFromDatabase();
      jobs.Should().HaveCount(2);
      jobs[0].StartedAt.Should().Be(_clock.UtcNow);
      jobs[0].StartedBy.Should().Be("test-instance");
      jobs[1].StartedAt.Should().Be(_clock.UtcNow);
      jobs[1].StartedBy.Should().Be("test-instance");
   }

   private async Task<List<JobData>> GetJobsFromDatabase()
   {
      return (await _db.Dapper.QueryAsync<JobData>("SELECT * FROM mvdmio.jobs;")).ToList();
   }

   private async Task InsertStartedJob(DateTime? startedAt, string? startedBy)
   {
      await _db.Dapper.ExecuteAsync(
         """
         INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, started_at, started_by)
         VALUES (:id, :job_type, :parameters_json, :parameters_type, :cron_expression, :application_name, :job_name, :job_group, :perform_at, :started_at, :started_by)
         """,
         new Dictionary<string, object?> {
            { "id", Guid.NewGuid() },
            { "job_type", typeof(TestJob).AssemblyQualifiedName },
            { "parameters_json", new TypedQueryParameter("{}", NpgsqlDbType.Jsonb) },
            { "parameters_type", typeof(TestJob.Parameters).AssemblyQualifiedName },
            { "cron_expression", null },
            { "application_name", _configuration.ApplicationName },
            { "job_name", "TestJobName" },
            { "job_group", "TestJobGroup" },
            { "perform_at", _clock.UtcNow },
            { "started_at", startedAt },
            { "started_by", startedBy }
         }
      );
   }
}