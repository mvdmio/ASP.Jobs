using System.Diagnostics;
using System.Text.Json.Serialization;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class TestJob : Job<TestJob.Parameters>
{
   private readonly TestJobRetryPolicyProvider _retryPolicyProvider;

   public TestJob(TestJobRetryPolicyProvider retryPolicyProvider)
   {
      _retryPolicyProvider = retryPolicyProvider;
   }

   public override RetryPolicy RetryPolicy => _retryPolicyProvider.Policy;

   public override async Task<Parameters> ExecuteAsync(Parameters properties, CancellationToken cancellationToken)
   {
      properties.Execute = ObservedCulture.Current;
      properties.ExecuteActivity = Activity.Current;

      if(properties.Delay.HasValue)
         await Task.Delay(properties.Delay.Value, cancellationToken);

      properties.ExecutionCount++;

      // Fails the first `FailuresBeforeSuccess` executions (using RetryableException, or a default), then succeeds.
      if (properties.ExecutionCount <= properties.FailuresBeforeSuccess)
         throw properties.RetryableException ?? new InvalidOperationException($"TestJob induced failure #{properties.ExecutionCount}");

      // Unconditional failure, independent of FailuresBeforeSuccess - used by tests that never expect a retry.
      if (properties.ThrowOnExecute is not null)
         throw properties.ThrowOnExecute;

      return properties;
   }

   // Records nothing in HookCallOrder: JobRunnerRetryTests asserts exact hook lists for jobs scheduled through the Scheduler.
   public override Task OnJobScheduledAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters.OnJobScheduledCallback?.Invoke();

      if (parameters.ThrowInOnJobScheduledAsync is not null)
         throw parameters.ThrowInOnJobScheduledAsync;

      return Task.CompletedTask;
   }

   public override Task OnJobExecutedAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters.ExecutedHook = ObservedCulture.Current;
      parameters.Executed = true;
      parameters.HookCallOrder.Add(nameof(OnJobExecutedAsync));

      if (parameters.ThrowInOnJobExecutedAsync is not null)
         throw parameters.ThrowInOnJobExecutedAsync;

      return Task.CompletedTask;
   }

   public override Task OnJobFailedAsync(Parameters parameters, Exception exception, CancellationToken cancellationToken)
   {
      parameters.FailedHook = ObservedCulture.Current;
      parameters.Crashed = true;
      parameters.FailedWithException = exception;
      parameters.HookCallOrder.Add(nameof(OnJobFailedAsync));

      if (parameters.ThrowInOnJobFailedAsync is not null)
         throw parameters.ThrowInOnJobFailedAsync;

      return Task.CompletedTask;
   }

   public override Task OnJobRetryAsync(Parameters parameters, Exception exception, RetryContext retryContext, CancellationToken cancellationToken)
   {
      parameters.RetryHook = ObservedCulture.Current;
      parameters.RetryContexts.Add(retryContext);
      parameters.HookCallOrder.Add(nameof(OnJobRetryAsync));

      if (parameters.ThrowInOnJobRetryAsync is not null)
         throw parameters.ThrowInOnJobRetryAsync;

      return Task.CompletedTask;
   }

   public class Parameters
   {
      public TimeSpan? Delay { get; set; }
      public Exception? ThrowOnExecute { get; set; }

      /// <summary>Number of executions that should fail (via <see cref="RetryableException"/>) before the job succeeds.</summary>
      public int FailuresBeforeSuccess { get; set; }

      /// <summary>Exception thrown while <see cref="FailuresBeforeSuccess"/> has not yet been reached. Defaults to a generic exception when null.</summary>
      public Exception? RetryableException { get; set; }

      public int ExecutionCount { get; set; }

      public bool Executed { get; set; }
      public bool Crashed { get; set; }

      /// <summary>The exception instance passed to <see cref="OnJobFailedAsync"/>, captured for tests that must confirm which exception surfaced.</summary>
      public Exception? FailedWithException { get; set; }

      /// <summary>When set, thrown from OnJobExecutedAsync/OnJobFailedAsync/OnJobRetryAsync after recording state, to verify these hooks are fire-safe observers.</summary>
      public Exception? ThrowInOnJobExecutedAsync { get; set; }
      public Exception? ThrowInOnJobFailedAsync { get; set; }
      public Exception? ThrowInOnJobRetryAsync { get; set; }

      /// <summary>When set, thrown from OnJobScheduledAsync after <see cref="OnJobScheduledCallback"/> runs, to verify the hook can veto scheduling.</summary>
      public Exception? ThrowInOnJobScheduledAsync { get; set; }

      /// <summary>Invoked from OnJobScheduledAsync, so a test can act at that point (e.g. cancel a token). Not serialised: storage cannot store a delegate.</summary>
      [JsonIgnore]
      public Action? OnJobScheduledCallback { get; set; }

      public List<RetryContext> RetryContexts { get; } = [];
      public List<string> HookCallOrder { get; } = [];

      /// <summary>The current span seen inside ExecuteAsync. Not serialised: a span is no job data.</summary>
      [JsonIgnore]
      public Activity? ExecuteActivity { get; set; }

      public ObservedCulture? Execute { get; set; }
      public ObservedCulture? ExecutedHook { get; set; }
      public ObservedCulture? FailedHook { get; set; }
      public ObservedCulture? RetryHook { get; set; }
   }
}
