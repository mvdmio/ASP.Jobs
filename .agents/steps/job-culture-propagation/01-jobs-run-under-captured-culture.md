# 01 — Jobs run under the culture they were scheduled in

Status: pending
Blocked by: none

## What to build

A scheduled job carries a Captured Culture — formatting (`CurrentCulture`) and UI (`CurrentUICulture`) as culture *names* — recorded at schedule time and reapplied on the executing thread for the whole execution-time lifecycle.

- `PerformAsapAsync` / `PerformAtAsync` without an explicit culture capture the scheduling thread's two values independently, read at the top of the method before the first `await` (a batch captures once and applies that pair to every item).
- `PerformCronAsync` without an explicit culture captures the invariant culture for both values (empty-string name), not the startup thread.
- Each of those three methods gains additive, required-`CultureInfo` overloads at full parity with the existing shapes: single, batch (`IEnumerable`), and options-carrying for ASAP/At; `string` and `CronExpression` for CRON — eight new overloads. `CancellationToken` stays last. The explicit culture's name is written to **both** fields. A null `CultureInfo` throws `ArgumentNullException`. Culture is not added to `JobScheduleOptions`. `PerformNowAsync` gets no culture overload and keeps running in the caller's ambient culture.
- The internal job-store item gains two nullable culture-name fields. Null means no Captured Culture (pre-feature jobs); empty string means captured invariant. A Captured Culture is present only when **both** names are non-null; a half-null pair is treated as absent.
- In the runner: save the thread's two cultures, resolve and apply the job's Captured Culture with `CultureInfo.GetCultureInfo` (both names resolved before either is assigned), run the job, restore in a `finally`. Do not touch the thread when no Captured Culture is present. Resolution lives inside the execution `try` so `CultureNotFoundException` rides the normal failure path (logged, `OnJobFailedAsync`). There is no catch-and-fallback.
- Reapplication covers `ExecuteAsync`, `OnJobExecutedAsync`, and `OnJobFailedAsync`. It also covers `OnJobRetryAsync` — an execution-time runner-side hook added after this spec that otherwise would observe the restored ambient culture. It does not cover `OnJobScheduledAsync` (already ran on the caller's thread) or storage finalization / next-occurrence scheduling.
- Child jobs scheduled with a default overload inherit automatically: while the parent runs, its Captured Culture is ambient. CRON recurrence copies the two name fields forward onto the next occurrence (that path bypasses the scheduler). In-memory retries copy the two names onto the retry item so an Execution Chain keeps its Captured Culture.
- Captured Culture is not exposed on `ScheduledJobInfo`.

## Footprint

Projects: mvdmio.ASP.Jobs, mvdmio.ASP.Jobs.Tests.Unit

- `src/mvdmio.ASP.Jobs/IJobScheduler.cs` — culture-taking `PerformAsapAsync`, `PerformAtAsync`, `PerformCronAsync` overloads
- `src/mvdmio.ASP.Jobs/Internals/JobScheduler.cs` — `JobScheduler`, `ScheduleAsapAsync`, `ScheduleAtAsync`, `ScheduleCronAsync`
- `src/mvdmio.ASP.Jobs/Internals/Storage/Data/JobStoreItem.cs` — `CultureName`, `UICultureName`
- `src/mvdmio.ASP.Jobs/Internals/JobRunnerService.cs` — `PerformJob`, `ApplyCapturedCulture`, `ScheduleNextOccurrence`
- `src/mvdmio.ASP.Jobs/Internals/Storage/InMemoryJobStorage.cs` — `TryScheduleRetryAsync` copies the two names onto the retry item
- `src/mvdmio.ASP.Jobs/ScheduledJobInfo.cs` — no culture members
- `src/mvdmio.ASP.Jobs/JobScheduleOptions.cs` — no culture members
- `test/mvdmio.ASP.Jobs.Tests.Unit/JobCulturePropagationTests.cs` — in-memory runner seam (and CRON at the storage seam)
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/CultureRecordingJob.cs` — records observed cultures during execution
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/CultureChildSchedulingJob.cs` — parent that schedules a child via a default overload
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/JobRunnerHarness.cs` — scheduler + runner + in-memory storage; registers culture jobs and `IJobScheduler`

## Acceptance criteria

- [ ] An ASAP job scheduled under culture A / UI culture B, with the thread then switched to something else before the runner drains, executes under A and B.
- [ ] An "at time" job does the same (ambient default and explicit override).
- [ ] An explicit `CultureInfo` on ASAP, At, or CRON pins both fields to that culture's name regardless of the scheduling thread.
- [ ] A batch scheduled with one explicit culture stores and runs every item under that culture; a default batch captures the thread once for the whole batch.
- [ ] An options-carrying overload accepts an explicit culture together with job name / group.
- [ ] `PerformCronAsync` without a culture stores empty-string names (invariant); with a culture stores that name on both fields.
- [ ] After a CRON job finishes, the next occurrence carries the same two culture-name fields (asserted at the storage seam; no new CRON timing harness).
- [ ] A parent job that schedules a child with a default overload has that child execute under the parent's Captured Culture.
- [ ] After a job with a Captured Culture runs, a following job with no Captured Culture is unaffected (thread restored; null means ambient).
- [ ] An unresolvable stored culture name fails the job through `OnJobFailedAsync` without running `ExecuteAsync`.
- [ ] `OnJobExecutedAsync` / `OnJobFailedAsync` (and `OnJobRetryAsync` when a retry is scheduled) observe the Captured Culture, not the restored ambient culture.
- [ ] `PerformNowAsync` has no culture overload; existing scheduling call sites still compile.
- [ ] `ScheduledJobInfo` does not expose Captured Culture.
