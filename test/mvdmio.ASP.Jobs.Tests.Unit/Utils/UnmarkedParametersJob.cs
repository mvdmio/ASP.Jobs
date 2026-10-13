namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>A job whose Job Parameters have no Logged Parameter.</summary>
public class UnmarkedParametersJob : Job<UnmarkedParametersJob.Parameters>
{
   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return parameters.ThrowOnExecute is null ? Task.CompletedTask : throw parameters.ThrowOnExecute;
   }

   public class Parameters
   {
      public Guid Id { get; set; } = Guid.NewGuid();

      public string Secret { get; set; } = $"secret-{Guid.NewGuid()}";

      public Exception? ThrowOnExecute { get; set; }
   }
}
