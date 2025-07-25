﻿using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace mvdmio.ASP.Jobs.Tests.Unit.Extensions;

public static class AsyncExtensions
{
   /// <summary>
   ///    Allows a cancellation token to be awaited.
   /// </summary>
   [EditorBrowsable(EditorBrowsableState.Never)]
   public static CancellationTokenAwaiter GetAwaiter(this CancellationToken ct)
   {
      // return our special awaiter
      return new CancellationTokenAwaiter {
         CancellationToken = ct
      };
   }

   /// <summary>
   ///    The awaiter for cancellation tokens.
   /// </summary>
   [EditorBrowsable(EditorBrowsableState.Never)]
   public struct CancellationTokenAwaiter : INotifyCompletion, ICriticalNotifyCompletion
   {
      public CancellationTokenAwaiter(CancellationToken cancellationToken)
      {
         CancellationToken = cancellationToken;
      }

      internal CancellationToken CancellationToken;

      public object GetResult()
      {
         // this is called by compiler generated methods when the
         // task has completed. Instead of returning a result, we 
         // just throw an exception.
         if (IsCompleted)
            throw new OperationCanceledException();

         throw new InvalidOperationException("The cancellation token has not yet been cancelled.");
      }

      // called by compiler generated/.net internals to check
      // if the task has completed.
      public bool IsCompleted => CancellationToken.IsCancellationRequested;

      // The compiler will generate stuff that hooks in
      // here. We hook those methods directly into the
      // cancellation token.
      public void OnCompleted(Action continuation)
      {
         CancellationToken.Register(continuation);
      }

      public void UnsafeOnCompleted(Action continuation)
      {
         CancellationToken.Register(continuation);
      }
   }
}