# Job runner passes the job span's name and tags when it starts the span

## Problem Statement

The job runner starts each job's span on the `mvdmio.ASP.Jobs` activity source with no name and no tags. The span's name defaults to the calling method, `PerformJob`, for every job. Only after the span has started does the runner set its display name (`Job: <JobType.Name>`) and its tags (`job.type`, `job.name`, `job.group`, `job.parameters`, `job.cron`, `job.attempt`).

An OpenTelemetry sampler decides whether to record a span when the span starts. It sees only the name, kind, parent, and tags passed at that moment. Today it therefore cannot tell one job from another.

The mvdmio suite needs this. It is moving its Sentry tracing to OTLP ([michielvandermeer/mvdmio-suite#261](https://github.com/michielvandermeer/mvdmio-suite/issues/261)). Its trace budget then runs in a sampler: it records 1 in 100 runs of a high-frequency job, and every run of any other job. The sampler must know the job type when the run's span starts.

Nothing in the runner needs to wait. The runner has claimed the job and resolved its type before it starts the span, so every value the tags carry is already known.

## Solution

The runner passes the span's name and all of its job tags to the activity source when it starts the span. A sampler sees the job type at the moment it decides.

The span that comes out does not change: same name, same tags, same events and status. Only the moment the name and tags are set moves forward.

A span that a sampler drops can still exist as a propagation-only span. It still carries the same name and tags, because a consumer can read the current span while the job runs.

## User Stories

1. As an application developer, I want the job span's name passed when the span starts, so that a sampler can see which job runs.
2. As an application developer, I want `job.type` passed when the span starts, so that a sampler can decide by job type.
3. As an application developer, I want `job.name`, `job.group`, `job.parameters`, `job.cron`, and `job.attempt` passed when the span starts too, so that every tag is available to a sampler, and none is a special case.
4. As an application developer, I want the exported span to keep its `Job: <JobType.Name>` name, so that traces look the same in my tracing backend.
5. As an application developer, I want the exported span to keep the same tags and values, so that my queries and dashboards keep working.
6. As an application developer, I want a span that a sampler drops, but that still exists as a propagation-only span, to carry the job name and `job.type`, so that code reading the current span during the job can still recognise the job.
7. As an application developer, I want the span's events (`Job Started`, `Job Completed`, `Job Canceled`, `Job Retry Scheduled`, `Job Chain Superseded`) and statuses to stay as they are, so that a failed run still shows as failed.
8. As an application developer, I want the span's kind to stay `Internal` and its parent to stay whatever span is current, so that the trace's shape does not change.
9. As an application developer, I want no span at all when no listener is attached to the source, as today, so that an app without tracing pays nothing.
10. As the mvdmio suite maintainer, I want a released package version with this change, so that the suite can move its trace budget into a sampler and unblock #261.

## Implementation Decisions

- **Job runner.** The runner starts the job span with the name `Job: <JobType.Name>` and the kind `Internal`. It passes the six job tags as the start tags, with the values it sets today. The parent stays the ambient span, as today.
- **Name and tags must also hold on a dropped span.** It is not settled whether .NET attaches start tags to a span created for propagation only. If it does not, the runner keeps setting the name and tags after start, which writes the same values again. Either way, the dropped span must carry them (see Testing Decisions).
- **Unchanged.** Events, statuses, exception recording, the `AddException` on failure, retry behaviour (one span per attempt), and the activity source name `mvdmio.ASP.Jobs` stay as they are. So does the work inside the span: the job body, the hooks, chain finalisation, and the next cron occurrence.
- **No public API change.** No interface or options type changes.
- **Behaviour change to record.** The span's operation name changes from `PerformJob` to `Job: <JobType.Name>`. Its display name stays the same, and it is the name exporters send.
- **Release.** A minor version bump: the next minor after the current version (4.8.0 at the time of writing, so 4.9.0). Add a dated entry to the changelog. A push to `main` publishes the package to NuGet.

## Testing Decisions

- A good test drives the job runner through the unit tests' `JobRunnerHarness` (scheduler, runner, and in-memory storage wired together) and listens to the `mvdmio.ASP.Jobs` activity source with a plain `ActivityListener`. It asserts on what the listener sees, not on calls inside the runner.
- **Tags at start.** The listener's sampling callback records the name and tags it is offered. A test runs one job and asserts that the callback saw `Job: <JobType.Name>` and every one of the six job tags, with the expected values.
- **Dropped span.** The listener's sampling callback answers propagation-only. A test runs one job and asserts that the current span inside the job body carries the job name and `job.type`.
- **Exported span unchanged.** The listener records everything. A test runs one successful job and one failing job, and asserts on each span's display name, tags, status, and events.
- Prior art: `JobRunnerServiceTests` and `JobRunnerRetryTests`, which drive jobs through `JobRunnerHarness` with in-memory storage. Use the shared `TestJob`, which already has switches for failing (`ThrowOnExecute`, `FailuresBeforeSuccess`).

## Out of Scope

- Storing a trace context with a scheduled job, so that a fanned-out run becomes a child of its scheduler's span.
- Spans for the claim loop, the cleanup timer, or instance registration. They stay without a span of their own.
- Any change to which tags exist, or to their values.

## Further Notes

- This Issue blocks [michielvandermeer/mvdmio-suite#261](https://github.com/michielvandermeer/mvdmio-suite/issues/261), which moves the suite's Sentry tracing onto `Sentry.OpenTelemetry.Exporter` and its budget into an OpenTelemetry sampler.
- In OpenTelemetry .NET, a sampler's "drop" decision still creates the span as propagation-only (`ActivitySamplingResult.PropagationData`). That is why a dropped run's span still exists for code that reads the current span.

