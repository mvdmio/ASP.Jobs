# 01 — Job runner passes the span's name and tags at start

Status: done
Depends on: none

## What to build

When the job runner starts a Job's span on the `mvdmio.ASP.Jobs` activity source, it passes the span's name, `Job: <JobType.Name>`, and the kind `Internal`. It also passes the job tags as start tags. A sampler, or any `ActivityListener` sampling callback, therefore sees which Job runs at the moment it decides. The parent stays the ambient span, as today.

The start tags carry exactly the tags and values the runner sets today:

- `job.type`: the job type's assembly-qualified name
- `job.name`: the job name from the schedule options
- `job.group`: the group from the schedule options
- `job.parameters`: the parameters object
- `job.cron`: the CRON expression's string form
- `job.attempt`: the number of retries this Execution Chain has used so far

A tag whose value is null is left out of the start tags. Today `SetTag` with a null value adds nothing, so a job with no group and no CRON expression has no `job.group` and no `job.cron` tag. That stays true both at start and on the exported span. The Spec's Out of Scope forbids any change to which tags exist.

The span that comes out does not change. It keeps the same display name, the same tags and values, the same events (`Job Started`, `Job Completed`, `Job Canceled`, `Job Retry Scheduled`, `Job Chain Superseded`), the same statuses, and the same `AddException` on failure. Each retry attempt still gets its own span. Only the span's operation name changes, from `PerformJob` to `Job: <JobType.Name>`. When no listener is attached, there is still no span, and jobs run as before.

A span that a sampler drops can still exist as a propagation-only span (`ActivitySamplingResult.PropagationData`). That span must carry the job name and the tags too, because code can read the current span while the job runs. The spec does not settle whether .NET attaches start tags and the start name to a propagation-only span. The dropped-span test answers that question. If .NET does not attach them, the runner keeps setting the display name and tags after start as well, which writes the same values a second time.

Release: raise the package version to the next minor. The tag `v4.9.0` is already released and the project file reads `4.9.0`, so the next minor is `4.10.0`. The Spec gave `4.9.0` only as an example from when it was written, and its rule is "the next minor after the current version". The Changelog entry is not part of this step: `/document-changes` writes it after review. Do not change the public API.

Tests live in one new unit test class. They drive jobs through `JobRunnerHarness` and listen to the `mvdmio.ASP.Jobs` source with a plain `ActivityListener`, which each test disposes when it ends. Rules for the tests:

- **Isolation.** An `ActivityListener` hears the whole process, and other test classes run their jobs on the same source in parallel. Each test therefore matches only its own spans: a span counts as the test's own when its `job.parameters` tag holds that test's own `TestJob.Parameters` instance. In-memory storage hands the same instance to the job. No other test class attaches a listener, and the tests in one class run one at a time, so one test's sampling answer cannot change another test's span.
- **Tags at start.** The sampling callback records the name and tags it is offered. One run uses an ASAP job with an explicit job name and group. The callback must see `Job: TestJob` and `job.type`, `job.name`, `job.group`, `job.parameters`, and `job.attempt` (`0`), each with its expected value, and no `job.cron`. A second run uses a CRON job, and the callback must see `job.cron` with the expression's string form. CRON jobs take no schedule options, so no single run can carry all six tags. Drive the CRON run directly: start the runner, wait until the job has run, then stop the runner. A CRON chain always leaves its next occurrence scheduled, so a drain never ends. Prior art: `JobRunnerRetryTests.CronJob_WaitsForChainToEnd_BeforeSchedulingNextOccurrence`.
- **Dropped span.** The sampling callback answers propagation-only. `TestJob` records what the current span carries inside `ExecuteAsync`. The test asserts that this span was not recorded, that it carries the display name `Job: TestJob`, and that its `job.type` is `TestJob`'s assembly-qualified name. The recording is kept out of JSON, the way `OnJobScheduledCallback` is.
- **Exported span unchanged.** The listener records everything and collects spans when they stop. For one successful job, the test asserts the display name and operation name (`Job: TestJob`), the kind `Internal`, the job tags, status `Ok`, and the events `Job Started` and `Job Completed`. For one failing job (`ThrowOnExecute`, with no retry behaviour that matches), it asserts the display name, the tags, status `Error`, and the events `Job Started` and the exception event.

## Footprint

Projects: `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj`, `test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj`, `test/mvdmio.ASP.Jobs.Tests.Integration/mvdmio.ASP.Jobs.Tests.Integration.csproj` (needs Docker)

