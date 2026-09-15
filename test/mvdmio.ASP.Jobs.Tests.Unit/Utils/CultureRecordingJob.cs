using System.Globalization;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Records the <see cref="CultureInfo.CurrentCulture"/> and <see cref="CultureInfo.CurrentUICulture"/> it observes
/// during <see cref="ExecuteAsync"/> and the execution-time hooks, so tests can assert which culture a job ran under.
/// </summary>
public class CultureRecordingJob : Job<CultureRecordingJob.Parameters>
{
   public override RetryPolicy RetryPolicy { get; } = new() {
      new RetryBehavior<InvalidOperationException> { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
   };

   public override Task ExecuteAsync(Parameters properties, CancellationToken cancellationToken)
   {
      properties.ObservedCulture = CultureInfo.CurrentCulture.Name;
      properties.ObservedUICulture = CultureInfo.CurrentUICulture.Name;
      properties.ExecutionCount++;

      if (properties.ExecutionCount <= properties.FailuresBeforeSuccess)
         throw new InvalidOperationException($"CultureRecordingJob induced failure #{properties.ExecutionCount}");

      if (properties.ThrowOnExecute is not null)
         throw properties.ThrowOnExecute;

      properties.Executed = true;
      return Task.CompletedTask;
   }

   public override Task OnJobExecutedAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters.ExecutedHookCulture = CultureInfo.CurrentCulture.Name;
      parameters.ExecutedHookUICulture = CultureInfo.CurrentUICulture.Name;
      return Task.CompletedTask;
   }

   public override Task OnJobFailedAsync(Parameters parameters, Exception exception, CancellationToken cancellationToken)
   {
      parameters.Failed = true;
      parameters.FailedHookCulture = CultureInfo.CurrentCulture.Name;
      parameters.FailedHookUICulture = CultureInfo.CurrentUICulture.Name;
      return Task.CompletedTask;
   }

   public override Task OnJobRetryAsync(Parameters parameters, Exception exception, RetryContext retryContext, CancellationToken cancellationToken)
   {
      parameters.RetryHookCulture = CultureInfo.CurrentCulture.Name;
      parameters.RetryHookUICulture = CultureInfo.CurrentUICulture.Name;
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public string? ObservedCulture { get; set; }
      public string? ObservedUICulture { get; set; }
      public string? ExecutedHookCulture { get; set; }
      public string? ExecutedHookUICulture { get; set; }
      public string? FailedHookCulture { get; set; }
      public string? FailedHookUICulture { get; set; }
      public string? RetryHookCulture { get; set; }
      public string? RetryHookUICulture { get; set; }
      public bool Executed { get; set; }
      public bool Failed { get; set; }
      public int ExecutionCount { get; set; }
      public int FailuresBeforeSuccess { get; set; }
      public Exception? ThrowOnExecute { get; set; }
   }
}
