using System;
using System.Threading;
using System.Threading.Tasks;

namespace mvdmio.ASP.Jobs.Utils;

/// <summary>
///    Provides helper methods for running async operations synchronously.
/// </summary>
internal class AsyncHelper
{
   private static readonly TaskFactory _taskFactory = new(CancellationToken.None, TaskCreationOptions.None, TaskContinuationOptions.None, TaskScheduler.Default);

   /// <summary>
   ///    Runs the given async function synchronously and returns the result.
   /// </summary>
   /// <typeparam name="TResult">The type of the result.</typeparam>
   /// <param name="func">The async function to run synchronously.</param>
   /// <returns>The result of the async function, or default if the operation was cancelled.</returns>
   public static TResult? RunSync<TResult>(Func<Task<TResult>> func)
   {
      try
      {
         return _taskFactory.StartNew(func).Unwrap().GetAwaiter().GetResult();
      }
      catch (ThreadAbortException)
      {
         // Ignore. Happens when application is shutdown while process is running.
         return default;
      }
      catch (TaskCanceledException)
      {
         // Ignore. Happens when application is shutdown while process is running.
         return default;
      }
   }

   /// <summary>
   ///    Runs the given async function synchronously.
   /// </summary>
   /// <param name="func">The async function to run synchronously.</param>
   public static void RunSync(Func<Task> func)
   {
      try
      {
         _taskFactory.StartNew(func).Unwrap().GetAwaiter().GetResult();
      }
      catch (ThreadAbortException)
      {
         // Ignore. Happens when application is shutdown while process is running.
      }
      catch (TaskCanceledException)
      {
         // Ignore. Happens when application is shutdown while process is running.
      }
   }
}