- `src/mvdmio.ASP.Jobs/Internals/JobRunnerService.cs` — `PerformJob` (the `_openTelemetry.StartActivity()` call and the `DisplayName` and `SetTag` lines that follow it), `_openTelemetry`
- `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj` — `<Version>`
- `test/mvdmio.ASP.Jobs.Tests.Unit/JobRunnerTracingTests.cs` — new test class for the tag-at-start, dropped-span, and exported-span tests
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/TestJob.cs` — `ExecuteAsync` and `Parameters`, which gain a record of the current span seen inside the job body
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/JobRunnerHarness.cs` — `Scheduler`, `Runner`, `RunAndDrainAsync`, `Storage` (used; change only if a test needs it)
- `test/mvdmio.ASP.Jobs.Tests.Unit/JobRunnerRetryTests.cs` — prior art for the CRON drive and for retry policies set through `RetryPolicyProvider`

## Acceptance criteria

- [ ] The runner starts the job span with the name `Job: <JobType.Name>`, the kind `Internal`, the ambient parent, and the non-null job tags as start tags.
- [ ] A test's sampling callback sees `Job: TestJob` and `job.type`, `job.name`, `job.group`, `job.parameters`, and `job.attempt` with their expected values for a named, grouped ASAP job, with no `job.cron`.
- [ ] A test's sampling callback sees `job.cron` with the expression's string form for a CRON job.
- [ ] A test whose listener answers propagation-only shows that the current span inside the job body is not recorded, has the display name `Job: TestJob`, and has `job.type` set to `TestJob`'s assembly-qualified name.
- [ ] A test with a record-everything listener shows that a successful job's span has the display name and operation name `Job: TestJob`, the kind `Internal`, the job tags, status `Ok`, and the events `Job Started` then `Job Completed`.
- [ ] The same test setup shows that a failing job's span has the display name `Job: TestJob`, the job tags, status `Error`, and the events `Job Started` and the exception event.
- [ ] A null-valued tag is absent on the exported span, as it is today. A job with no group and no CRON expression has no `job.group` and no `job.cron` tag.
- [ ] The tracing tests match only their own spans, by their own `job.parameters` instance, and dispose their listeners.
- [ ] Events, statuses, exception recording, one span per retry attempt, and the source name `mvdmio.ASP.Jobs` are unchanged. No public API changes.
- [ ] `<Version>` in `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj` reads `4.10.0`.
- [ ] `dotnet build`, then `dotnet test` (run one after the other), pass for the whole solution, with the integration tests included.

## Outcome

Safety fact: the runner passes `Job: <JobType.Name>`, kind `Internal`, and the non-null job tags to `StartActivity`, so a sampling callback sees the job type when it decides, and both a recorded and a propagation-only span carry the same name and tags; if false, a sampler cannot tell jobs apart, or code reading `Activity.Current` in a dropped run loses the job name (rung 3)
Proof: `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj --filter FullyQualifiedName~JobRunnerTracingTests` exit 0 — Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5 (with `JobRunnerService.cs` from d4e768d, 4 of the 5 fail)
Merge risk: easy — reverting the commits restores the old span start, the live in-memory job views, and version 4.9.0; nothing is published until a push to `main`; affects applications whose samplers or dashboards key on the operation name `PerformJob`

- .NET does attach the start tags and the start name to a propagation-only span: the dropped-span test passes without setting them again after start, so the runner no longer calls `SetTag` or sets `DisplayName` after start.
- A small helper, `GetJobSpanTags` in `JobRunnerService`, leaves null-valued tags out of the start tags.
- `TestJob.Parameters` gains `ExecuteActivity` (`[JsonIgnore]`), set to `Activity.Current` inside `ExecuteAsync`.
- `dotnet build` and `dotnet test` pass for the whole solution: 101 unit tests, 108 integration tests.
- The runner builds the span name and tags only when the activity source `HasListeners()`, so a run with no listener allocates nothing for tracing.
- `JobRunnerService.ActivitySourceName` (`internal const`, `mvdmio.ASP.Jobs`) is the one source of the activity source name; `TracerProviderBuilder.AddJobs()` and `JobRunnerTracingTests` read it.
- `JobRunnerHarness.WaitUntilAsync` (static) replaces the private copies in `JobRunnerRetryTests` and `JobRunnerTracingTests`.
- `InMemoryJobStorage.ScheduledJobs` and `InProgressJobs` (and so `GetScheduledJobsAsync` and `GetInProgressJobsAsync`) now return a copy taken under the queue lock. Before, they returned the live dictionary view, and `JobCulturePropagationTests.PerformCron_CarriesCultureForwardToNextOccurrence` failed about 2 runs in 3 with "Collection was modified" once the new tracing tests ran beside it.
