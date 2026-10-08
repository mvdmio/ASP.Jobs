# Batch PerformAsapAsync and PerformAtAsync store the whole batch in one write and one NOTIFY

## Problem Statement

An application that schedules many jobs at once calls a batch overload of the **Scheduler**: `PerformAsapAsync` or `PerformAtAsync` with an `IEnumerable<TParameters>`. The caller expects one write for the whole batch. Today it gets one write per job.

Each batch overload loops over its parameters and schedules them one at a time. Every job goes through `IJobStorage.ScheduleJobAsync`, which in Postgres storage runs one `INSERT … ON CONFLICT … DO UPDATE` upsert and then one `NOTIFY jobs_updated`. A batch of N jobs therefore costs 2N round trips to Postgres, and every Worker Instance listening on `jobs_updated` wakes N times. Postgres storage already has `ScheduleJobsAsync(IEnumerable<JobStoreItem>)`, but the Scheduler only ever sends it one item.

The mvdmio suite's HealthCheck app shows the cost. Its scheduler jobs run every minute and each fans out to one execution job per monitored target:

- One traced `AzureMetricsScheduleJob` run spent 157 ms on 23 upserts and 67 ms on 23 `NOTIFY` statements. That is 224 ms of a 327 ms run (Sentry trace `a916bf508810b77a200346c97846617c`).
- `UptimeCheckScheduleJob` and `DnsCheckScheduleJob` each write 36 rows per run, the same way.
- Sentry's performance monitoring files these runs as "N+1 Query" problems: HEALTH-CHECK-7, tracked in the suite as michielvandermeer/mvdmio-suite#251.

The cost grows with every monitored target. A batch also fails part-way today: when the fifth job of ten fails to schedule, the first four stay scheduled and the caller sees an exception.

## Solution

A batch overload writes the whole batch to storage in one call. Postgres storage writes all of its rows in one statement and sends one `NOTIFY jobs_updated` for the batch. A batch of N jobs costs two round trips instead of 2N, and listening Worker Instances wake once.

A batch becomes all-or-nothing. When any job in it fails to schedule, no job from that batch is stored.

The public API of `IJobScheduler` does not change. Single-job scheduling, CRON scheduling, and retries keep their current behaviour.

## User Stories

1. As an application developer, I want `PerformAsapAsync` with a list of parameters to store the whole list in one database write, so that a fan-out job does not spend most of its run on round trips.
2. As an application developer, I want `PerformAtAsync` with a list of parameters to store the whole list in one database write, so that timed fan-out gets the same benefit.
3. As an application developer, I want the culture-taking batch overloads (`PerformAsapAsync` and `PerformAtAsync` with a `CultureInfo`) to batch the same way, so that every batch overload behaves alike.
4. As an application developer, I want a batch to wake listening Worker Instances once, so that 36 new jobs do not cause 36 wake-ups on every instance.
5. As an application developer, I want a batch to be stored completely or not at all, so that a failure never leaves half of a fan-out scheduled.
6. As an application developer, I want `OnJobScheduledAsync` to still run once for each job in a batch, before anything is stored, so that my scheduling hooks keep working.
7. As an application developer, I want a hook that throws for one job to stop the whole batch from being stored, so that the hook can veto a batch the same way it vetoes a single job.
8. As an application developer, I want cancellation before the storage write to store nothing from the batch, so that a cancelled fan-out leaves no partial work behind.
9. As an application developer, I want an empty batch to write nothing and send no `NOTIFY`, so that an idle fan-out costs nothing.
10. As an application developer, I want every job in a batch to keep its own job name, Captured Culture, and `perform_at`, so that batching changes nothing about how each job later runs.
11. As an application developer, I want each job in a batch to keep its existing upsert behaviour, so that a pending job with the same name is still replaced and its Resolution Grace still reopens.
12. As an application developer, I want two jobs with the same job name in one storage write to resolve to the last one, so that Postgres storage matches in-memory storage and does not fail with "ON CONFLICT DO UPDATE command cannot affect row a second time".
13. As an application developer, I want a batch of any size to schedule, so that a large fan-out does not hit Postgres's limit of 65,535 bind parameters in one statement.
14. As an application developer, I want single-job overloads to keep their behaviour and cost, so that the change is safe for callers that never batch.
15. As an application developer, I want CRON scheduling and the retry reschedule to stay as they are, so that the Execution Chain rules do not move.
16. As an application developer, I want a log line for each scheduled job and an error log naming the job type when a batch fails, so that I can still trace what was scheduled.
17. As an application developer, I want in-memory storage to keep its batch behaviour, so that unit tests and in-memory hosts see the same results as Postgres.
18. As the mvdmio suite maintainer, I want a released package version with this change, so that the suite can bump `mvdmio.ASP.Jobs` and close #251.

