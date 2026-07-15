using System;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Base class for a single entry in a <see cref="RetryPolicy" />. Use the generic <see cref="RetryBehavior{TException}" />
///    to declare a behavior for a specific exception type.
/// </summary>
[PublicAPI]
public abstract class RetryBehavior
{
   // Sealed to the assembly: only RetryBehavior{TException} may derive from this type.
   internal RetryBehavior()
   {
   }

   /// <summary>
   ///    Returns true if this behavior applies to the given exception (catch-clause semantics: inheritance matching).
   /// </summary>
   internal abstract bool Matches(Exception exception);

   /// <summary>
   ///    The maximum number of retries this behavior allows for one Execution Chain (excluding the first attempt).
   /// </summary>
   internal abstract int MaxRetriesValue { get; }

   /// <summary>
   ///    Computes the delay before the given retry attempt (1-based, excluding the first attempt). Applies the
   ///    behavior's exception-derived override (<see cref="RetryBehavior{TException}.RetryAfter" />) when it is set
   ///    and returns non-null; otherwise falls back to backoff and the delay cap.
   /// </summary>
   /// <param name="exception">The exception that matched this behavior.</param>
   /// <param name="attempt">The upcoming attempt number (1-based, excluding the first attempt).</param>
   internal abstract TimeSpan ComputeDelay(Exception exception, int attempt);
}

/// <summary>
///    Declares how a Job should be retried when it throws an exception of type <typeparamref name="TException" /> (or a
///    subclass of it). Matching follows catch-clause semantics: the first declared <see cref="RetryBehavior" /> in a
///    <see cref="RetryPolicy" /> whose exception type matches wins.
/// </summary>
/// <typeparam name="TException">The exception type this behavior applies to.</typeparam>
[PublicAPI]
public sealed class RetryBehavior<TException> : RetryBehavior
   where TException : Exception
{
   private int _maxRetries;
   private TimeSpan _initialDelay;
   private double _backoffFactor = 1.0;
   private TimeSpan? _maxDelay;

   /// <summary>
   ///    The maximum number of retries allowed for this Execution Chain, excluding the first attempt. Must be at least 1.
   /// </summary>
   public required int MaxRetries
   {
      get => _maxRetries;
      init => _maxRetries = value >= 1
         ? value
         : throw new ArgumentOutOfRangeException(nameof(MaxRetries), value, "MaxRetries must be at least 1.");
   }

   /// <summary>
   ///    The delay before the first retry. Must be zero or positive.
   /// </summary>
   public required TimeSpan InitialDelay
   {
      get => _initialDelay;
      init
      {
         if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(InitialDelay), value, "InitialDelay must be zero or positive.");

         // Object-initializer setters run in the order the caller writes them, not declaration order, so MaxDelay
         // may already be set at this point - re-check the cross-field constraint from this side too, or a
         // MaxDelay-then-InitialDelay initializer order could silently accept MaxDelay < InitialDelay.
         if (_maxDelay is not null && value > _maxDelay.Value)
            throw new ArgumentOutOfRangeException(nameof(InitialDelay), value, "InitialDelay must not exceed MaxDelay.");

         _initialDelay = value;
      }
   }

   /// <summary>
   ///    The multiplier applied to the delay after each retry: delay = InitialDelay * BackoffFactor^(attempt-1). Defaults to
   ///    1.0 (a fixed delay). Must be at least 1.
   /// </summary>
   public double BackoffFactor
   {
      get => _backoffFactor;
      init => _backoffFactor = value >= 1.0
         ? value
         : throw new ArgumentOutOfRangeException(nameof(BackoffFactor), value, "BackoffFactor must be at least 1.");
   }

   /// <summary>
   ///    An optional cap on the computed delay. When set, must be greater than or equal to <see cref="InitialDelay" />.
   /// </summary>
   public TimeSpan? MaxDelay
   {
      get => _maxDelay;
      init => _maxDelay = value is null || value >= _initialDelay
         ? value
         : throw new ArgumentOutOfRangeException(nameof(MaxDelay), value, "MaxDelay must be greater than or equal to InitialDelay.");
   }

   /// <summary>
   ///    Optional function that reads the retry delay directly out of the matched exception (e.g. a server-provided
   ///    <c>Retry-After</c> value), invoked with the exception - strongly typed as <typeparamref name="TException" /> - and
   ///    the upcoming attempt number. A non-null result is used verbatim as the delay before the next attempt -
   ///    <b>bypassing <see cref="MaxDelay" /> entirely</b>: a server-directed wait is trusted as given, not clamped to a
   ///    self-imposed cap. Returning <c>null</c> (or leaving this unset) falls back to the
   ///    <see cref="InitialDelay" />/<see cref="BackoffFactor" />/<see cref="MaxDelay" /> computation, unchanged. A thrown
   ///    exception or a negative result propagates as a failure of the retry attempt.
   /// </summary>
   public Func<TException, int, TimeSpan?>? RetryAfter { get; init; }

   internal override bool Matches(Exception exception)
   {
      return exception is TException;
   }

   internal override int MaxRetriesValue => MaxRetries;

   internal override TimeSpan ComputeDelay(Exception exception, int attempt)
   {
      if (RetryAfter is not null)
      {
         var overrideDelay = RetryAfter((TException)exception, attempt);

         if (overrideDelay is not null)
         {
            if (overrideDelay.Value < TimeSpan.Zero)
               throw new InvalidOperationException($"RetryAfter returned a negative TimeSpan ({overrideDelay.Value}) for attempt {attempt}; it must be zero or positive.");

            return overrideDelay.Value;
         }
      }

      var delay = InitialDelay * Math.Pow(BackoffFactor, attempt - 1);

      if (MaxDelay is not null && delay > MaxDelay.Value)
         return MaxDelay.Value;

      return delay;
   }
}
