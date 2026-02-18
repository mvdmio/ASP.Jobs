using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using NSubstitute;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
///    Tests that verify parameter modifications in job lifecycle methods are persisted correctly
///    when using PostgreSQL storage.
/// </summary>
public sealed class PostgresParameterModificationTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly TestClock _clock;
   private readonly ServiceProvider _services;
   private readonly PostgresJobStorage _storage;
   private readonly PostgresJobInstanceRepository _jobInstanceRepository;
   private readonly JobScheduler _scheduler;
   
   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public PostgresParameterModificationTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _clock = new TestClock();
      
      var configuration = new PostgresJobStorageConfiguration {
         InstanceId = "test-instance",
         ApplicationName = "test-application",
         DatabaseConnectionString = fixture.ConnectionString
      };

      _jobInstanceRepository = new PostgresJobInstanceRepository(fixture.DatabaseConnectionFactory, Options.Create(configuration), _clock);
      _storage = new PostgresJobStorage(fixture.DatabaseConnectionFactory, Options.Create(configuration), Substitute.For<ILogger<PostgresJobStorage>>(), _clock);
      
      var services = new ServiceCollection();
      services.RegisterJob<ParameterModifyingJob>();
      services.RegisterJob<InternalPropertyModifyingJob>();
      services.RegisterJob<PrivateSetterModifyingJob>();
      services.RegisterJob<FieldModifyingJob>();
      _services = services.BuildServiceProvider();
      
      _scheduler = new JobScheduler(_services, _storage, _clock);
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
   
   [Fact]
   public async Task PerformAsap_ParameterModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
   {
      // Arrange
      var parameters = new ParameterModifyingJob.Parameters {
         OriginalValue = "original",
         ModifiedInOnJobScheduled = null,
         ValueSeenInExecute = null
      };
      
      // Act - Schedule the job
      await _scheduler.PerformAsapAsync<ParameterModifyingJob, ParameterModifyingJob.Parameters>(parameters, CancellationToken);
      
      // Retrieve the job from storage (simulating what JobRunnerService does)
      var storedJob = await _storage.WaitForNextJobAsync(CancellationToken);
      
      // Assert - The stored parameters should contain the modification made in OnJobScheduledAsync
      storedJob.Should().NotBeNull();
      var storedParameters = storedJob.Parameters as ParameterModifyingJob.Parameters;
      storedParameters.Should().NotBeNull();
      storedParameters.OriginalValue.Should().Be("original");
      storedParameters.ModifiedInOnJobScheduled.Should().Be("modified_during_scheduling");
   }
   
   [Fact]
   public async Task PerformAt_ParameterModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
   {
      // Arrange
      var parameters = new ParameterModifyingJob.Parameters {
         OriginalValue = "original",
         ModifiedInOnJobScheduled = null,
         ValueSeenInExecute = null
      };
      
      // Act - Schedule the job for immediate execution
      await _scheduler.PerformAtAsync<ParameterModifyingJob, ParameterModifyingJob.Parameters>(
         _clock.UtcNow, 
         parameters, 
         CancellationToken);
      
      // Retrieve the job from storage (simulating what JobRunnerService does)
      var storedJob = await _storage.WaitForNextJobAsync(CancellationToken);
      
      // Assert - The stored parameters should contain the modification made in OnJobScheduledAsync
      storedJob.Should().NotBeNull();
      var storedParameters = storedJob.Parameters as ParameterModifyingJob.Parameters;
      storedParameters.Should().NotBeNull();
      storedParameters.OriginalValue.Should().Be("original");
      storedParameters.ModifiedInOnJobScheduled.Should().Be("modified_during_scheduling");
   }
   
   [Fact]
   public async Task PerformCron_ParameterModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
   {
      // Arrange
      var parameters = new ParameterModifyingJob.Parameters {
         OriginalValue = "original",
         ModifiedInOnJobScheduled = null,
         ValueSeenInExecute = null
      };
      
      // Act - Schedule the job with a CRON expression (run immediately for testing)
      await _scheduler.PerformCronAsync<ParameterModifyingJob, ParameterModifyingJob.Parameters>(
         "* * * * *", // Every minute
         parameters,
         runImmediately: true,
         CancellationToken);
      
      // Retrieve the job from storage (simulating what JobRunnerService does)
      var storedJob = await _storage.WaitForNextJobAsync(CancellationToken);
      
      // Assert - The stored parameters should contain the modification made in OnJobScheduledAsync
      storedJob.Should().NotBeNull();
      var storedParameters = storedJob.Parameters as ParameterModifyingJob.Parameters;
      storedParameters.Should().NotBeNull();
      storedParameters.OriginalValue.Should().Be("original");
      storedParameters.ModifiedInOnJobScheduled.Should().Be("modified_during_scheduling");
   }
   
    [Fact]
    public async Task PerformAsap_InternalPropertyModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
    {
       // Arrange
       var parameters = new InternalPropertyModifyingJob.Parameters {
          PublicValue = "public_original"
       };
       
       // Act - Schedule the job
       await _scheduler.PerformAsapAsync<InternalPropertyModifyingJob, InternalPropertyModifyingJob.Parameters>(parameters, CancellationToken);
       
       // Retrieve the job from storage (simulating what JobRunnerService does)
       var storedJob = await _storage.WaitForNextJobAsync(CancellationToken);
       
       // Assert - The stored parameters should contain the modification made to the internal property in OnJobScheduledAsync
       storedJob.Should().NotBeNull();
       var storedParameters = storedJob.Parameters as InternalPropertyModifyingJob.Parameters;
       storedParameters.Should().NotBeNull();
       storedParameters.PublicValue.Should().Be("public_original");
       storedParameters.InternalModifiedValue.Should().Be("modified_during_scheduling");
    }
    
    [Fact]
    public async Task PerformAsap_PrivateSetterPropertyModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
    {
       // Arrange
       var parameters = new PrivateSetterModifyingJob.Parameters("public_original");
       
       // Act - Schedule the job
       await _scheduler.PerformAsapAsync<PrivateSetterModifyingJob, PrivateSetterModifyingJob.Parameters>(parameters, CancellationToken);
       
       // Retrieve the job from storage (simulating what JobRunnerService does)
       var storedJob = await _storage.WaitForNextJobAsync(CancellationToken);
       
       // Assert - The stored parameters should contain the modification made to the property with private setter in OnJobScheduledAsync
       storedJob.Should().NotBeNull();
       var storedParameters = storedJob.Parameters as PrivateSetterModifyingJob.Parameters;
       storedParameters.Should().NotBeNull();
       storedParameters.PublicValue.Should().Be("public_original");
       storedParameters.ModifiedValue.Should().Be("modified_during_scheduling");
    }
    
    [Fact]
    public async Task PerformAsap_FieldModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
    {
       // Arrange
       var parameters = new FieldModifyingJob.Parameters {
          PublicValue = "public_original"
       };
       
       // Act - Schedule the job
       await _scheduler.PerformAsapAsync<FieldModifyingJob, FieldModifyingJob.Parameters>(parameters, CancellationToken);
       
       // Retrieve the job from storage (simulating what JobRunnerService does)
       var storedJob = await _storage.WaitForNextJobAsync(CancellationToken);
       
       // Assert - The stored parameters should contain the modification made to the field in OnJobScheduledAsync
       storedJob.Should().NotBeNull();
       var storedParameters = storedJob.Parameters as FieldModifyingJob.Parameters;
       storedParameters.Should().NotBeNull();
       storedParameters.PublicValue.Should().Be("public_original");
       storedParameters._modifiedField.Should().Be("modified_during_scheduling");
    }
}

