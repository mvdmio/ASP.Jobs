using System;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Carries information about a single retried attempt, passed to <see cref="Job{TProperties}.OnJobRetryAsync" />.
///    A record so that future fields can be added without breaking existing overrides.
/// </summary>
[PublicAPI]
public sealed record RetryContext
{
   /// <summary>
   ///    The 1-based attempt number of this retry (excluding the first, non-retry attempt).
   /// </summary>
   public required int Attempt { get; init; }

   /// <summary>
   ///    The maximum number of retries allowed by the matched <see cref="RetryBehavior" /> for this Execution Chain.
   /// </summary>
   public required int MaxRetries { get; init; }

   /// <summary>
   ///    The UTC time at which this retry is scheduled to run.
   /// </summary>
   public required DateTime NextAttemptAtUtc { get; init; }
}
