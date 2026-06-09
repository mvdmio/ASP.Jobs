using AwesomeAssertions;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
/// Locks in the fail-fast Initialization Guard contract (ADR 0001): accessing the public storage surface before
/// Initialization throws <see cref="JobStorageNotInitializedException"/>; after Initialization the same operation succeeds.
/// The guard flag is per-process, so a fresh storage instance is "not initialized" even though the shared fixture
/// database is already migrated.
/// </summary>
public sealed class InitializationGuardTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly PostgresStorageHarness _harness;

   public InitializationGuardTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _harness = new PostgresStorageHarness(fixture);
   }

   // Note: intentionally does NOT initialize the storage instance - that is what the guard test exercises.
   public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

   public ValueTask DisposeAsync() => ValueTask.CompletedTask;

   [Fact]
   public async Task ScheduleJob_ThrowsBeforeInitialization_AndSucceedsAfter()
   {
      // Arrange
      var job = JobStoreItemFactory.MakeTestJob(performAt: _harness.Clock.UtcNow);

      // Act + Assert - before Initialization, scheduling fails fast with the actionable exception.
      var beforeInit = async () => await _harness.Storage.ScheduleJobAsync(job, TestContext.Current.CancellationToken);
      await beforeInit.Should().ThrowAsync<JobStorageNotInitializedException>();

      // Act - initialize, then the same operation must succeed.
      await _harness.Storage.InitializeAsync(TestContext.Current.CancellationToken);

      // Assert
      var afterInit = async () => await _harness.Storage.ScheduleJobAsync(job, TestContext.Current.CancellationToken);
      await afterInit.Should().NotThrowAsync();
   }
}
