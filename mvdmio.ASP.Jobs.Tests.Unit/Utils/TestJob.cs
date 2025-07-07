namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class TestJob : Job<TestJob.Parameters>
{
   public class Parameters
   {
      public required int Delay { get; set; }
      
      internal CancellationTokenSource Cts { get; } = new();
      public Exception? ThrowOnExecute { get; set; }
      public bool Executed { get; set; } = false;
      public bool Crashed { get; set; } = false;
      

      public async Task Complete()
      {
         if (Delay > 0)
            await Task.Delay(Delay, Cts.Token);
         
         await Cts.CancelAsync();
      }

      public async Task Crash(Exception ex)
      {
         ThrowOnExecute = ex;
         await Complete();
      }
   }
   
   public override async Task ExecuteAsync(Parameters properties, CancellationToken cancellationToken)
   {
      while (properties.Cts.IsCancellationRequested == false)
      {
         await Task.Delay(1, cancellationToken);   
      }
      
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
}