namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class TestJob : IJob
{
   public Task OnJobScheduledAsync(object properties, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public Task ExecuteAsync(object properties, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public Task OnJobExecutedAsync(object properties, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public Task OnJobFailedAsync(object parameters, Exception exception, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }
}