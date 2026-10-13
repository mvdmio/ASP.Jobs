namespace mvdmio.ASP.Jobs.Tests.Integration.Jobs;

/// <summary>
///    A test job whose Job Parameters hold a Logged Parameter and an unmarked secret.
///    Used to verify that logging only marked properties does not change what the store keeps.
/// </summary>
public class SecretParametersJob : Job<SecretParametersJob.Parameters>
{
   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public class Parameters
   {
      [LoggedParameter]
      public int Id { get; set; }

      public string? Secret { get; set; }
   }
}
