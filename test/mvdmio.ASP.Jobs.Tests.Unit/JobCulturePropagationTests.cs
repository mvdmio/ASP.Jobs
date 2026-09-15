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
      var parameters = new TestJob.Parameters();

      // Act
      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, new CultureInfo("nl-NL"), CancellationToken);
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert - the explicit culture is applied to both the formatting and UI culture.
      parameters.Executed.Should().BeTrue();
      parameters.Execute.Should().Be(new ObservedCulture("nl-NL", "nl-NL"));
   }

   [Fact]
   public async Task AmbientCulture_IsCapturedIndependentlyAndReapplied()
   {
      using var _ = new ThreadCultureScope("nl-NL", "de-DE");

      var parameters = new TestJob.Parameters();
      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // The scheduling thread's culture changes before the job runs; the job must still run under what was captured.
      CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
      CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

      await _harness.RunAndDrainAsync(CancellationToken);

      parameters.Execute.Should().Be(new ObservedCulture("nl-NL", "de-DE"));
   }

   [Fact]
   public async Task PerformAt_AmbientCulture_IsCapturedAndReapplied()
   {
      using var _ = new ThreadCultureScope("fr-FR", "it-IT");

      var parameters = new TestJob.Parameters();
      await _harness.Scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(_harness.Clock.UtcNow, parameters, CancellationToken);

      CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
      CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

      await _harness.RunAndDrainAsync(CancellationToken);

      parameters.Execute.Should().Be(new ObservedCulture("fr-FR", "it-IT"));
   }

   [Fact]
   public async Task PerformAt_ExplicitCulture_IsReappliedDuringExecution()
   {
      var parameters = new TestJob.Parameters();

      await _harness.Scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(
         _harness.Clock.UtcNow, parameters, new CultureInfo("sv-SE"), CancellationToken);
      await _harness.RunAndDrainAsync(CancellationToken);

      parameters.Execute.Should().Be(new ObservedCulture("sv-SE", "sv-SE"));
   }

   [Fact]
   public async Task PerformAsap_BatchWithExplicitCulture_RunsEveryItemUnderThatCulture()
   {
      var batch = new[] { new TestJob.Parameters(), new TestJob.Parameters() };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(batch, new CultureInfo("nl-NL"), CancellationToken);
      await _harness.RunAndDrainAsync(CancellationToken);

      foreach (var parameters in batch)
      {
         parameters.Executed.Should().BeTrue();
         parameters.Execute.Should().Be(new ObservedCulture("nl-NL", "nl-NL"));
      }
   }

   [Fact]
   public async Task PerformAsap_DefaultBatch_CapturesThreadCultureOnceForWholeBatch()
   {
      using var _ = new ThreadCultureScope("nl-NL", "de-DE");

      var batch = new[] { new TestJob.Parameters(), new TestJob.Parameters() };
      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(batch, CancellationToken);

      CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
      CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

      await _harness.RunAndDrainAsync(CancellationToken);

      foreach (var parameters in batch)
         parameters.Execute.Should().Be(new ObservedCulture("nl-NL", "de-DE"));
   }

   [Fact]
   public async Task OptionsCarryingOverload_AcceptsExplicitCultureWithJobName()
   {
      var parameters = new TestJob.Parameters();
      var options = new JobScheduleOptions { JobName = "culture-options-job", Group = "culture-group" };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(
         parameters, options, new CultureInfo("ja-JP"), CancellationToken);

      var stored = _harness.Storage.ScheduledJobs.Single();
      stored.Options.JobName.Should().Be("culture-options-job");
      stored.Options.Group.Should().Be("culture-group");
      stored.CultureName.Should().Be("ja-JP");
      stored.UICultureName.Should().Be("ja-JP");

      await _harness.RunAndDrainAsync(CancellationToken);

      parameters.Execute.Should().Be(new ObservedCulture("ja-JP", "ja-JP"));
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
      parameters.Child.Execute.Should().Be(new ObservedCulture("nl-NL", "nl-NL"));
   }

   [Fact]
   public async Task UnresolvableCulture_RoutesToOnJobFailed_WithoutExecuting()
   {
      // Arrange - bypass the scheduler (which only ever captures valid culture names) and store a job with an
      // unresolvable culture name directly. A name longer than the max locale-name length always throws.
      var parameters = new TestJob.Parameters();
      await _harness.Storage.ScheduleJobAsync(
         new JobStoreItem {
            JobType = typeof(TestJob),
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
      parameters.Crashed.Should().BeTrue();
      parameters.Executed.Should().BeFalse();
   }

   [Fact]
   public async Task ExecutionTimeHooks_ObserveCapturedCulture()
   {
      _harness.RetryPolicyProvider.Policy = new RetryPolicy {
         new RetryBehavior<InvalidOperationException> { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
      };

      var succeeded = new TestJob.Parameters();
      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(
         succeeded, new CultureInfo("nl-NL"), CancellationToken);

      var failed = new TestJob.Parameters { ThrowOnExecute = new ArgumentException("boom") };
      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(
         failed, new CultureInfo("de-DE"), CancellationToken);

      var retried = new TestJob.Parameters { FailuresBeforeSuccess = 1 };
      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(
         retried, new CultureInfo("fr-FR"), CancellationToken);

      await _harness.RunAndDrainAsync(CancellationToken);

      succeeded.ExecutedHook.Should().Be(new ObservedCulture("nl-NL", "nl-NL"));

      failed.Crashed.Should().BeTrue();
      failed.FailedHook.Should().Be(new ObservedCulture("de-DE", "de-DE"));

      retried.RetryHook.Should().Be(new ObservedCulture("fr-FR", "fr-FR"));
      retried.Executed.Should().BeTrue();
   }

   [Fact]
   public async Task PerformCron_WithoutCulture_CapturesInvariant()
   {
      // Act
      await _harness.Scheduler.PerformCronAsync<TestJob, TestJob.Parameters>("0 0 * * *", new TestJob.Parameters(), runImmediately: false, CancellationToken);

      // Assert - CRON defaults to the invariant culture (empty-string name), not the scheduling thread's culture.
      var stored = _harness.Storage.ScheduledJobs.Single();
      stored.CultureName.Should().Be(string.Empty);
      stored.UICultureName.Should().Be(string.Empty);
   }

   [Fact]
   public async Task PerformCron_WithCulture_CapturesThatCulture()
   {
      // Act
      await _harness.Scheduler.PerformCronAsync<TestJob, TestJob.Parameters>("0 0 * * *", new TestJob.Parameters(), new CultureInfo("nl-NL"), runImmediately: false, CancellationToken);

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

      var withCulture = new TestJob.Parameters();
      await harness.Storage.ScheduleJobAsync(
         new JobStoreItem {
            JobType = typeof(TestJob),
            Parameters = withCulture,
            Options = new JobScheduleOptions(),
            PerformAt = harness.Clock.UtcNow,
            CultureName = "ja-JP",
            UICultureName = "ja-JP"
         },
         CancellationToken
      );

      var withoutCulture = new TestJob.Parameters();
      await harness.Storage.ScheduleJobAsync(
         new JobStoreItem {
            JobType = typeof(TestJob),
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
      withCulture.Execute.Should().Be(new ObservedCulture("ja-JP", "ja-JP"));
      withoutCulture.Executed.Should().BeTrue();
      withoutCulture.Execute!.Value.Culture.Should().NotBe("ja-JP");
      withoutCulture.Execute.Value.UICulture.Should().NotBe("ja-JP");
   }

   [Fact]
   public async Task PerformCron_CarriesCultureForwardToNextOccurrence()
   {
      // Arrange - a cron job that runs immediately, so the runner schedules the next occurrence in its finally.
      var harness = new JobRunnerHarness(maxConcurrentJobs: 1);
      await harness.Scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(
         "0 0 * * *", new TestJob.Parameters(), new CultureInfo("ja-JP"), runImmediately: true, CancellationToken);

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

   [Fact]
   public void PerformNowAsync_ScheduledJobInfo_AndJobScheduleOptions_HaveNoCultureMembers()
   {
      typeof(IJobScheduler).GetMethods()
         .Where(m => m.Name == nameof(IJobScheduler.PerformNowAsync))
         .SelectMany(m => m.GetParameters())
         .Should().NotContain(p => p.ParameterType == typeof(CultureInfo));

      typeof(ScheduledJobInfo).GetProperties().Select(p => p.Name)
         .Should().NotContain(n => n.Contains("Culture", StringComparison.Ordinal));

      typeof(JobScheduleOptions).GetProperties().Select(p => p.Name)
         .Should().NotContain(n => n.Contains("Culture", StringComparison.Ordinal));
   }
}
