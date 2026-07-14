using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    An ordered list of <see cref="RetryBehavior" /> entries declaring which exceptions a Job's failures should retry,
///    how many times, and with what delay. Matching follows catch-clause semantics: the first declared behavior whose
///    exception type matches the thrown exception wins; an exception that matches no behavior is not retried. An empty
///    policy (the default) means a job behaves exactly as it would without a Retry Policy.
/// </summary>
/// <remarks>
///    Supports collection-expression syntax, e.g. <c>RetryPolicy => [new RetryBehavior&lt;HttpRequestException&gt; { ... }];</c>
/// </remarks>
[PublicAPI]
public sealed class RetryPolicy : IEnumerable<RetryBehavior>
{
   private readonly List<RetryBehavior> _behaviors = [];

   /// <summary>
   ///    Adds a <see cref="RetryBehavior" /> to the end of this policy. Declaration order determines matching precedence.
   /// </summary>
   /// <param name="behavior">The behavior to add.</param>
   public void Add(RetryBehavior behavior)
   {
      ArgumentNullException.ThrowIfNull(behavior);
      _behaviors.Add(behavior);
   }

   /// <inheritdoc />
   public IEnumerator<RetryBehavior> GetEnumerator()
   {
      return _behaviors.GetEnumerator();
   }

   IEnumerator IEnumerable.GetEnumerator()
   {
      return GetEnumerator();
   }

   /// <summary>
   ///    Finds the first declared behavior whose exception type matches the given exception (inheritance matching), or null
   ///    if none match.
   /// </summary>
   internal RetryBehavior? FindMatchingBehavior(Exception exception)
   {
      return _behaviors.FirstOrDefault(behavior => behavior.Matches(exception));
   }
}
