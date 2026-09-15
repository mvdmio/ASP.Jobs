# 04 — Never trust a stale stamp

Status: done
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

- [x] A stamped row whose job class and parameters class do load is returned for execution with its `unresolvable_since` cleared in the table.
- [x] A row rescheduled as a retry comes back pending with `unresolvable_since` cleared, its new `perform_at`, its incremented attempt and its Claim fields null.
- [x] A stamped row far older than five minutes that a capable instance can load is run rather than deleted, and after its retry it survives a subsequent Claim by an instance that cannot load it (it is deferred with a fresh stamp, not deleted).
- [x] Deferring still leaves `attempt` untouched and never routes through the retry reschedule.
- [x] `dotnet build`, then `dotnet test`, run sequentially, are green.

## Outcome

Built as specified, landing exactly where the footprint named: `WaitForNextJobAsync`'s resolved branch and `TryScheduleRetryAsync`'s `SET` list, both in `PostgresJobStorage.cs`.

- **Claim path**: in `WaitForNextJobAsync`, once `selectedJob.ToJobStoreItem()` succeeds (both classes loaded), a new private helper `ClearUnresolvableStampAsync(jobId, ct)` runs a one-column `UPDATE ... SET unresolvable_since = NULL WHERE id = :id` before the job is returned for execution. It is only called when `selectedJob.UnresolvableSince is not null`, so a normal never-stamped row's Claim issues no extra write. No `NOTIFY` is raised (nothing else needs waking - the job is about to run in this same process, matching the "unchanged" framing for this path in the Step file).
- **Retry path**: `TryScheduleRetryAsync`'s `UPDATE ... SET` list gained `unresolvable_since = NULL` alongside the existing `perform_at`, `attempt = attempt + 1`, `started_at = NULL`, `started_by = NULL`. No other change to that method - the `NOT EXISTS` guard, the unique-violation catch, and `SupersedeRetryAsync` are untouched.
- Both clears happen unconditionally on their respective success path, matching the Spec's "A stale stamp is never trusted" section verbatim: "A Claim that loads both classes clears the stamp before running the job" / "The retry reschedule clears the stamp in its SET list."
- No schema, migration, or `JobData` change needed - `unresolvable_since` was already selected/mapped by step 01.
- Deferring is untouched: it still only ever runs from the failed-load branch, still leaves `attempt` alone, and still never touches `TryScheduleRetryAsync`.

No drift from the footprint: the change is exactly the two call sites the footprint named, plus one new private helper (`ClearUnresolvableStampAsync`) alongside the existing `DeferUnresolvableJobAsync`/`DeleteExpiredUnresolvableJobAsync`/`SupersedeUnresolvableJobAsync` family.

### Testing

- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresUnresolvableJobTests.cs`: added `WaitForNextJob_ClearsTheStamp_WhenTheClaimedRowsClassesDoLoad` (a resolvable row stamped directly via raw SQL to simulate a stale leftover stamp; Claim returns it and the table shows the stamp cleared) and `WaitForNextJob_DeletesAStaleStampedRow_ThatACapableInstanceRanAndRetried_WhenAnIncapableInstanceLaterClaimsIt` - the exact scenario the Spec's motivating paragraph and this Step's acceptance criteria describe end-to-end: a day-old stamp is cleared by a capable Claim, cleared again (redundantly) by the retry reschedule, the row's type is then swapped to an unloadable one to simulate the class becoming unavailable to the next Claimer, and the subsequent Claim defers with a fresh stamp rather than deleting - proving the stale stamp did not survive to cause a wrongful deletion.
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresStorageTests.cs`: added `TryScheduleRetryAsync_ClearsUnresolvableSince_LeftOverFromAnEarlierEpisode`, mirroring the existing retry tests' shape - stamps the row directly, retries, asserts `UnresolvableSince` is null alongside the existing `Attempt`/`StartedAt`/`StartedBy` assertions.
- All cases driven through the storage/raw-SQL seam per the Spec's Testing Decisions; clock moved via `PostgresStorageHarness.Clock`; no sleeping; no log-text assertions.

### Verification

`dotnet build` (whole solution): green, 0 errors, same pre-existing warnings as steps 01-03 (`NU1903`, one `CS0618`, xUnit1051 in an untouched file).
`dotnet test` run sequentially: Unit `72/72` passed. Integration `76/76` passed (73 from step 03 + 3 new).

### Deviations

none - both clears landed exactly where the Step file and Spec described, with no schema or footprint changes beyond one new private helper.
