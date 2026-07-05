using System.Globalization;
using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public sealed class JobCulturePropagationTests
{
   private readonly JobRunnerHarness _harness = new();

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   [Fact]
   public async Task ExplicitCulture_IsReappliedDuringExecution()
   {
      // Arrange
      var parameters = new CultureRecordingJob.Parameters();

      // Act
      await _harness.Scheduler.PerformAsapAsync<CultureRecordingJob, CultureRecordingJob.Parameters>(parameters, new CultureInfo("nl-NL"), CancellationToken);
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert - the explicit culture is applied to both the formatting and UI culture.
      parameters.Executed.Should().BeTrue();
      parameters.ObservedCulture.Should().Be("nl-NL");
      parameters.ObservedUICulture.Should().Be("nl-NL");
   }

   [Fact]
   public async Task AmbientCulture_IsCapturedIndependentlyAndReapplied()
   {
      var originalCulture = CultureInfo.CurrentCulture;
      var originalUICulture = CultureInfo.CurrentUICulture;
      try
      {
         // Arrange - a request-like thread where formatting culture and UI culture differ.
         CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
         CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

         var parameters = new CultureRecordingJob.Parameters();
         await _harness.Scheduler.PerformAsapAsync<CultureRecordingJob, CultureRecordingJob.Parameters>(parameters, CancellationToken);

         // The scheduling thread's culture changes before the job runs; the job must still run under what was captured.
         CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
         CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

         // Act
         await _harness.RunAndDrainAsync(CancellationToken);

         // Assert - both ambient values captured independently.
         parameters.ObservedCulture.Should().Be("nl-NL");
         parameters.ObservedUICulture.Should().Be("de-DE");
      }
      finally
      {
         CultureInfo.CurrentCulture = originalCulture;
         CultureInfo.CurrentUICulture = originalUICulture;
      }
   }

   [Fact]
   public async Task ChildJob_InheritsParentCulture()
   {
      // Arrange - a parent job that schedules a child using the default (no-culture) overload.
      var parameters = new CultureChildSchedulingJob.Parameters();

      // Act
      await _harness.Scheduler.PerformAsapAsync<CultureChildSchedulingJob, CultureChildSchedulingJob.Parameters>(parameters, new CultureInfo("nl-NL"), CancellationToken);
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert - the child captured the parent's reapplied culture automatically.
      parameters.Child.Executed.Should().BeTrue();
      parameters.Child.ObservedCulture.Should().Be("nl-NL");
      parameters.Child.ObservedUICulture.Should().Be("nl-NL");
   }

   [Fact]
   public async Task UnresolvableCulture_RoutesToOnJobFailed_WithoutExecuting()
   {
      // Arrange - bypass the scheduler (which only ever captures valid culture names) and store a job with an
      // unresolvable culture name directly. A name longer than the max locale-name length always throws.
      var parameters = new CultureRecordingJob.Parameters();
      await _harness.Storage.ScheduleJobAsync(
         new JobStoreItem {
            JobType = typeof(CultureRecordingJob),
            Parameters = parameters,
            Options = new JobScheduleOptions(),
            PerformAt = _harness.Clock.UtcNow,
            CultureName = new string('x', 100),
            UICultureName = new string('x', 100)
         },
         CancellationToken
      );

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert - the failure surfaced through the normal job-failure path; ExecuteAsync never ran.
      parameters.Failed.Should().BeTrue();
      parameters.Executed.Should().BeFalse();
   }

   [Fact]
   public async Task PerformCron_WithoutCulture_CapturesInvariant()
   {
      // Act
      await _harness.Scheduler.PerformCronAsync<CultureRecordingJob, CultureRecordingJob.Parameters>("0 0 * * *", new CultureRecordingJob.Parameters(), runImmediately: false, CancellationToken);

      // Assert - CRON defaults to the invariant culture (empty-string name), not the scheduling thread's culture.
      var stored = _harness.Storage.ScheduledJobs.Single();
      stored.CultureName.Should().Be(string.Empty);
      stored.UICultureName.Should().Be(string.Empty);
   }

   [Fact]
   public async Task PerformCron_WithCulture_CapturesThatCulture()
   {
      // Act
      await _harness.Scheduler.PerformCronAsync<CultureRecordingJob, CultureRecordingJob.Parameters>("0 0 * * *", new CultureRecordingJob.Parameters(), new CultureInfo("nl-NL"), runImmediately: false, CancellationToken);

      // Assert
      var stored = _harness.Storage.ScheduledJobs.Single();
      stored.CultureName.Should().Be("nl-NL");
      stored.UICultureName.Should().Be("nl-NL");
   }

   [Fact]
   public async Task CapturedCulture_DoesNotLeakToNextJobWithoutCapturedCulture()
   {
      // Arrange - a single runner thread so the two jobs run sequentially; the first carries a Captured Culture,
      // the second (stored directly) carries none. A leak would show up as the second job observing the first's culture.
      var harness = new JobRunnerHarness(maxConcurrentJobs: 1);

      var withCulture = new CultureRecordingJob.Parameters();
      await harness.Storage.ScheduleJobAsync(
         new JobStoreItem {
            JobType = typeof(CultureRecordingJob),
            Parameters = withCulture,
            Options = new JobScheduleOptions(),
            PerformAt = harness.Clock.UtcNow,
            CultureName = "ja-JP",
            UICultureName = "ja-JP"
         },
         CancellationToken
      );

      var withoutCulture = new CultureRecordingJob.Parameters();
      await harness.Storage.ScheduleJobAsync(
         new JobStoreItem {
            JobType = typeof(CultureRecordingJob),
            Parameters = withoutCulture,
            Options = new JobScheduleOptions(),
            PerformAt = harness.Clock.UtcNow,
            CultureName = null,
            UICultureName = null
         },
         CancellationToken
      );

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert - the no-culture job ran under the thread's ambient culture, not the previous job's Captured Culture.
      withCulture.ObservedCulture.Should().Be("ja-JP");
      withoutCulture.Executed.Should().BeTrue();
      withoutCulture.ObservedCulture.Should().NotBe("ja-JP");
      withoutCulture.ObservedUICulture.Should().NotBe("ja-JP");
   }

   [Fact]
   public async Task PerformCron_CarriesCultureForwardToNextOccurrence()
   {
      // Arrange - a cron job that runs immediately, so the runner schedules the next occurrence in its finally.
      var harness = new JobRunnerHarness(maxConcurrentJobs: 1);
      await harness.Scheduler.PerformCronAsync<CultureRecordingJob, CultureRecordingJob.Parameters>(
         "0 0 * * *", new CultureRecordingJob.Parameters(), new CultureInfo("ja-JP"), runImmediately: true, CancellationToken);

      await harness.Runner.StartAsync(CancellationToken);
      try
      {
         // Act - wait until the immediate occurrence has run and the next (future) occurrence has been scheduled.
         JobStoreItem? next = null;
         while (!CancellationToken.IsCancellationRequested && next is null)
         {
            next = harness.Storage.ScheduledJobs.FirstOrDefault(x => x.CronExpression is not null && x.PerformAt > harness.Clock.UtcNow);
            if (next is null)
               await Task.Delay(10, CancellationToken);
         }

         // Assert - the next occurrence carries the same Captured Culture forward.
         next.Should().NotBeNull();
         next!.CultureName.Should().Be("ja-JP");
         next.UICultureName.Should().Be("ja-JP");
      }
      finally
      {
         await harness.Runner.StopAsync(CancellationToken);
      }
   }
}
