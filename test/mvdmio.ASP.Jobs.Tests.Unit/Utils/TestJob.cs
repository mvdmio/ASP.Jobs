namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class TestJob : Job<TestJob.Parameters>
{
   public override async Task ExecuteAsync(Parameters properties, CancellationToken cancellationToken)
   {
      if(properties.Delay.HasValue)
         await Task.Delay(properties.Delay.Value, cancellationToken);
      
      if (properties.ThrowOnExecute is not null)
         throw properties.ThrowOnExecute;
   }

   public override Task OnJobExecutedAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters.Executed = true;
      return Task.CompletedTask;
   }

   public override Task OnJobFailedAsync(Parameters parameters, Exception exception, CancellationToken cancellationToken)
   {
      parameters.Crashed = true;
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public TimeSpan? Delay { get; set; }
      public Exception? ThrowOnExecute { get; set; }
      
      public bool Executed { get; set; }
      public bool Crashed { get; set; }
   }
}