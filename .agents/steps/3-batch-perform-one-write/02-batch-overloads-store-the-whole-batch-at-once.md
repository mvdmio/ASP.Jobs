# 02 — Batch overloads store the whole batch at once

Status: built
Depends on: 01

## What to build

An application that calls a batch overload of the Scheduler gets the whole batch stored in one storage call. Against Postgres that is one write and one `NOTIFY jobs_updated`, so listening Worker Instances wake once. The batch is all-or-nothing: when any job in it fails to schedule, no job from it is stored.

All four batch overloads in `JobScheduler` change: `PerformAsapAsync(IEnumerable<TParameters>)`, `PerformAsapAsync(IEnumerable<TParameters>, CultureInfo)`, `PerformAtAsync(DateTime, IEnumerable<TParameters>)`, and `PerformAtAsync(DateTime, IEnumerable<TParameters>, CultureInfo)`. Each one, in this order:

1. Rejects a null `parameters` and, for the culture overloads, a null `culture`, as today.
2. Captures the culture once for the batch, before any `await` (ADR-0002). The ambient overloads read the thread's culture and UI culture; the culture overloads use `culture.Name` for both.
3. Materialises the parameters once and checks every item before any hook runs. A null item throws `ArgumentNullException` (as the single-job path does), runs no hook, and stores nothing. Like the single-job path's null check, this one logs nothing.
4. For each job, in order: runs `OnJobScheduledAsync` in its own DI scope, then builds one `JobStoreItem` with a fresh `JobScheduleOptions`, so each job keeps its own GUID job name. `PerformAt` is `performAtUtc` for the timed overloads and the clock's `UtcNow` for ASAP, as today.
5. Checks the cancellation token before each hook and before the storage write. A token cancelled by then throws `OperationCanceledException`, and storage is never called.
6. Calls `IJobStorage.ScheduleJobsAsync` once with all items. It skips the call when the batch is empty: no hook, no write, no `NOTIFY`, no throw.
7. After the write succeeds, logs one Information line per job with the message its single-job path logs today. `ScheduleAsapAsync` and `ScheduleAtAsync` word theirs differently: "Scheduled job: … with parameters: …" and "Scheduled Job: … with parameters: … to run at …". On any failure after step 3 (a hook, cancellation, storage), logs one error naming the job type and the batch size, then rethrows.

The single-job paths do not change: `ScheduleAsapAsync`, `ScheduleAtAsync`, `ScheduleCronAsync`, and the runner's retry reschedule keep calling `ScheduleJobAsync` with one item. `IJobScheduler` keeps every signature. Its XML docs on the four batch overloads add that `OnJobScheduledAsync` runs once per job and that the batch is stored completely or not at all.

`TestJob` gains two things, both following its `ThrowInOnJobExecutedAsync` pattern:

- A `ThrowInOnJobScheduledAsync` exception on `Parameters`.
- An optional callback on `Parameters`.

Its new `OnJobScheduledAsync` override invokes the callback, then throws the exception when it is set. The override must not add to `HookCallOrder`, because `JobRunnerRetryTests` asserts exact hook lists for jobs scheduled through the Scheduler. Postgres storage serialises `Parameters` to JSON, so a delegate property must stay out of serialisation.

Release: bump `<Version>` in `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj` from 4.7.0 to 4.8.0. The dated `CHANGELOG.md` entry the Spec asks for, recording that a batch is now all-or-nothing, is written by `/document-changes` after review, not by this step.

## Footprint

Projects: `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj`, `test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj`, `test/mvdmio.ASP.Jobs.Tests.Integration/mvdmio.ASP.Jobs.Tests.Integration.csproj`

