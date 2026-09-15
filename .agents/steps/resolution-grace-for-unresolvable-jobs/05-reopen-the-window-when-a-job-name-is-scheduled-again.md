# 05 — Reopen the window when a job name is scheduled again

Status: done
Blocked by: 01, 02, 03, 04

## What to build

Scheduling a job under a name that already has a pending row must produce a row that describes the *new* schedule. Today the scheduling upsert's `ON CONFLICT ... DO UPDATE` list sets only the id, the parameters, both cultures, the scheduled time and the attempt count, so two things go wrong.

- **The window never reopens.** A pending row stamped by an earlier deploy keeps its stamp when the job is scheduled again, so a fresh schedule can be deleted because of an old stamp. `unresolvable_since` joins the update list, set to null: re-scheduling a job name opens Resolution Grace from the start.
- **A pre-existing bug: the job class never changed.** Because `job_type` was never in the update list, re-scheduling a job name onto an existing pending row kept the **old** job class — pointing an existing job name at a different class silently kept running the old one. `job_type`, `parameters_type`, `cron_expression` and `job_group` join the update list too, so the new class is the one that runs.

This second fix is independent of Resolution Grace and must be called out as its own change rather than slipped in: say so plainly in this step's `## Outcome`, so that `/document-changes` writes it up as a separate fix in the Changelog.

Nothing else about scheduling changes: no change to scheduling semantics, to finalization after a successful run, to CRON recurrence, or to culture capture. In-memory storage stays untouched — it holds live type objects and can never have an Unresolvable Job. There is no new configuration and no new public method, so upgrading stays a package bump.

This is the last step of the Spec: it leaves the whole suite green.

## Footprint

Projects: `src/mvdmio.ASP.Jobs`, `test/mvdmio.ASP.Jobs.Tests.Unit`, `test/mvdmio.ASP.Jobs.Tests.Integration`

- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorage.cs` — `ScheduleJobsAsync`
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresStorageTests.cs` — `ScheduleJob_ShouldUpdateNotStartedJob` and neighbours
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresUnresolvableJobTests.cs` — the re-scheduling case, which covers both the window reopening and the job-class bug

## Acceptance criteria

- [x] Re-scheduling a job under a name whose pending row carries a stamp clears `unresolvable_since` on that row.
- [x] Re-scheduling a job name onto an existing pending row updates the stored job class, parameters class, CRON expression and group, and the job that then runs is the new class.
- [x] The table still holds exactly one pending row for that application name and job name.
- [x] No new public member, no new configuration property, and no change to the in-memory storage.
- [x] The job-class fix is recorded in this step's `## Outcome` as a separate, pre-existing bug so it reaches the Changelog on its own.
- [x] `dotnet build`, then `dotnet test` across the whole solution, run sequentially, are green — including the Postgres integration tests with Docker running.

## Outcome

Extended the scheduling upsert's `ON CONFLICT (application_name, job_name) WHERE started_at IS NULL DO UPDATE SET` list in `ScheduleJobsAsync` (`src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorage.cs`) to also set `job_type = EXCLUDED.job_type`, `parameters_type = EXCLUDED.parameters_type`, `cron_expression = EXCLUDED.cron_expression`, `job_group = EXCLUDED.job_group`, and `unresolvable_since = NULL`, alongside the id/parameters/cultures/perform_at/attempt fields it already set. No other line of the statement, and no other method, changed.

Two independent things land in this one change, exactly as the Step file frames them:

1. **Resolution Grace fix (this Spec):** re-scheduling a job name whose pending row carries an `unresolvable_since` stamp now clears that stamp, so a fresh schedule reopens the window from scratch instead of inheriting a stale one it could be deleted against.
2. **Pre-existing bug, independent of Resolution Grace — call out separately for the Changelog:** `job_type` (and `parameters_type`, `cron_expression`, `job_group`) was never in the upsert's update list. Re-scheduling an existing job name onto a pending row silently kept running the **old** job class, parameters class, CRON expression, and group, ignoring the new schedule's values entirely. This is fixed here as its own change, not folded into the Resolution Grace narrative.

Tests added:
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresStorageTests.cs`: `ScheduleJob_ShouldUpdateJobClass_WhenReschedulingOntoAnExistingPendingRow` — schedules a `TestJob`, then re-schedules the same job name onto `CompletedTestJob` (a distinct class/parameters type already used elsewhere in the suite) with a new CRON expression and group; asserts the stored row now carries the new job type, parameters type, CRON expression and group, that there is still exactly one pending row, and that the job actually Claimed and run is the new class.
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresUnresolvableJobTests.cs`: `ScheduleJob_ReopensTheResolutionGraceWindow_WhenReschedulingAJobNameThatCarriesAStamp` — writes an Unresolvable, stamped row via the fixture's raw-SQL helper, then re-schedules the same job name onto a resolvable `CompletedTestJob`; asserts the stamp is cleared, the new class is stored, exactly one pending row remains, and the subsequent Claim runs the new class rather than deferring or deleting it. This is the case the Step file names as covering both the window reopening and the job-class bug together.

`dotnet build` then `dotnet test` (sequential, Docker running) are green across the whole solution: 72 unit tests, 78 integration tests, 0 failures.

## Deviations

none — the change landed exactly where the Step file's footprint named (the `ON CONFLICT ... DO UPDATE` list inside `ScheduleJobsAsync`), and both required test cases (in `PostgresStorageTests.cs` and `PostgresUnresolvableJobTests.cs`) were added as the footprint anticipated. The job-class fix is a pre-existing, Resolution-Grace-independent bug, recorded here as its own change so `/document-changes` writes it up separately in the Changelog, per the Step file's explicit instruction.
