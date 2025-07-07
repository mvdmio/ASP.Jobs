namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class CompletedTestJob : Job<CompletedTestJob.Parameters>
{
   public class Parameters
   {
   }
   
   public override Task ExecuteAsync(Parameters properties, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }
}