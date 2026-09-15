# Idea — Sweep Unresolvable Jobs that never come due

**Status:** needs-triage

Cut from the grilling session that produced the "delete Unresolvable Jobs after the Resolution Grace closes" Spec. Recorded so the gap is not lost.

## The gap

Resolution Grace opens the first time a Worker Instance Claims a job and cannot load its class. A job only gets Claimed once it is due. So a row scheduled far in the future — `PerformAtAsync(DateTime.UtcNow.AddMonths(3), ...)` — whose job class is deleted next week is never Claimed, never found Unresolvable, and never deleted. It sits in `mvdmio.jobs` until it comes due months later, and only then does the five-minute window start.

The same holds for a row whose job class is deleted while the row waits on a long retry backoff.

## Why it was cut

It is quiet. Nothing logs, nothing spins, nothing is destroyed. It is a row at rest, not the warning trickle that motivated the Spec. Catching it needs a periodic sweep that loads every pending job's type, which is real work for a case nobody has reported.

## If picked up

`PostgresCleanupService` already ticks every minute and is the natural home. The hard part is not the sweep — it is that a sweeping instance deleting a row it cannot load, without that row ever having been due, has no Resolution Grace to lean on. It would need its own rule.