## Implementation Decisions

- **Scheduler batch overloads.** All four batch overloads in `JobScheduler` change: `PerformAsapAsync` with parameters, `PerformAsapAsync` with parameters and culture, `PerformAtAsync` with parameters, and `PerformAtAsync` with parameters and culture. Each one:
  1. Materialises the parameters once and rejects a null item, as the single-job path does today.
  2. Captures the culture once for the batch, before any `await`, as it does today ([ADR-0002](https://github.com/mvdmio/ASP.Jobs/blob/main/docs/adr/0002-job-culture-capture-and-propagation.md)).
  3. Runs `OnJobScheduledAsync` for each job, in order, in its own DI scope, as the single-job path does today.
  4. Builds one `JobStoreItem` per job, each with a fresh `JobScheduleOptions`, so each keeps its own GUID job name.
  5. Calls `IJobStorage.ScheduleJobsAsync` once with all items. It skips the call when the batch is empty.
  6. Logs one Information line for each job after the write succeeds, with the same message its single-job path logs today (`ScheduleAsapAsync` and `ScheduleAtAsync` word theirs differently). On failure it logs one error naming the job type and the batch size, then rethrows.
- **Single-job paths stay as they are.** `ScheduleAsapAsync`, `ScheduleAtAsync`, `ScheduleCronAsync`, and the runner's retry reschedule keep calling `ScheduleJobAsync` with one item. Postgres `ScheduleJobAsync` keeps delegating to `ScheduleJobsAsync` with one item.
- **Postgres `ScheduleJobsAsync`.**
  - It materialises the items and converts them to `JobData` before touching the database. A conversion failure therefore stores nothing.
  - It removes duplicate job names within the call; the last item with a given name wins. This matches `InMemoryJobStorage`, which keys its pending jobs by job name.
  - It writes all rows in one `INSERT … ON CONFLICT (application_name, job_name) WHERE started_at IS NULL DO UPDATE` statement. The `DO UPDATE` column list stays exactly as it is today, including `attempt = 0` and `unresolvable_since = NULL`. Resolution Grace therefore still reopens on reschedule ([ADR-0005](https://github.com/mvdmio/ASP.Jobs/blob/main/docs/adr/0005-unresolvable-jobs-are-deferred-then-deleted-on-a-clock.md)).
  - The statement must not hit Postgres's 65,535 bind-parameter limit. One array parameter per column, expanded with `unnest`, satisfies this for any batch size. The implementer picks the mechanism; per-row parameter lists need chunking.
  - It sends one `NOTIFY jobs_updated` after the write. When there are no items, it sends nothing.
  - One statement is atomic in Postgres, so the batch is all-or-nothing without an explicit transaction.
  - It drops the unused `instance_id` entry from the parameter dictionary, since the statement never read it.
- **In-memory `ScheduleJobsAsync`** already stores a batch under one lock with one wake signal. Only its empty-batch case changes: it sends no wake signal.
- **No public API change.** `IJobScheduler` and `JobScheduleOptions` keep their signatures. `IJobStorage` is internal.
- **No schema change.** The existing partial unique index on `(application_name, job_name) WHERE started_at IS NULL` is the conflict target.
- **Behaviour change to record.** A batch is all-or-nothing. Before this change, jobs written before a failure stayed scheduled.
- **Release.** Bump `<Version>` in `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj` from 4.7.0 to 4.8.0; a minor bump, since behaviour changes and the API does not. Add a dated entry to `CHANGELOG.md`. A push to `main` publishes the package to NuGet.

## Testing Decisions

- A good test drives the public seam and checks stored jobs or observed notifications. It does not assert on SQL text or on how many times a method is called.
- **Main seam: `JobSchedulerTests`.** This abstract class already runs every case against both `PostgresJobSchedulerTests` and `InMemoryJobSchedulerTests`. Add cases there:
  - A batch `PerformAsapAsync` and a batch `PerformAtAsync` each store every job with its own job name, its parameters, its `perform_at`, and the batch's Captured Culture. Cover the culture-taking overloads too.
  - A batch where `OnJobScheduledAsync` throws for one job stores no job from that batch. `TestJob` (in the unit test project's `Utils/TestJob.cs`, shared with the integration tests) needs a `ThrowInOnJobScheduledAsync` property on its `Parameters` and an `OnJobScheduledAsync` override that throws it, following its existing `ThrowInOnJobExecutedAsync` pattern.
  - A batch whose cancellation token is cancelled after the first job's `OnJobScheduledAsync` has run throws `OperationCanceledException` and stores no job from that batch. To cancel at that point, `TestJob.Parameters` can carry an optional callback that its `OnJobScheduledAsync` override invokes; the test's callback cancels the token source.
  - An empty batch stores nothing and does not throw.
- **Postgres storage: `PostgresStorageTests`.**
  - `ScheduleJobsAsync` with several items stores all of them, equivalent to the items scheduled.
  - It upserts onto an existing pending job with the same name. The row takes the new values and its `unresolvable_since` is cleared.
  - Two items with the same job name in one call leave one row, holding the last item.
  - A batch with many items stores every row. Use more items than per-row parameters allow in one statement (at least 5,958 rows: 5,958 rows at 11 parameters each is 65,538), or document why the mechanism has no limit.
  - A connection listening on `jobs_updated` receives exactly one notification for a batch of several items, and none for an empty batch. This is the external proof that the batch costs one write.
- **In-memory storage: `InMemoryJobStorageTests`.** Its existing `ScheduleJobsAsync` coverage stays. Add the duplicate-name case and the empty-batch case.
- **Prior art.** `PerformAsap_StoresJob` and `PerformAt_StoresJob` in `JobSchedulerTests`. `ScheduleJob_BasicJob`, `ScheduleJob_ShouldUpdateNotStartedJob`, and `ScheduleJob_ShouldUpdateJobClass_WhenReschedulingOntoAnExistingPendingRow` in `PostgresStorageTests`. `ScheduleJob_ReopensTheResolutionGraceWindow_WhenReschedulingAJobNameThatCarriesAStamp` and, for listening on `jobs_updated`, `WaitForNextJob_RaisesJobsUpdatedNotification_WhenDeferringAnUnresolvableJob` in `PostgresUnresolvableJobTests`.
- Integration tests need Docker (Testcontainers). Run test projects one at a time.

## Out of Scope

- **Batching single-job callers.** CRON scheduling, retry reschedules, and single-job `PerformAsapAsync` or `PerformAtAsync` each still write one row.
- **A batch overload that takes `JobScheduleOptions`.** Batches keep using fresh options per job.
- **Changing `OnJobScheduledAsync` to run once per batch**, or sharing one DI scope across a batch.
- **The suite's package bump.** michielvandermeer/mvdmio-suite#251 bumps `mvdmio.ASP.Jobs` in the suite once this release is on NuGet, and resolves HEALTH-CHECK-7 in Sentry. That Issue is blocked by this one.
- **The per-job Information log line.** It stays one line per job.

## Further Notes

- Found by triage of michielvandermeer/mvdmio-suite#251, a Sentry "N+1 Query" performance issue (HEALTH-CHECK-7) on `Job: AzureMetricsScheduleJob`.
- Suite callers that gain from this: HealthCheck's `UptimeCheckScheduleJob`, `DnsCheckScheduleJob`, and `AzureMetricsScheduleJob` (every minute), `SslCheckScheduleJob` (daily), and `AzureMetricsBackfillScheduleJob`.

