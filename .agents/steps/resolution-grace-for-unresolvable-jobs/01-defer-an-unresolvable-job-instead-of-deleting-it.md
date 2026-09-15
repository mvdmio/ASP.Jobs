# 01 — Defer an Unresolvable Job instead of deleting it

Status: pending
Blocked by: none

## What to build

A Worker Instance that Claims a due job and cannot load its job class or its parameters class stops destroying the row. It stamps the row with the moment the job was first found Unresolvable, releases the Claim without moving the job's scheduled time, remembers the job id in a short-lived skip list so it does not take the same job again, logs at Debug, and carries on looking for other work.

Observable result: with an Unresolvable due job sitting ahead of a resolvable due job in the queue, the storage hands back the resolvable job and leaves the Unresolvable row pending, due and unclaimed — exactly where it was, so a peer Worker Instance running a build that can load the class Claims it with no added delay.

The pieces this step needs:

- **Schema.** One new nullable `TIMESTAMPTZ` column `unresolvable_since` on `mvdmio.jobs`, added by one new migration under the Postgres migrations namespace (identifier matching its filename timestamp, sorting after `202607151200`; migrations are discovered by reflection, so there is no list to edit). No new index. Existing rows migrate to null.
- **Reading it.** `JobData` gains the column, and every query with an explicit column list that must see it (the Claim's `RETURNING`, the listing selects) carries it.
- **The Claim path.** After a successful Claim, if either type fails to load: release the Claim and write the stamp in one statement — clear `started_at` and `started_by`, set `unresolvable_since` to the storage clock's current time *only when it is currently null*, leave `perform_at` and `attempt` alone — then add the id to the skip list, log at Debug, and continue the wait loop.
- **The skip list.** A set of job ids held in the storage object, each entry expiring after five minutes (the same five minutes as the Resolution Grace — step 03 depends on that). The Claim query's inner `SELECT` gains one clause excluding the ids currently in it; ordering, the application filter, the due filter and `FOR UPDATE SKIP LOCKED` are unchanged. Entries are dropped when they expire, so the set cannot grow without bound. The set lives and dies with the process; nothing about correctness depends on it surviving a restart.
- **Waking the peers.** Releasing a Claim this way raises the `jobs_updated` notification, so a peer sitting in its wait loop wakes and takes the job at once (User Story 3). *The Spec is silent on this; it is the reading for this step.*
- **Not spinning.** The Spec is also silent on the wait loop, and the naive shape spins hot: the deferred row is still pending and still due, so the "sleep until the next `perform_at`" query returns a time in the past and the loop turns over again immediately for the whole five minutes. The reading for this step: rows currently in the skip list do not count towards the next-due time, and the wait is bounded by the moment the earliest skip entry expires — so an instance with nothing else to do waits rather than burning a core, and still wakes when that entry lapses.
- **Wording.** The phrase "the type no longer exists" is removed everywhere it appears in the source, including the listing warning and the `IJobStorage` doc comment. The type may well exist, on a peer running a different build — that is the case this work is about. The replacement says the type could not be loaded in this process.
- **Listing is otherwise untouched.** `GetScheduledJobsAsync` and `GetInProgressJobsAsync` keep skipping rows they cannot load and still never delete one.

Deleting an Unresolvable Job is step 03's work: in this step an instance that re-Claims after its skip entry lapses simply defers again, leaving the original stamp in place.

## Footprint

Projects: `src/mvdmio.ASP.Jobs`, `test/mvdmio.ASP.Jobs.Tests.Unit`, `test/mvdmio.ASP.Jobs.Tests.Integration`

- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/Migrations/_202609151200_AddUnresolvableSince.cs` — new migration: `Identifier`, `Name`, `UpAsync` (pattern: `_202607151200_AddRetryAttempt.cs`)
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/Data/JobData.cs` — `UnresolvableSince`
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorage.cs` — `WaitForNextJobAsync`, `SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt`, `FilterResolvableJobs`, the skip-list field and the five-minute constant
- `src/mvdmio.ASP.Jobs/Internals/Storage/Interfaces/IJobStorage.cs` — `DeleteJobByIdAsync` doc comment wording
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresUnresolvableJobTests.cs` — new test file; Unresolvable rows are written with raw SQL through `PostgresFixture.DatabaseConnection`, resolvable rows through the storage, time moved with `PostgresStorageHarness.Clock`
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresStorageTests.cs` — prior art for raw-SQL row shaping and `GetJobsFromDatabase`

## Acceptance criteria

- [ ] An existing database and a fresh one both migrate themselves to carry `unresolvable_since`; rows written before the column existed read as null.
- [ ] Given an Unresolvable due job scheduled earlier than a resolvable due job, `WaitForNextJobAsync` returns the resolvable job.
- [ ] The Unresolvable row is still in the table afterwards, with `started_at` and `started_by` null, `perform_at` unchanged, `attempt` unchanged, and `unresolvable_since` set to the storage clock's time.
- [ ] A row that already carries a stamp keeps the original stamp when deferred again — the stamp is written only when it is null.
- [ ] Called again inside the window, the same storage neither returns the skipped row nor re-Claims it: its Claim fields stay null.
- [ ] With only Unresolvable due rows present, all inside the window, the call returns null when its cancellation token fires, every row is still in the table, and the instance does not spin — it is not re-running the Claim query in a tight loop once every due row is skipped.
- [ ] Deferring raises `jobs_updated`.
- [ ] Deferring logs at Debug and logs no warning.
- [ ] `GetScheduledJobsAsync` and `GetInProgressJobsAsync` still skip rows they cannot load and leave every one of them in the table.
- [ ] The phrase "the type no longer exists" appears nowhere in `src/`.
- [ ] `dotnet build`, then `dotnet test`, run sequentially, are green.