/// <summary>
///    A test job that modifies its parameters during OnJobScheduledAsync.
///    Used to verify that parameter modifications are persisted correctly with PostgreSQL storage.
/// </summary>
public class ParameterModifyingJob : Job<ParameterModifyingJob.Parameters>
{
   public override Task OnJobScheduledAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      // Modify the parameters during scheduling
      parameters.ModifiedInOnJobScheduled = "modified_during_scheduling";
      return Task.CompletedTask;
   }

   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      // Record what value we see during execution
      parameters.ValueSeenInExecute = parameters.ModifiedInOnJobScheduled;
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public string? OriginalValue { get; set; }
      public string? ModifiedInOnJobScheduled { get; set; }
      public string? ValueSeenInExecute { get; set; }
   }
}

/// <summary>
///    A test job that modifies an internal property during OnJobScheduledAsync.
///    Used to verify that internal property modifications are persisted correctly with PostgreSQL storage.
/// </summary>
public class InternalPropertyModifyingJob : Job<InternalPropertyModifyingJob.Parameters>
{
   public override Task OnJobScheduledAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      // Modify the internal property during scheduling
      parameters.InternalModifiedValue = "modified_during_scheduling";
      return Task.CompletedTask;
   }

   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public string? PublicValue { get; set; }
      internal string? InternalModifiedValue { get; set; }
   }
}

/// <summary>
///    A test job that modifies a property with a private setter during OnJobScheduledAsync.
///    Used to verify that properties with private setters are persisted correctly with PostgreSQL storage.
/// </summary>
public class PrivateSetterModifyingJob : Job<PrivateSetterModifyingJob.Parameters>
{
   public override Task OnJobScheduledAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      // Modify the property with private setter during scheduling
      parameters.SetModifiedValue("modified_during_scheduling");
      return Task.CompletedTask;
   }

   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public Parameters() { }
      
      public Parameters(string publicValue)
      {
         PublicValue = publicValue;
      }
      
      public string? PublicValue { get; set; }
      public string? ModifiedValue { get; private set; }
      
      public void SetModifiedValue(string value) => ModifiedValue = value;
   }
}

/// <summary>
///    A test job that modifies a field during OnJobScheduledAsync.
///    Used to verify that fields are persisted correctly with PostgreSQL storage.
/// </summary>
public class FieldModifyingJob : Job<FieldModifyingJob.Parameters>
{
   public override Task OnJobScheduledAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      // Modify the field during scheduling
      parameters._modifiedField = "modified_during_scheduling";
      return Task.CompletedTask;
   }

   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public string? PublicValue { get; set; }
      
      // Public field to test field serialization
      public string? _modifiedField;
   }
}
