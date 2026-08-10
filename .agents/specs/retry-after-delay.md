# Exception-Derived Retry Delay (`RetryAfter`)

**Status:** ready-for-agent

_Domain terms used below are defined in `CONTEXT.md` ("Retry Policy"). This feature extends the retry mechanism established by `docs/adr/0003-retry-is-a-storage-reschedule.md` and should be documented in a new ADR (`docs/adr/0004-exception-derived-retry-delay.md`) rather than an edit to 0003, since it is a related-but-separate decision (0003 covers *where* retry delay is computed/stored; this covers *what* computes it)._

## Problem Statement

I declare Retry Policies whose delay is always computed the same way regardless of what actually happened — a fixed value, or exponential backoff from `InitialDelay`/`BackoffFactor`, optionally capped by `MaxDelay`. Some failures, though, tell me exactly how long to wait: a rate-limited HTTP API returns a `Retry-After` value, or the Claude API responds "you may retry this in 4 hours." Today there's no way for a Retry Behavior to read that value out of the exception and use it — I'm stuck with my pre-declared backoff curve even when the failure itself is more authoritative than my guess.

## Solution

`RetryBehavior<TException>` gains an optional `RetryAfter` function: `Func<TException, int, TimeSpan?>`. When set, it's invoked with the matched exception (strongly typed) and the upcoming attempt number, and its result — when non-null — is used verbatim as the delay before the next attempt, bypassing `MaxDelay`. Returning `null` (or leaving `RetryAfter` unset) falls back to the existing `InitialDelay`/`BackoffFactor`/`MaxDelay` computation, unchanged.

```csharp
new RetryBehavior<RateLimitException>
{
   MaxRetries = 5,
   InitialDelay = TimeSpan.FromSeconds(1),
   BackoffFactor = 2.0,
   MaxDelay = TimeSpan.FromMinutes(1),
   RetryAfter = (ex, attempt) => ex.RetryAfterHeader // TimeSpan? — null falls back to backoff
}
```

## User Stories

