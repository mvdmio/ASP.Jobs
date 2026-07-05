using System.Globalization;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Records the <see cref="CultureInfo.CurrentCulture"/> and <see cref="CultureInfo.CurrentUICulture"/> it observes
/// while executing, so tests can assert which culture a job actually ran under.
/// </summary>
public class CultureRecordingJob : Job<CultureRecordingJob.Parameters>
{
   public override Task ExecuteAsync(Parameters properties, CancellationToken cancellationToken)
   {
      properties.ObservedCulture = CultureInfo.CurrentCulture.Name;
      properties.ObservedUICulture = CultureInfo.CurrentUICulture.Name;
      properties.Executed = true;
      return Task.CompletedTask;
   }

   public override Task OnJobFailedAsync(Parameters parameters, Exception exception, CancellationToken cancellationToken)
   {
      parameters.Failed = true;
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public string? ObservedCulture { get; set; }
      public string? ObservedUICulture { get; set; }
      public bool Executed { get; set; }
      public bool Failed { get; set; }
   }
}
