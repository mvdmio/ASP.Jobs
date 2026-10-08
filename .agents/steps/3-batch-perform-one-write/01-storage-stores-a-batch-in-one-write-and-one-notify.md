# 01 — Storage stores a batch in one write and one NOTIFY

Status: done
Depends on: none

## What to build

`IJobStorage.ScheduleJobsAsync` stores a whole batch of `JobStoreItem`s in one write, completely or not at all. Postgres storage sends one `NOTIFY jobs_updated` for it. An empty batch writes nothing and wakes nobody. The Scheduler still sends one item per call after this step; step 02 makes its batch overloads send the whole batch.

Postgres `ScheduleJobsAsync`:

- The Initialization Guard (`ThrowIfNotInitialized`) stays the first thing it does, even for an empty batch.
- It materialises the items and converts each one to `JobData` before it touches the database. When any conversion fails, no row is stored.
- It removes duplicate job names within the call; the last item with a given name wins, its id included. This matches `InMemoryJobStorage`, which keys pending jobs by job name. It never fails with "ON CONFLICT DO UPDATE command cannot affect row a second time".
- It writes every row in one `INSERT … ON CONFLICT (application_name, job_name) WHERE started_at IS NULL DO UPDATE` statement. The `DO UPDATE` column list stays exactly as it is today, including `attempt = 0` and `unresolvable_since = NULL`, so rescheduling a job name still reopens Resolution Grace (ADR-0005).
- No batch size can hit Postgres's limit of 65,535 bind parameters. One array parameter per column, expanded with `unnest`, has no limit; the implementer picks the mechanism. A mechanism that splits the write into several statements must run them in one transaction, so the batch stays all-or-nothing. With one statement no transaction is needed.
- It sends one `NOTIFY jobs_updated` after the write. With no items it touches no database and sends nothing.
- It drops the unused `instance_id` entry from the parameters.
- `ScheduleJobAsync` keeps delegating with one item, so single-job scheduling, CRON scheduling, and the retry reschedule run through the new statement unchanged in behaviour.

In-memory `ScheduleJobsAsync` keeps storing a batch under one lock with one wake signal. When the batch is empty it sends no wake signal.

The `IJobStorage.ScheduleJobsAsync` XML doc states the contract: all items or none, and the last item wins when two share a job name.

Test notes:

- `PostgresStorageTests` shares one token that cancels after one second. The large-batch test may need its own, longer token.
- For thousands of rows, check the row count and the set of job names. A full `BeEquivalentTo` over thousands of items is too slow.
- `DatabaseConnection.WaitAsync` only reports whether a first notification arrived. Proving "exactly one" needs a listener that counts notifications over a short window after the write. Put it where step 02's scheduler test can reuse it.

## Footprint

Projects: `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj`, `test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj`, `test/mvdmio.ASP.Jobs.Tests.Integration/mvdmio.ASP.Jobs.Tests.Integration.csproj`

- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorage.cs` — `ScheduleJobsAsync`, `ScheduleJobAsync` (delegates, stays as is), `ThrowIfNotInitialized`
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/Data/JobData.cs` — `FromJobStoreItem` (the conversion that must finish before any database access)
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/Migrations/_202507091530_Setup.cs` and later migrations — column types for the array parameters (`perform_at` is `TIMESTAMPTZ`, `parameters_json` is `jsonb`); read only, no schema change
- `src/mvdmio.ASP.Jobs/Internals/Storage/InMemoryJobStorage.cs` — `ScheduleJobsAsync`, `SendWakeSignal`
- `src/mvdmio.ASP.Jobs/Internals/Storage/Interfaces/IJobStorage.cs` — `ScheduleJobsAsync` XML doc
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresStorageTests.cs` — new `ScheduleJobs…` cases; `GetJobsFromDatabase`, `_db`, the one-second `_cts`; prior art `ScheduleJob_BasicJob`, `ScheduleJob_ShouldUpdateNotStartedJob`, `ScheduleJob_ShouldUpdateJobClass_WhenReschedulingOntoAnExistingPendingRow`, and the direct `UPDATE … SET unresolvable_since` in `TryScheduleRetryAsync_ClearsUnresolvableSince_LeftOverFromAnEarlierEpisode`
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresUnresolvableJobTests.cs` — prior art `WaitForNextJob_RaisesJobsUpdatedNotification_WhenDeferringAnUnresolvableJob` and `ScheduleJob_ReopensTheResolutionGraceWindow_WhenReschedulingAJobNameThatCarriesAStamp`; must stay green
- `test/mvdmio.ASP.Jobs.Tests.Integration/Fixtures/PostgresFixture.cs` — `ConnectionString`, `DatabaseConnection`; a counting `jobs_updated` listener can live under `Fixtures/`
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/InitializationGuardTests.cs`, `PostgresStorageConnectionLeakTests.cs` — existing single-job coverage through the new statement; must stay green
- `test/mvdmio.ASP.Jobs.Tests.Unit/InMemoryJobStorageTests.cs` — new duplicate-name and empty-batch cases; `AddNewJobStoreItems`, existing `AddJobs`
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/JobStoreItemFactory.cs` — `MakeTestJob` (`useNullParameters: true` gives an item whose conversion to `JobData` fails)

## Acceptance criteria

- [ ] Postgres `ScheduleJobsAsync` with several items stores all of them, equivalent to the items scheduled.
- [ ] Postgres `ScheduleJobsAsync` with an item whose name matches an existing pending row, stamped with `unresolvable_since`, leaves one row. That row holds the new item's values, `unresolvable_since` is null, and `attempt` is 0.
- [ ] Postgres `ScheduleJobsAsync` with two items that share a job name leaves one row, holding the last item (its id, parameters, and `perform_at`).
- [ ] Postgres `ScheduleJobsAsync` with at least 5,958 items stores every row.
- [ ] Postgres `ScheduleJobsAsync` with one item that cannot be converted to `JobData` throws and stores no row from that call.
- [ ] A connection listening on `jobs_updated` receives exactly one notification for a Postgres `ScheduleJobsAsync` call with several items, and none for a call with no items.
- [ ] Postgres `ScheduleJobsAsync` with no items stores nothing and does not throw.
- [ ] In-memory `ScheduleJobsAsync` with two items that share a job name leaves one scheduled job: the last item.
- [ ] In-memory `ScheduleJobsAsync` with no items stores nothing and does not throw.
- [ ] Single-job scheduling, the Resolution Grace reopen, the Initialization Guard, and connection-leak tests stay green unchanged.
- [ ] `dotnet build`, then `dotnet test`, pass, one `dotnet` process at a time (Docker running).

## Outcome

Safety fact: Postgres `ScheduleJobsAsync` checks the Initialization Guard first, converts every item before touching the database, then stores the whole batch (deduplicated by job name, last wins) in one `INSERT … SELECT FROM unnest(...) ON CONFLICT … DO UPDATE` statement followed by one `NOTIFY jobs_updated`, and does nothing for an empty batch; if false, a batch is stored partly, fails on a duplicate name or past 65,535 bind parameters, or wakes listening Worker Instances more than once or for nothing (rung 3)
Proof: `dotnet test /data/projects/mvdmio/ASP.Jobs/.claude/worktrees/3-batch-perform-one-write/test/mvdmio.ASP.Jobs.Tests.Integration/mvdmio.ASP.Jobs.Tests.Integration.csproj --filter "FullyQualifiedName~PostgresStorageTests.ScheduleJobs|FullyQualifiedName~InitializationGuardTests"` exit 0 — 10 passed incl. `ScheduleJobs_SendsNoNotification_ForAnEmptyBatch`, `ScheduleJobs_StoresEveryRow_WhenTheBatchExceedsThePerStatementBindParameterLimit` (6,000 rows), `ScheduleJobs_StoresNothing_WhenOneItemCannotBeConverted`; transcript in `01-storage-batch-checker.txt` in the Proof folder
Merge risk: easy — no schema change, no data rewrite; reverting the commit restores the per-row upsert loop; affects every Postgres scheduling path (single-job, CRON, batch) since `ScheduleJobAsync` delegates to the new statement

Notes for step 02:

- `test/mvdmio.ASP.Jobs.Tests.Integration/Fixtures/JobsUpdatedListener.cs` counts `jobs_updated` notifications: `await using var l = await JobsUpdatedListener.StartAsync(fixture.ConnectionString, ct);` before the write, then `await l.CountNotificationsAsync(TimeSpan.FromMilliseconds(500), ct)` once afterwards (the window ends by cancelling the wait, so call it once per listener). Pass a token that outlives the window; the one-second shared tokens are too short when combined with other work.
- `application_name` is bound as one scalar from configuration, not per row; the INSERT column list and `DO UPDATE` list are unchanged (the INSERT still does not write `attempt`, so a fresh row gets the column default 0).
- `perform_at` is passed as an untyped `DateTime[]`, letting Npgsql infer the type as the single-row path did; `parameters_json` is typed `jsonb[]`, nullable text columns typed `text[]`.
- `InitializationGuardTests.ScheduleJobs_ThrowsBeforeInitialization_EvenForAnEmptyBatch` pins the guard on an empty batch.
- Keep every `perform_at` in a batch UTC, as the Scheduler already does: Npgsql infers the array type from `DateTime.Kind`, and a batch mixing kinds is untested.
- In-memory `ScheduleJobsAsync` returns before taking the lock for an empty batch, so it neither waits on nor observes the cancellation token in that case.
