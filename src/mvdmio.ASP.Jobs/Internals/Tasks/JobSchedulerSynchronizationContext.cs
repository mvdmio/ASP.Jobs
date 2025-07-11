using System.Threading;
using System.Threading.Tasks;

namespace mvdmio.ASP.Jobs.Internals.Tasks;

internal sealed class JobSchedulerSynchronizationContext : SynchronizationContext
{
   private readonly TaskScheduler _scheduler;

   public JobSchedulerSynchronizationContext(TaskScheduler scheduler)
   {
      _scheduler = scheduler;
   }

   public override void Post(SendOrPostCallback d, object state)
   {
      Task.Factory.StartNew(() => d(state), CancellationToken.None, TaskCreationOptions.None, _scheduler);
   }

   public override void Send(SendOrPostCallback d, object state)
   {
      var t = Task.Factory.StartNew(() => d(state), CancellationToken.None, TaskCreationOptions.None, _scheduler);
      t.Wait();
   }
}