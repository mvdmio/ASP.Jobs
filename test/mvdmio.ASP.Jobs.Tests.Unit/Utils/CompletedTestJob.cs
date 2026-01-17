namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class CompletedTestJob : Job<CompletedTestJob.Parameters>
{
   public override Task<Parameters> ExecuteAsync(Parameters properties, CancellationToken cancellationToken)
   {
      return Task.FromResult(properties);
   }

   public class Parameters
   {
   }
}