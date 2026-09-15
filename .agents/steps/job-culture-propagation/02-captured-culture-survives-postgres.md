# 02 — Captured Culture survives PostgreSQL

Status: done
Blocked by: 01

## What to build

The two culture-name fields round-trip through PostgreSQL the same way they live on the in-memory store item, so a job scheduled before a restart still runs in the culture it was captured with, and switching backends does not change behavior.

- A timestamped migration adds two **nullable** text columns (`culture`, `ui_culture`) on `mvdmio.jobs`. Null means no Captured Culture; empty string means captured invariant. Use the existing `_202607051200_AddCulture` migration when it is already in the tree — do not add a second culture migration after `_202607151200_AddRetryAttempt`.
- `JobData` maps both fields in both directions (`FromJobStoreItem` / `ToJobStoreItem`).
- Insert, conflict-update, claim (`RETURNING`), and the scheduled/in-progress reads carry both columns. Postgres retries update the existing row in place, so the columns stay with the Execution Chain without a separate copy.
- Captured Culture remains internal: query surfaces and `ScheduledJobInfo` stay unchanged.

This step leaves the whole suite green (unit and integration). Changelog and version bump are out of this step.

## Footprint

Projects: mvdmio.ASP.Jobs, mvdmio.ASP.Jobs.Tests.Unit, mvdmio.ASP.Jobs.Tests.Integration

- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/Migrations/_202607051200_AddCulture.cs` — `_202607051200_AddCulture`
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/Data/JobData.cs` — `Culture`, `UICulture`, `FromJobStoreItem`, `ToJobStoreItem`
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorage.cs` — `ScheduleJobsAsync`, `WaitForNextJobAsync`, `GetScheduledJobsAsync`, `GetInProgressJobsAsync`
- `test/mvdmio.ASP.Jobs.Tests.Unit/JobDataTests.cs` — culture-name round-trip (specific, invariant/empty, null)
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/PostgresCultureTests.cs` — schedule through the real database and read both columns back
- `test/mvdmio.ASP.Jobs.Tests.Integration/Fixtures/PostgresStorageHarness.cs` — Postgres storage harness used by the culture tests
- `test/mvdmio.ASP.Jobs.Tests.Integration/JobSchedulerTests.cs` — equivalency continues to ignore host-dependent captured names; dedicated culture tests own that assertion

## Acceptance criteria

- [x] `JobData` round-trips a specific pair of culture names, the invariant pair (empty string), and null.
- [x] Scheduling with an explicit culture and reading the job back through PostgreSQL returns both stored names.
- [x] Scheduling with differing ambient formatting and UI cultures persists both values independently through PostgreSQL.
- [x] Existing rows with null columns remain valid (no Captured Culture) after the migration.
- [x] `dotnet test` is green for the unit and integration projects.

## Outcome

Postgres culture persistence was already on the branch (`_202607051200_AddCulture`, `JobData` mapping, insert/claim/read SQL). This step closed the remaining acceptance gap and made the whole suite green.

- Added `PostgresCultureTests.RowsWithNullCultureColumns_RemainValidWithNoCapturedCulture` — inserts omitting `culture`/`ui_culture` and asserts both names come back null via `GetScheduledJobsAsync`.
- Fixed a lost-wake race in `InMemoryJobStorage.SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt`: capture `_wakeWaiters` under `_jobQueueLock` and re-check for a due claimable job before sleeping. Without that, a zero-delay group retry could free the group between the empty check and the await, parking `WaitForNextJobAsync` forever. That showed up as `JobRunnerRetryTests.Group_KeepsFlowing_WhileOneMemberRetries` timing out when run in parallel with `JobCulturePropagationTests`.
- Footprint otherwise matched the code; no second culture migration. Whole suite green (unit + integration).
