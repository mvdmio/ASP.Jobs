namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class ReadCountingJob : Job<ReadCountingJob.Parameters>
{
   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public class Parameters
   {
      private int _reads;

      [LoggedParameter]
      public int Counted
      {
         get
         {
            Interlocked.Increment(ref _reads);
            return 42;
         }
      }

      public int Reads => _reads;
   }
}