1. As a library consumer, I want a `RetryBehavior<TException>` to read a wait duration out of the caught exception, so that I can honor a server-provided `Retry-After` value instead of guessing with backoff math.
2. As a library consumer integrating with a rate-limited HTTP API, I want the retry delay to come from the API's own response when available, so that I don't get rate-limited again on the very next attempt.
3. As a library consumer, I want `RetryAfter` to receive the exception as its statically-declared `TException` type (not the base `Exception`), so that I can read strongly-typed members (e.g. a `RetryAfterHeader` property) without casting.
4. As a library consumer, I want `RetryAfter` to also receive the upcoming attempt number, so that I can build attempt-aware logic (e.g. only honor the server value from the second attempt onward) even though today's use case doesn't need it.
5. As a library consumer, I want `RetryAfter` to return `TimeSpan?` rather than `TimeSpan`, so that I can say "I have no override for this particular exception instance" (e.g. the API didn't include a `Retry-After` value this time) and fall through to normal backoff, rather than being forced to duplicate the backoff math myself inside the lambda.
6. As a library consumer, I want a non-null `RetryAfter` result to be honored exactly, even if it exceeds `MaxDelay`, so that an explicit server-directed wait (e.g. "retry in 4 hours") is never silently clamped to a cap I set for my own backoff curve.
7. As a library consumer, I want `MaxDelay` to still cap the delay whenever `RetryAfter` is unset or returns `null` for a given exception, so that my safety rail continues to bound the computed-backoff path exactly as it does today.
8. As a library consumer, I want an exception thrown by my own `RetryAfter` function to propagate rather than be silently swallowed, so that a bug in my delay-parsing logic surfaces immediately instead of masquerading as "no override, use backoff."
9. As a library consumer, I want a negative `TimeSpan` returned from `RetryAfter` to be rejected (throw) rather than silently accepted or clamped, so that a bug computing "time until retry" (e.g. subtracting timestamps in the wrong order) is caught rather than producing a nonsensical immediate/negative delay.
10. As a library consumer, I want `RetryAfter` to be a purely optional, additive property on `RetryBehavior<TException>`, so that every Retry Policy I've already declared keeps compiling and behaving exactly as before.
11. As a library consumer with multiple `RetryBehavior<TException>` entries in one `RetryPolicy`, I want to set `RetryAfter` independently per behavior, so that only the exception types where it makes sense (e.g. rate-limit exceptions) opt in, while others keep using plain backoff.
12. As a maintainer, I want the `RetryAfter`-thrown and negative-value cases to surface through the same failure path as any other unexpected error during retry scheduling (not a new, silent background-task hazard), so that a broken `RetryAfter` implementation fails the job loudly (logged, routed to `OnJobFailedAsync`) instead of crashing or orphaning the runner's fire-and-forget execution task.
13. As a reader of the domain glossary, I want `RetryAfter` to be understood as a detail of the existing "Retry Policy" concept, not a new top-level domain term, so that the glossary doesn't fragment a concept that is still fundamentally "how the Job decides retry timing."

## Implementation Decisions

**Location and shape**
- The new member lives on `RetryBehavior<TException>` (not on `RetryPolicy`), alongside `InitialDelay`, `BackoffFactor`, and `MaxDelay` — this is where the exception's concrete type is already known and where `ComputeDelay` already lives.
- Signature: `Func<TException, int, TimeSpan?>? RetryAfter { get; init; }`. Parameters are the matched exception (as `TException`) and the upcoming attempt number (same "next attempt" value `ComputeDelay` already receives). Returns `null` to defer to the existing computation, or a `TimeSpan` to use verbatim.
- Default is `null` (no override) — purely additive; no existing `RetryBehavior<TException>` usage is affected.

**Precedence and capping**
- When `RetryAfter` is set and returns non-null for a given exception, that value **is** the delay for the upcoming attempt — no further backoff math, and **not** clamped by `MaxDelay`.
- When `RetryAfter` is unset, or returns `null` for a given exception, the existing `InitialDelay * BackoffFactor^(attempt-1)` computation runs, still capped by `MaxDelay` when set, exactly as today.

**Error handling**
- If invoking `RetryAfter` throws, the exception is not caught and treated as "no override" — it must surface as a failure, the same way an unexpected exception anywhere else in the retry-scheduling path would.
- If `RetryAfter` returns a negative `TimeSpan`, that is also treated as a failure (thrown), consistent with the non-negative invariant `InitialDelay` already enforces on this class.
- Concretely: today, the call to `ComputeDelay` inside the runner's retry-scheduling path sits *after* the job's own failure-handling try/catch has already closed (retry eligibility has already been confirmed by that point) and is *not* wrapped by anything that routes to `OnJobFailedAsync`. A bare propagation from that exact call site would surface as an unhandled exception on the runner's per-job background task, not through the job's normal failure path — a new and more dangerous failure mode than "the job failed." To satisfy "propagate, don't swallow" without introducing that hazard, the delay computation (including the `RetryAfter` invocation and its validation) must be wrapped so a thrown/invalid result routes through the same failure path as a normal job failure (logged, `OnJobFailedAsync` invoked, chain finalized) — mirroring the existing precedent where Captured Culture resolution is deliberately placed inside the job's `try` block specifically so an unresolvable culture rides the normal failure path instead of being lost on a fire-and-forget task.

**Public API impact**
- Additive only: one new nullable init-only property on an existing public generic class. No breaking changes, no new overloads elsewhere, no `JobScheduleOptions` changes, no storage/schema changes (Retry Policies are never persisted — they're re-obtained live from `job.RetryPolicy` on every attempt, per ADR 0003 — so `RetryAfter` never needs to be serializable).

**Documentation**
- Add `docs/adr/0004-exception-derived-retry-delay.md` covering: why `RetryAfter` bypasses `MaxDelay` (server-directed waits are trusted as given, not clamped to a self-imposed cap), and why a throw/negative result is surfaced through the failure path rather than swallowed into a fallback.
- No change to `CONTEXT.md`'s "Retry Policy" glossary entry — `RetryAfter` is a mechanism within the "how" that entry already covers, not a new concept.

## Testing Decisions

Good tests here assert externally observable retry-scheduling behavior — the computed next-attempt delay/time and, on failure, that `OnJobFailedAsync` is invoked with the right exception — never the internal arithmetic of `ComputeDelay` in isolation.

**Primary seam — in-memory runner end-to-end.** Prior art: `test/mvdmio.ASP.Jobs.Tests.Unit/JobRunnerRetryTests.cs`, which already exercises the full scheduler + runner + in-memory storage path for retry scenarios (`JobRunnerHarness`, `TestJob`, `TestJobRetryPolicyProvider`). Extend this single seam to cover:
- `RetryAfter` returning a value: the next attempt is scheduled at `now + that value`, even when it exceeds the behavior's `MaxDelay`.
- `RetryAfter` returning `null` for a given exception: the next attempt is scheduled using the normal `InitialDelay`/`BackoffFactor`/`MaxDelay` computation, unchanged from today's behavior.
- No `RetryAfter` set at all: behavior is identical to before this feature existed (regression coverage).
- `RetryAfter` throwing: the job ends up on the normal failure path (`OnJobFailedAsync` invoked with the original execution exception, chain finalized) rather than an unobserved/unhandled exception escaping the runner.
- `RetryAfter` returning a negative `TimeSpan`: same failure-path outcome as the throwing case.
- A `RetryPolicy` with multiple `RetryBehavior<TException>` entries where only one has `RetryAfter` set: confirms the override is per-behavior, not global.

No second seam is needed — `RetryBehavior<TException>`'s existing unit tests (`test/mvdmio.ASP.Jobs.Tests.Unit/RetryPolicyTests.cs`) test matching/dispatch, not delay computation, and there's no established unit-level seam for `ComputeDelay` itself to extend; the end-to-end harness is the highest seam that already covers this exact code path and keeps assertions behavioral.

## Out of Scope

- Any change to `RetryPolicy` itself (dispatch/matching logic is untouched).
- Jitter, or any other change to the existing backoff computation for the non-override path (still governed by ADR 0003's "no jitter in v1").
- Serialization/persistence of `RetryAfter` or any part of `RetryBehavior` — Retry Policies remain code-declared and are never stored.
- A new top-level domain glossary term — this is documented as a detail of the existing "Retry Policy" entry.
- Attempt-aware built-in helpers or convenience constructors for common cases (e.g. parsing HTTP `Retry-After` headers) — consumers write their own `RetryAfter` lambda.
- Changing `MaxRetries`/retry-budget semantics — `RetryAfter` only affects the delay of an already-eligible retry, not whether one is granted.

## Further Notes

- The most important non-obvious behavior to call out in the ADR and XML doc comments: **`RetryAfter` bypasses `MaxDelay`.** This will look like a bug to a future maintainer ("why doesn't the cap apply here?") unless it's documented at the point of definition, not just in an ADR.
- This is additive to a published NuGet package's public API — no version-bump caveats beyond the normal minor-version bump additive members warrant.
- The propagation-safety fix described above (wrapping the delay computation so failures route through the normal job-failure path) is required groundwork for this feature to satisfy the "propagate, don't swallow" decision safely — it is not optional cleanup.
