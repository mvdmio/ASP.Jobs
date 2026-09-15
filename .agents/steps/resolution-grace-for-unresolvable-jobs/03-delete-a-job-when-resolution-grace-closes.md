# 03 — Delete a job when Resolution Grace closes

Status: pending
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
