using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mvdmio.ASP.Jobs.Internals.Tasks;

internal sealed class JobTaskScheduler : TaskScheduler, IDisposable
{
   private readonly CancellationTokenSource _cts = new CancellationTokenSource();
   private readonly ConcurrentQueue<Task> _tasks = new ConcurrentQueue<Task>();
   
   private readonly List<Thread> _threads;
   
   public JobTaskScheduler(int numberOfThreads)
   {
      if (numberOfThreads < 1)
         throw new ArgumentOutOfRangeException(nameof(numberOfThreads));

      _threads = Enumerable.Range(0, numberOfThreads).Select(i => {
            var thread = new Thread(DoWork) {
               IsBackground = true
            };

            return thread;
         }
      ).ToList();

      _threads.ForEach(t => t.Start());
   }

   private void DoWork()
   {
      while (true)
      {
         if (_tasks.TryDequeue(out var task))
         {
            TryExecuteTask(task);
         }
         else if (_cts.IsCancellationRequested)
         {
            // Queue is empty and cancellation requested: exit
            break;
         }
         else
         {
            // Queue empty but not cancelled: sleep to wait for new tasks
            Thread.Sleep(10);
         }
      }
   }

   public void Dispose()
   {
      _cts.Cancel();
      
      // Avoid self-join deadlock if Dispose is called from a worker thread
      var currentThread = Thread.CurrentThread;
      _threads.Where(t => t != currentThread).ToList().ForEach(t => t.Join());
      
      _cts.Dispose();
   }

   protected override IEnumerable<Task>? GetScheduledTasks()
   {
      return _tasks;
   }

   protected override void QueueTask(Task task)
   {
      // You MUST enqueue the task here, otherwise the awaits on the task will never complete, resulting in deadlocks.
      // Do NOT return when the Cancellation is requested on _cts.
      
      _tasks.Enqueue(task);
   }

   protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
   {
      return false;
   }
}