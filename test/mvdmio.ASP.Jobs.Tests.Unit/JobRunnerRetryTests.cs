using AwesomeAssertions;
using Cronos;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

/// <summary>
/// External-behavior tests for the Retry Policy feature over the <see cref="JobRunnerHarness"/> (InMemory storage) seam:
/// what storage contains afterwards and which lifecycle hooks fired, in what order - never runner internals.
/// </summary>
public sealed class JobRunnerRetryTests
{
   private readonly JobRunnerHarness _harness = new();
   private readonly CancellationTokenSource _cts;

   private CancellationToken CancellationToken => _cts.Token;

   public JobRunnerRetryTests()
   {
      _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      _cts.CancelAfter(TimeSpan.FromSeconds(10));
   }

   [Fact]
   public async Task RetryableFailure_SucceedsWithinBudget()
   {
      // Arrange
      _harness.RetryPolicyProvider.Policy = new RetryPolicy {
         new RetryBehavior<InvalidOperationException> { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
      };

      var parameters = new TestJob.Parameters {
         FailuresBeforeSuccess = 2,
         RetryableException = new InvalidOperationException("transient")
      };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      parameters.Executed.Should().BeTrue();
      parameters.Crashed.Should().BeFalse();
      parameters.ExecutionCount.Should().Be(3);
      parameters.RetryContexts.Should().HaveCount(2);
      parameters.RetryContexts[0].Attempt.Should().Be(1);
      parameters.RetryContexts[0].MaxRetries.Should().Be(3);
      parameters.RetryContexts[1].Attempt.Should().Be(2);
      parameters.HookCallOrder.Should().Equal("OnJobRetryAsync", "OnJobRetryAsync", "OnJobExecutedAsync");

      _harness.Storage.ScheduledJobs.Should().BeEmpty();
      _harness.Storage.InProgressJobs.Should().BeEmpty();
   }

   [Fact]
   public async Task RetryBudgetDepleted_EndsChainInFailure()
   {
      // Arrange
      _harness.RetryPolicyProvider.Policy = new RetryPolicy {
         new RetryBehavior<InvalidOperationException> { MaxRetries = 2, InitialDelay = TimeSpan.Zero }
      };

      var parameters = new TestJob.Parameters {
         FailuresBeforeSuccess = int.MaxValue, // never actually recovers
         RetryableException = new InvalidOperationException("persistently transient")
      };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      parameters.Executed.Should().BeFalse();
      parameters.Crashed.Should().BeTrue();
      parameters.ExecutionCount.Should().Be(3); // initial attempt + 2 retries
      parameters.RetryContexts.Should().HaveCount(2);
      parameters.HookCallOrder.Should().Equal("OnJobRetryAsync", "OnJobRetryAsync", "OnJobFailedAsync");

      _harness.Storage.ScheduledJobs.Should().BeEmpty();
      _harness.Storage.InProgressJobs.Should().BeEmpty();
   }

   [Fact]
   public async Task NonMatchingException_FailsImmediately_WithoutRetrying()
   {
      // Arrange
      _harness.RetryPolicyProvider.Policy = new RetryPolicy {
         new RetryBehavior<TimeoutException> { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
      };

      var parameters = new TestJob.Parameters {
         ThrowOnExecute = new InvalidOperationException("not covered by the policy")
      };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      parameters.Crashed.Should().BeTrue();
      parameters.ExecutionCount.Should().Be(1);
      parameters.RetryContexts.Should().BeEmpty();
      parameters.HookCallOrder.Should().Equal("OnJobFailedAsync");
   }

   [Fact]
   public async Task NoRetryPolicy_BehavesExactlyAsWithoutOne()
   {
      // Arrange - default (empty) RetryPolicy: a job without one behaves exactly as it does today.
      var parameters = new TestJob.Parameters {
         ThrowOnExecute = new Exception("no policy configured")
      };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      parameters.Crashed.Should().BeTrue();
      parameters.RetryContexts.Should().BeEmpty();
      _harness.Storage.ScheduledJobs.Should().BeEmpty();
      _harness.Storage.InProgressJobs.Should().BeEmpty();
   }

   [Fact]
   public async Task OnJobExecutedAsyncThrow_DoesNotTriggerOnJobFailed_AndChainStillSucceeds()
   {
      // Arrange - deliberate behavior change (bug fix): OnJobExecutedAsync throwing no longer routes to OnJobFailedAsync.
      var parameters = new TestJob.Parameters {
         ThrowInOnJobExecutedAsync = new Exception("boom from OnJobExecutedAsync")
      };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      parameters.Executed.Should().BeTrue();
      parameters.Crashed.Should().BeFalse();
      _harness.Storage.ScheduledJobs.Should().BeEmpty();
      _harness.Storage.InProgressJobs.Should().BeEmpty();
   }

   [Fact]
   public async Task OnJobFailedAsyncThrow_DoesNotAlterChainOutcome()
   {
      // Arrange
      var parameters = new TestJob.Parameters {
         ThrowOnExecute = new Exception("crash"),
         ThrowInOnJobFailedAsync = new Exception("boom from OnJobFailedAsync")
      };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert - the hook's throw is logged, not propagated; the chain still ends (job removed from storage).
      parameters.Crashed.Should().BeTrue();
      _harness.Storage.ScheduledJobs.Should().BeEmpty();
      _harness.Storage.InProgressJobs.Should().BeEmpty();
   }

   [Fact]
   public async Task OnJobRetryAsyncThrow_DoesNotPreventTheRetryFromBeingWritten()
   {
      // Arrange
      _harness.RetryPolicyProvider.Policy = new RetryPolicy {
         new RetryBehavior<InvalidOperationException> { MaxRetries = 1, InitialDelay = TimeSpan.Zero }
      };

      var parameters = new TestJob.Parameters {
         FailuresBeforeSuccess = 1,
         RetryableException = new InvalidOperationException("transient"),
         ThrowInOnJobRetryAsync = new Exception("boom from OnJobRetryAsync")
      };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      parameters.Executed.Should().BeTrue();
      parameters.RetryContexts.Should().HaveCount(1);
   }

   [Fact]
   public async Task Supersession_AbandonsRetry_SilentlyWithoutFiringHooks()
   {
      // Arrange
      _harness.RetryPolicyProvider.Policy = new RetryPolicy {
         new RetryBehavior<InvalidOperationException> { MaxRetries = 5, InitialDelay = TimeSpan.Zero }
      };

      var failingParameters = new TestJob.Parameters {
         Delay = TimeSpan.FromMilliseconds(100), // gives the test a window to schedule the superseding job before this one retries
         FailuresBeforeSuccess = int.MaxValue,
         RetryableException = new InvalidOperationException("transient")
      };

      var jobName = "SupersedableJob";
      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(failingParameters, new JobScheduleOptions { JobName = jobName }, CancellationToken);

      await _harness.Runner.StartAsync(CancellationToken);
      await WaitUntilAsync(() => _harness.Storage.InProgressJobs.Any(), CancellationToken);

      // Schedule the superseding job far in the future, so the producer never claims it for execution - it only
      // needs to exist as a not-started job of the same name for the supersession check to see a conflict. If it
      // were claimable immediately, the runner could pick it up and finish it before the original's retry-write
      // even runs, which would make this test racy rather than exercising the supersession path.
      var supersedingParameters = new TestJob.Parameters();
      await _harness.Scheduler.PerformAtAsync<TestJob, TestJob.Parameters>(
         _harness.Clock.UtcNow.AddHours(1),
         supersedingParameters,
         new JobScheduleOptions { JobName = jobName },
         CancellationToken);

      await WaitUntilAsync(() => !_harness.Storage.InProgressJobs.Any(), CancellationToken); // the original chain resolves (superseded)
      await _harness.Runner.StopAsync(CancellationToken);

      // Assert
      failingParameters.Crashed.Should().BeFalse("supersession is silent - OnJobFailedAsync must not fire");
      failingParameters.RetryContexts.Should().BeEmpty("OnJobRetryAsync must not fire for a chain that turns out to be superseded");
      supersedingParameters.Executed.Should().BeFalse("it is scheduled an hour from now and the runner has not reached it yet");

      _harness.Storage.ScheduledJobs.Should().ContainSingle(x => x.Options.JobName == jobName && x.Parameters == supersedingParameters);
   }

   [Fact]
   public async Task Group_KeepsFlowing_WhileOneMemberRetries()
   {
      // Arrange
      _harness.RetryPolicyProvider.Policy = new RetryPolicy {
         new RetryBehavior<InvalidOperationException> { MaxRetries = 1, InitialDelay = TimeSpan.Zero }
      };

      var firstParameters = new TestJob.Parameters {
         FailuresBeforeSuccess = 1,
         RetryableException = new InvalidOperationException("transient")
      };
      var secondParameters = new TestJob.Parameters();

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(firstParameters, new JobScheduleOptions { Group = "TestGroup" }, CancellationToken);
      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(secondParameters, new JobScheduleOptions { Group = "TestGroup" }, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert - the second group member is not stalled behind the first member's retry.
      firstParameters.Executed.Should().BeTrue();
      secondParameters.Executed.Should().BeTrue();
   }

   [Fact]
   public async Task CronJob_WaitsForChainToEnd_BeforeSchedulingNextOccurrence()
   {
      // Arrange
      _harness.RetryPolicyProvider.Policy = new RetryPolicy {
         new RetryBehavior<InvalidOperationException> { MaxRetries = 1, InitialDelay = TimeSpan.Zero }
      };

      var parameters = new TestJob.Parameters {
         FailuresBeforeSuccess = 1,
         RetryableException = new InvalidOperationException("transient")
      };

      await _harness.Scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.EverySecond, parameters, runImmediately: true, CancellationToken);

      // Act - drive the runner directly rather than RunAndDrainAsync: a CRON chain always leaves one scheduled
      // job behind (the next occurrence), so a "wait until empty" drain would never terminate.
      await _harness.Runner.StartAsync(CancellationToken);
      await WaitUntilAsync(() => parameters.Executed, CancellationToken);
      await _harness.Runner.StopAsync(CancellationToken);

      // Assert
      parameters.RetryContexts.Should().HaveCount(1, "the failed first occurrence should retry once before succeeding");
      parameters.HookCallOrder.Should().Equal("OnJobRetryAsync", "OnJobExecutedAsync");

      _harness.Storage.InProgressJobs.Should().BeEmpty();
      _harness.Storage.ScheduledJobs.Should()
         .HaveCount(1, "exactly one next occurrence should be scheduled once the chain ends - not one for the failed attempt in between");
   }

   private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
   {
      while (!condition())
      {
         await Task.Delay(5, ct);
      }
   }
}
