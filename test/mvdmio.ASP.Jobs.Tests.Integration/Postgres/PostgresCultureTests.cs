using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
///    Tests that verify a job's Captured Culture round-trips through the real PostgreSQL database.
/// </summary>
public sealed class PostgresCultureTests : IAsyncLifetime
{
   private readonly PostgresFixture _fixture;
   private readonly PostgresStorageHarness _harness;
   private readonly ServiceProvider _services;
   private readonly JobScheduler _scheduler;

   private PostgresJobStorage Storage => _harness.Storage;

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public PostgresCultureTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _harness = new PostgresStorageHarness(fixture);

      var services = new ServiceCollection();
      services.RegisterJob<CultureRecordingJob>();
      _services = services.BuildServiceProvider();

      _scheduler = new JobScheduler(_services, Storage, _harness.Clock);
   }

   public async ValueTask InitializeAsync()
   {
      await _fixture.ResetAsync();
      await _harness.Storage.InitializeAsync(CancellationToken);
      await _harness.InstanceRepository.RegisterInstance(CancellationToken);
   }

   public ValueTask DisposeAsync() => ValueTask.CompletedTask;

   [Fact]
   public async Task ExplicitCulture_RoundTripsThroughDatabase()
   {
      // Act
      await _scheduler.PerformAsapAsync<CultureRecordingJob, CultureRecordingJob.Parameters>(new CultureRecordingJob.Parameters(), new CultureInfo("nl-NL"), CancellationToken);

      // Assert
      var stored = (await Storage.GetScheduledJobsAsync(CancellationToken)).Single();
      stored.CultureName.Should().Be("nl-NL");
      stored.UICultureName.Should().Be("nl-NL");
   }

   [Fact]
   public async Task AmbientCulture_RoundTripsBothValuesIndependently()
   {
      var originalCulture = CultureInfo.CurrentCulture;
      var originalUICulture = CultureInfo.CurrentUICulture;
      try
      {
         // Arrange
         CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
         CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

         // Act
         await _scheduler.PerformAsapAsync<CultureRecordingJob, CultureRecordingJob.Parameters>(new CultureRecordingJob.Parameters(), CancellationToken);

         // Assert
         var stored = (await Storage.GetScheduledJobsAsync(CancellationToken)).Single();
         stored.CultureName.Should().Be("nl-NL");
         stored.UICultureName.Should().Be("de-DE");
      }
      finally
      {
         CultureInfo.CurrentCulture = originalCulture;
         CultureInfo.CurrentUICulture = originalUICulture;
      }
   }
}
