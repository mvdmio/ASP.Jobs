# 03 — Delete a job when Resolution Grace closes

Status: done
Blocked by: 02

## What to build

Resolution Grace is five minutes. When that window has closed and the job is still Unresolvable, the Worker Instance that next Claims it confirms it still cannot load the class, deletes the row, and logs one warning — the only warning this design produces. Without this step a job class a developer genuinely removed would leave a row every instance re-Claims and defers for the life of the table.

So, at a Claim where either type fails to load: if the row carries a stamp five minutes old or older by the storage clock, the instance deletes the row, drops the id from its skip list, logs the warning, and continues the wait loop. If the stamp is newer than five minutes, or absent, the job is deferred as step 01 built it.

The warning names the job name, the job id, the stored type name, and how long the job stayed unloadable — a real problem an operator can act on ("this job class needs cleaning up"), not the deploy-overlap noise the old wording produced.

The five-minute window is one constant in the library, shared with the skip-list expiry, and there is no configuration option for it: this is a published package whose public API is a contract. The skip entry expiring is what brings the deletion about — once it lapses the instance Claims the job again, re-confirms it cannot load the class, finds the stamp now old enough, and deletes.

A stamp on its own never causes a deletion: deletion always happens at a Claim, where the instance has just re-confirmed it cannot load the class. Deleting from the periodic cleanup service, which never loads a thing, was considered and rejected in ADR-0005; `PostgresCleanupService` stays untouched.

Time in the tests moves through the storage clock, never by sleeping.

## Footprint

Projects: `src/mvdmio.ASP.Jobs`, `test/mvdmio.ASP.Jobs.Tests.Unit`, `test/mvdmio.ASP.Jobs.Tests.Integration`

- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorage.cs` — `WaitForNextJobAsync`, `DeleteJobByIdAsync`, the skip list and the five-minute constant from step 01
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresUnresolvableJobTests.cs` — the window-closed cases
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/TestClock.cs` — the clock the integration harness drives (read-only; prior art)

## Acceptance criteria

- [ ] A row whose stamp is five minutes old or older is deleted when Claimed, and the same call goes on to return another due job when one is available.
- [ ] A row whose stamp is newer than five minutes is not deleted — it is deferred, exactly as in step 01.
- [ ] A stamped row is only ever deleted at a Claim where the instance has just failed to load its job class or parameters class.
- [ ] The deletion logs exactly one warning, naming the job name, the job id, the stored type name, and how long the job stayed unloadable.
- [ ] Deleting a job drops its id from the skip list.
- [ ] The five minutes is a single library constant, shared with the skip-list expiry, with no new configuration property and no new public member.
- [ ] Tests move the five minutes through the storage clock; no test sleeps.
- [ ] `dotnet build`, then `dotnet test`, run sequentially, are green.

## Outcome

Built as specified, landing exactly where steps 01/02 left it: `WaitForNextJobAsync`'s failed-load branch now has the full three-way decision the Spec describes. On a failed load it checks `selectedJob.UnresolvableSince`: if it is null or newer than `ResolutionGrace` (5 minutes, the existing constant from step 01), it defers via the existing `DeferUnresolvableJobAsync`/`SupersedeUnresolvableJobAsync` path unchanged; if it is `ResolutionGrace` old or older, a new private `DeleteExpiredUnresolvableJobAsync(job, now, ct)` runs instead - it deletes the row via the existing `DeleteJobByIdAsync`, drops the id from `_unresolvableJobSkipList` (mirroring `SupersedeUnresolvableJobAsync`'s cleanup), and logs the one warning this design produces (job name, job id, stored `JobType`, and `now - UnresolvableSince` as the unloadable duration). Either branch `continue`s the wait loop exactly as before, so a call that deletes one row goes on to Claim/return another due job in the same invocation. No NOTIFY is raised on deletion (nothing else needs waking, matching `FinalizeJobAsync`'s behavior).

- **No schema, no `JobData`, no query changes needed**: `unresolvable_since` was already selected in the Claim's `RETURNING` list by step 01, so `selectedJob.UnresolvableSince` was already available at the decision point; this step is purely the added branch plus one new private method.
- **Boundary is inclusive of "five minutes old or older"**: the comparison is `now - selectedJob.UnresolvableSince.Value >= ResolutionGrace`, matching the acceptance criterion's wording exactly.
- **A stale stamp is never trusted on its own**: the check only ever runs inside the failed-load branch (`jobStoreItem is null`), i.e. only at a Claim where the instance has just re-confirmed it cannot load the class - never from a background sweep, matching ADR-0005 and the Spec's "A stale stamp is never trusted" section. `PostgresCleanupService` was not touched.
- **The five minutes stays a single constant**: reused the existing `ResolutionGrace` field verbatim; no new constant, no new configuration property, no new public member.

### Testing

Extended `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresUnresolvableJobTests.cs` (all driven through the storage/raw-SQL seam per the Spec's Testing Decisions, clock moved via `PostgresStorageHarness.Clock`, no sleeping, no log-text assertions, no reaching into the skip list):

- `WaitForNextJob_DeletesTheRow_WhenTheStampIsFiveMinutesOldOrOlder_AndReturnsAnotherDueJob` - stamp set to exactly 5 minutes old; the same `WaitForNextJobAsync` call deletes the Unresolvable row and returns the other due resolvable job.
- `WaitForNextJob_DefersRatherThanDeletes_WhenTheStampIsNewerThanFiveMinutes` - stamp 4 minutes old; row survives, Claim fields stay null, original stamp preserved (defer path, unchanged from step 01).
- **Fixed a step-01 test invalidated by this step's new behavior**: `WaitForNextJob_KeepsOriginalStamp_WhenDeferredAgainAfterTheSkipEntryExpires` asserted that re-Claiming after this instance's own skip-list entry lapses (clock advanced 6 minutes) defers again with the same stamp. That premise is exactly what this step changes: the skip-list expiry and the Resolution Grace window share the same five minutes by design (Spec: "the skip expiry and the Resolution Grace are deliberately the same five minutes"), so once an instance's own skip entry has lapsed and it re-Claims the row, the stamp it wrote is necessarily old enough to delete - there is no longer a "defer again with the same stamp" case for that scenario. Renamed to `WaitForNextJob_DeletesTheRow_WhenReClaimedAfterItsOwnSkipEntryHasLapsed` and rewritten to assert the row is deleted, which is the correct, spec-mandated outcome (and is itself one of the Spec's callouts: "The entry expiring is what causes the deletion"). This is a fix to a test whose expectation the Spec explicitly supersedes, not a scope change - no other step-01/02 test needed touching.

No drift from the footprint: the change landed entirely inside `PostgresJobStorage.WaitForNextJobAsync` (the new branch) plus one new private helper `DeleteExpiredUnresolvableJobAsync`, exactly as the footprint named.

### Verification

`dotnet build` (whole solution): green, 0 errors, same pre-existing warnings as steps 01/02 (`NU1903`, one `CS0618`, xUnit1051 in an untouched file).
`dotnet test` run sequentially: Unit `72/72` passed. Integration `73/73` passed (71 from step 02 + 2 new, with the one step-01 test corrected in place rather than counted as new).
