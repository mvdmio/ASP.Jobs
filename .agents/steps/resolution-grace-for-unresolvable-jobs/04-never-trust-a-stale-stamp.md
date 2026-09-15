# 04 — Never trust a stale stamp

Status: pending
Blocked by: 03

## What to build

A job stamped by an old instance, run by a new one, and then put back onto a long retry backoff would carry a stamp far older than the window. The next Claim by an old instance would delete it on the spot, with no grace at all — destroying work a live build can run. This step closes that hole: the stamp is cleared whenever the job proves runnable.

- A Claim that loads both the job class and the parameters class clears `unresolvable_since` on that row before the job runs, and then returns the job for execution as it does today.
- The retry reschedule clears `unresolvable_since` in its `SET` list, alongside the new scheduled time, the incremented attempt and the cleared Claim fields.

Nothing else about these two paths changes. A deferral is still not a failed attempt: it leaves the attempt count alone and never routes through the retry reschedule, so a job waiting for a peer keeps its full Retry Budget.

## Footprint

Projects: `src/mvdmio.ASP.Jobs`, `test/mvdmio.ASP.Jobs.Tests.Unit`, `test/mvdmio.ASP.Jobs.Tests.Integration`

- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorage.cs` — `WaitForNextJobAsync` (the resolved branch), `TryScheduleRetryAsync`
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresUnresolvableJobTests.cs` — the stamp-clearing cases
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresStorageTests.cs` — existing retry-reschedule tests

## Acceptance criteria

- [ ] A stamped row whose job class and parameters class do load is returned for execution with its `unresolvable_since` cleared in the table.
- [ ] A row rescheduled as a retry comes back pending with `unresolvable_since` cleared, its new `perform_at`, its incremented attempt and its Claim fields null.
- [ ] A stamped row far older than five minutes that a capable instance can load is run rather than deleted, and after its retry it survives a subsequent Claim by an instance that cannot load it (it is deferred with a fresh stamp, not deleted).
- [ ] Deferring still leaves `attempt` untouched and never routes through the retry reschedule.
- [ ] `dotnet build`, then `dotnet test`, run sequentially, are green.
