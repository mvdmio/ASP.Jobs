using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Integration.Jobs;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
///    Tests that verify parameter modifications in job lifecycle methods are persisted correctly
///    when using PostgreSQL storage.
/// </summary>
public sealed class PostgresParameterModificationTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly PostgresStorageHarness _harness;
   private readonly ServiceProvider _services;
   private readonly JobScheduler _scheduler;

   private TestClock Clock => _harness.Clock;
   private PostgresJobStorage Storage => _harness.Storage;

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public PostgresParameterModificationTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _harness = new PostgresStorageHarness(fixture);

      var services = new ServiceCollection();
      services.RegisterJob<ParameterModifyingJob>();
      services.RegisterJob<InternalPropertyModifyingJob>();
      services.RegisterJob<PrivateSetterModifyingJob>();
      services.RegisterJob<FieldModifyingJob>();
      _services = services.BuildServiceProvider();

      _scheduler = new JobScheduler(_services, Storage, Clock);
   }

   public async ValueTask InitializeAsync()
   {
      await _fixture.ResetAsync();
      await _harness.Storage.InitializeAsync(CancellationToken);
      await _harness.InstanceRepository.RegisterInstance(CancellationToken);
   }

   public ValueTask DisposeAsync() => ValueTask.CompletedTask;

   [Fact]
   public async Task PerformAsap_ParameterModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
   {
      // Arrange
      var parameters = new ParameterModifyingJob.Parameters { OriginalValue = "original" };

      // Act
      await _scheduler.PerformAsapAsync<ParameterModifyingJob, ParameterModifyingJob.Parameters>(parameters, CancellationToken);

      // Assert
      var stored = await LoadStoredParametersAsync<ParameterModifyingJob.Parameters>();
      stored.OriginalValue.Should().Be("original");
      stored.ModifiedInOnJobScheduled.Should().Be("modified_during_scheduling");
   }

   [Fact]
   public async Task PerformAt_ParameterModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
   {
      // Arrange
      var parameters = new ParameterModifyingJob.Parameters { OriginalValue = "original" };

      // Act
      await _scheduler.PerformAtAsync<ParameterModifyingJob, ParameterModifyingJob.Parameters>(Clock.UtcNow, parameters, CancellationToken);

      // Assert
      var stored = await LoadStoredParametersAsync<ParameterModifyingJob.Parameters>();
      stored.OriginalValue.Should().Be("original");
      stored.ModifiedInOnJobScheduled.Should().Be("modified_during_scheduling");
   }

   [Fact]
   public async Task PerformCron_ParameterModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
   {
      // Arrange
      var parameters = new ParameterModifyingJob.Parameters { OriginalValue = "original" };

      // Act
      await _scheduler.PerformCronAsync<ParameterModifyingJob, ParameterModifyingJob.Parameters>(
         "* * * * *",
         parameters,
         runImmediately: true,
         CancellationToken);

      // Assert
      var stored = await LoadStoredParametersAsync<ParameterModifyingJob.Parameters>();
      stored.OriginalValue.Should().Be("original");
      stored.ModifiedInOnJobScheduled.Should().Be("modified_during_scheduling");
   }

   [Fact]
   public async Task PerformAsap_InternalPropertyModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
   {
      // Arrange
      var parameters = new InternalPropertyModifyingJob.Parameters { PublicValue = "public_original" };

      // Act
      await _scheduler.PerformAsapAsync<InternalPropertyModifyingJob, InternalPropertyModifyingJob.Parameters>(parameters, CancellationToken);

      // Assert
      var stored = await LoadStoredParametersAsync<InternalPropertyModifyingJob.Parameters>();
      stored.PublicValue.Should().Be("public_original");
      stored.InternalModifiedValue.Should().Be("modified_during_scheduling");
   }

   [Fact]
   public async Task PerformAsap_PrivateSetterPropertyModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
   {
      // Arrange
      var parameters = new PrivateSetterModifyingJob.Parameters("public_original");

      // Act
      await _scheduler.PerformAsapAsync<PrivateSetterModifyingJob, PrivateSetterModifyingJob.Parameters>(parameters, CancellationToken);

      // Assert
      var stored = await LoadStoredParametersAsync<PrivateSetterModifyingJob.Parameters>();
      stored.PublicValue.Should().Be("public_original");
      stored.ModifiedValue.Should().Be("modified_during_scheduling");
   }

   [Fact]
   public async Task PerformAsap_FieldModificationsInOnJobScheduled_ShouldBeVisibleInExecute()
   {
      // Arrange
      var parameters = new FieldModifyingJob.Parameters { PublicValue = "public_original" };

      // Act
      await _scheduler.PerformAsapAsync<FieldModifyingJob, FieldModifyingJob.Parameters>(parameters, CancellationToken);

      // Assert
      var stored = await LoadStoredParametersAsync<FieldModifyingJob.Parameters>();
      stored.PublicValue.Should().Be("public_original");
      stored._modifiedField.Should().Be("modified_during_scheduling");
   }

   private async Task<T> LoadStoredParametersAsync<T>() where T : class
   {
      var storedJob = await Storage.WaitForNextJobAsync(CancellationToken);
      storedJob.Should().NotBeNull();

      var stored = storedJob!.Parameters as T;
      stored.Should().NotBeNull();
      return stored!;
   }
}
