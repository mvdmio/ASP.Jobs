namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// A job whose Job Parameters hold a Logged Parameter, a secret that is not, and a Logged Parameter holding an object
/// with a Logged Parameter and a secret of its own. Throws from <see cref="Job{TProperties}.ExecuteAsync"/> when asked to.
/// </summary>
public class LoggedParametersJob : Job<LoggedParametersJob.Parameters>
{
   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return parameters.ThrowOnExecute is null ? Task.CompletedTask : throw parameters.ThrowOnExecute;
   }

   public class Parameters
   {
      [LoggedParameter]
      public Guid Id { get; set; } = Guid.NewGuid();

      public string Secret { get; set; } = $"secret-{Guid.NewGuid()}";

      [LoggedParameter]
      public OrderData Order { get; set; } = new();

      public Exception? ThrowOnExecute { get; set; }
   }

   public class OrderData
   {
      [LoggedParameter]
      public Guid Id { get; set; } = Guid.NewGuid();

      public string Token { get; set; } = $"token-{Guid.NewGuid()}";
   }
}
