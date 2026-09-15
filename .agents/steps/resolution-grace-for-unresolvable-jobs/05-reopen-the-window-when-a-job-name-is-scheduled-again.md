# 05 — Reopen the window when a job name is scheduled again

Status: pending
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

- [ ] Re-scheduling a job under a name whose pending row carries a stamp clears `unresolvable_since` on that row.
- [ ] Re-scheduling a job name onto an existing pending row updates the stored job class, parameters class, CRON expression and group, and the job that then runs is the new class.
- [ ] The table still holds exactly one pending row for that application name and job name.
- [ ] No new public member, no new configuration property, and no change to the in-memory storage.
- [ ] The job-class fix is recorded in this step's `## Outcome` as a separate, pre-existing bug so it reaches the Changelog on its own.
- [ ] `dotnet build`, then `dotnet test` across the whole solution, run sequentially, are green — including the Postgres integration tests with Docker running.
