# 02 — Drop a superseded Claim instead of releasing it

Status: done
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

- [x] With an Unresolvable Claimed row and a second pending row carrying the same application name and job name, Claiming deletes the Unresolvable row instead of releasing it.
- [x] The second pending row is untouched — same id, same `perform_at`, Claim fields still null — and remains the only pending row for that job name.
- [x] A unique violation raised concurrently on the release is caught through the inner `PostgresException` and handled the same way, rather than escaping to the caller.
- [x] Supersession logs at Information and is distinguishable from the Debug deferral line.
- [x] After the supersession the wait loop carries on and still returns another due job when one is available.
- [x] `dotnet build`, then `dotnet test`, run sequentially, are green.

## Outcome

Built as specified. `DeferUnresolvableJobAsync` (from step 01) now guards its release `UPDATE` with the same `NOT EXISTS` clause `TryScheduleRetryAsync` uses (matched on `application_name`/`job_name`/`started_at IS NULL`/`id <> :id`), and reads back the updated row's id via `RETURNING id`.

- `updatedId is null` (guard matched an existing pending row) and a caught `QueryException` whose `InnerException` is a `PostgresException` with `SqlState: PostgresErrorCodes.UniqueViolation` (concurrent insert landing between the check and the commit) both route to a new private `SupersedeUnresolvableJobAsync(job, supersededByDescription, exception, ct)` — mirrors `SupersedeRetryAsync`'s two call sites and message shape. It logs at Information ("was superseded by {description}; the Claim was dropped rather than released"), calls `DeleteJobByIdAsync`, and removes the job's skip-list entry (if any) so a superseded job's entry can't linger.
- On the non-superseded path, behavior is unchanged from step 01: `NOTIFY jobs_updated`, add to skip list, log at Debug, loop `continue`s (this happens in the caller, `WaitForNextJobAsync`, unchanged).
- No footprint drift: the change is entirely inside `DeferUnresolvableJobAsync` plus the one new private helper, both already in `PostgresJobStorage.cs` as named.

### Testing

Added to `PostgresUnresolvableJobTests.cs` (integration): the deletion-instead-of-release case, a case proving the wait loop continues past the supersession to return another due resolvable job, and a 5-iteration concurrent-scheduling race test shaped after `TryScheduleRetryAsync_ShouldSupersedeSafely_UnderConcurrentSchedulingRace`. The race test schedules the superseding job with a future `perform_at` — unlike the retry version, `WaitForNextJobAsync` loops internally after a supersession/defer and would otherwise reclaim a same-tick superseding row itself, which isn't the race this test is isolating (that's a `WaitForNextJobAsync`-specific difference from the single-shot `TryScheduleRetryAsync`, not a bug). Also fixed the race test's setup calls to use `TestContext.Current.CancellationToken` rather than the class's own `CancellationToken` field, which is bound to a single 1-second budget for the whole test method and was being exhausted across the loop's 5 iterations of ~300ms waits each.

### Verification

`dotnet build` (whole solution): green, 0 errors, same pre-existing warnings as step 01 (`NU1903`, one `CS0618`, xUnit1051 in an untouched file).
`dotnet test` run sequentially: Unit `72/72` passed. Integration `71/71` passed (68 from step 01 + 3 new), re-run 3x for flakiness on the new race test with no failures.
