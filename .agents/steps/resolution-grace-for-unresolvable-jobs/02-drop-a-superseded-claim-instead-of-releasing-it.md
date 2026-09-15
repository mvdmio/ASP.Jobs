# 02 — Drop a superseded Claim instead of releasing it

Status: pending
Blocked by: 01

## What to build

The rolling-deploy shape: the old Worker Instance holds the Claim on a job it cannot load while the new instance re-enqueues the same job name at boot. Releasing the Claim would then put two pending rows with the same application name and job name in the table, which the partial unique index forbids.

So the release is guarded. It carries the same `NOT EXISTS` guard the retry reschedule already uses: the release is refused when another pending row already holds the same application name and job name. When the guard matches no row, that other pending row is the newer schedule and carries the same work — the Claimed row is superseded, so the instance deletes it and logs at Information that it was superseded rather than deferred, matching what the retry path already does for the same situation. The newer pending row is left untouched, keeping one pending row per job name, and the newer schedule wins.

A unique violation arriving concurrently — an insert landing between the check and the commit — is caught and treated the same way. The driver wraps the exception, so the Postgres error is read from the inner exception, exactly as `TryScheduleRetryAsync` does today.

## Footprint

Projects: `src/mvdmio.ASP.Jobs`, `test/mvdmio.ASP.Jobs.Tests.Unit`, `test/mvdmio.ASP.Jobs.Tests.Integration`

- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorage.cs` — the deferral release added in step 01, `DeleteJobByIdAsync`; `TryScheduleRetryAsync` and `SupersedeRetryAsync` are the prior art for the guard, the `QueryException`/`PostgresException` unwrap and the Information log
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresUnresolvableJobTests.cs` — the supersession case
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresStorageTests.cs` — prior art for the concurrent-supersession test shape

## Acceptance criteria

- [ ] With an Unresolvable Claimed row and a second pending row carrying the same application name and job name, Claiming deletes the Unresolvable row instead of releasing it.
- [ ] The second pending row is untouched — same id, same `perform_at`, Claim fields still null — and remains the only pending row for that job name.
- [ ] A unique violation raised concurrently on the release is caught through the inner `PostgresException` and handled the same way, rather than escaping to the caller.
- [ ] Supersession logs at Information and is distinguishable from the Debug deferral line.
- [ ] After the supersession the wait loop carries on and still returns another due job when one is available.
- [ ] `dotnet build`, then `dotnet test`, run sequentially, are green.