- `src/mvdmio.ASP.Jobs/Internals/JobScheduler.cs` — the four batch `PerformAsapAsync` / `PerformAtAsync` overloads and the batch path they share; `ScheduleAsapAsync`, `ScheduleAtAsync` (log wording to reuse, otherwise unchanged); `ScheduleCronAsync` unchanged
- `src/mvdmio.ASP.Jobs/IJobScheduler.cs` — XML docs on the four `IEnumerable<TParameters>` overloads
- `src/mvdmio.ASP.Jobs/Internals/Storage/Interfaces/IJobStorage.cs` — `ScheduleJobsAsync` (contract from step 01)
- `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj` — `<Version>`
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/TestJob.cs` — `OnJobScheduledAsync` override, `Parameters.ThrowInOnJobScheduledAsync`, the callback property
- `test/mvdmio.ASP.Jobs.Tests.Integration/JobSchedulerTests.cs` — new batch cases in the abstract class (run against both `PostgresJobSchedulerTests` and `InMemoryJobSchedulerTests`); the Postgres-only notification case in `PostgresJobSchedulerTests`; `AssertScheduledJobsAsync` (excludes culture today), prior art `PerformAsap_StoresJob`, `PerformAt_StoresJob`
- `test/mvdmio.ASP.Jobs.Tests.Integration/Fixtures/` — the counting `jobs_updated` listener from step 01
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/ThreadCultureScope.cs` — sets the ambient culture for the ambient-overload cases
- `test/mvdmio.ASP.Jobs.Tests.Unit/JobCulturePropagationTests.cs` — `PerformAsap_BatchWithExplicitCulture_RunsEveryItemUnderThatCulture`, `PerformAsap_DefaultBatch_CapturesThreadCultureOnceForWholeBatch`; must stay green
- `test/mvdmio.ASP.Jobs.Tests.Unit/JobRunnerRetryTests.cs` — `HookCallOrder` assertions; must stay green

## Acceptance criteria

- [ ] On both storages, a batch `PerformAsapAsync` stores every job with its own distinct job name, its parameters, `perform_at` equal to the clock's now, and the ambient culture and UI culture captured before the call.
- [ ] On both storages, a batch `PerformAsapAsync` with a `CultureInfo` stores every job with that culture as both culture and UI culture.
- [ ] On both storages, a batch `PerformAtAsync` and a batch `PerformAtAsync` with a `CultureInfo` each store every job with its own job name, its parameters, the given `perform_at`, and the batch's Captured Culture.
- [ ] On both storages, a batch where `OnJobScheduledAsync` throws for one job (not the first) throws that exception and stores no job from the batch.
- [ ] On both storages, a batch whose token is cancelled from the first job's `OnJobScheduledAsync` throws `OperationCanceledException` and stores no job from the batch.
- [ ] On both storages, an empty batch stores nothing and does not throw.
- [ ] On both storages, a batch with a null item throws `ArgumentNullException` and stores nothing.
- [ ] Against Postgres, a batch `PerformAsapAsync` of several jobs raises exactly one `jobs_updated` notification.
- [ ] Single-job `PerformAsapAsync` / `PerformAtAsync`, CRON scheduling, retries, and the culture propagation tests stay green unchanged.
- [ ] `<Version>` in `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj` is 4.8.0.
- [ ] `dotnet build`, then `dotnet test`, pass for the whole solution, one `dotnet` process at a time (Docker running).

## Outcome

Safety fact: every batch `PerformAsapAsync` / `PerformAtAsync` overload runs all `OnJobScheduledAsync` hooks and checks cancellation before calling `IJobStorage.ScheduleJobsAsync` exactly once with the whole batch (skipped for an empty batch), so a hook veto, cancellation, or null item stores no job; if false, a fan-out is stored partly or wakes Postgres listeners once per job (rung 3)
Proof: `dotnet test /data/projects/mvdmio/ASP.Jobs/.claude/worktrees/3-batch-perform-one-write/test/mvdmio.ASP.Jobs.Tests.Integration/mvdmio.ASP.Jobs.Tests.Integration.csproj --filter "FullyQualifiedName~JobSchedulerTests"` exit 0 — 35 passed on both storages, incl. `PerformAsap_Batch_RaisesExactlyOneJobsUpdatedNotification`, `PerformAsap_Batch_StoresNoJob_WhenOnJobScheduledAsyncThrowsForOneJob`, `PerformAsap_Batch_StoresNoJob_WhenCancelledFromTheFirstJobsOnJobScheduledAsync`; transcript in `02-batch-overloads.txt` in the Proof folder
Merge risk: hard — the 4.8.0 `<Version>` bump publishes to NuGet once pushed to `main`, and a published package stays after a revert; before the push, reverting the commit restores the per-job loop. Affects every caller of the four batch overloads (now all-or-nothing, one storage call)

Notes:

- The four overloads share one private `JobScheduler.ScheduleBatchAsync(parameters, DateTime? performAtUtc, cultureName, uiCultureName, ct)`; `performAtUtc: null` means ASAP and reads `_clock.UtcNow` per job, as the single-job path does.
- The overloads stay `async`, so argument exceptions still surface through the returned Task, as before.
- `TestJob.Parameters.OnJobScheduledCallback` is an `Action?` marked `[JsonIgnore]`; the new `OnJobScheduledAsync` override adds nothing to `HookCallOrder`.
- `CHANGELOG.md` untouched, per the run's instructions.

