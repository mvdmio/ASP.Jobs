# Job Culture Capture, Reapplication & Propagation

**Status:** ready-for-agent

_Domain terms used below are defined in `CONTEXT.md` (Culture cluster). This feature is governed by `docs/adr/0002-job-culture-capture-and-propagation.md` — read it before implementing._

## Problem Statement

I build an ASP.NET Core application where work is triggered from request threads that run under a specific user culture — Dutch number and date formatting, German UI translations, and so on. When I hand that work off to a background job, the job runs later on a thread-pool thread whose culture is arbitrary (usually invariant or the OS default). As a result, a job that formats a currency amount, renders a date, generates a localized document, or looks up a translated resource produces output in the *wrong* locale — not the one the user was in when the work was started. Today there is no way to make a job run in the same culture it was scheduled from, and no way to explicitly pin a job to a culture. Jobs that schedule further jobs make it worse: the locale is lost at every hop.

## Solution

Every scheduled job now carries a **Captured Culture** — both a formatting culture (`CurrentCulture`) and a UI culture (`CurrentUICulture`) — recorded at the moment it is scheduled and reapplied to the executing thread while it runs.

- If I don't say otherwise, a job scheduled with `PerformAsapAsync` or `PerformAtAsync` runs in **the culture of the thread that scheduled it** (both values captured independently). A `PerformCronAsync` job runs in the **invariant culture** by default.
- I can pass an explicit `CultureInfo` to any of those three methods to pin the job to exactly that culture (used for both the formatting and UI culture).
- Jobs that schedule further jobs **propagate** their Captured Culture down automatically, and recurring CRON occurrences keep running in the culture the CRON was registered with.
- `PerformNowAsync` is unchanged: it runs in-line on my thread, so it already runs in my current culture.

The result: a job always runs in the culture it was started in — the user's culture when it was started in a user context — with an explicit override available when I need one.

## User Stories

1. As an application developer, I want a job scheduled from a user-localized request thread to execute under that same user culture, so that money, dates, and numbers it formats match what the user expects.
2. As an application developer, I want a job to resolve translated resources under the UI culture that was active when it was scheduled, so that generated emails and documents are in the user's language.
3. As an application developer, I want `CurrentCulture` and `CurrentUICulture` captured **independently**, so that a request where formatting culture and UI culture differ (e.g. Swiss formatting with German strings) is preserved faithfully.
4. As an application developer, I want to pass an explicit `CultureInfo` when scheduling an ASAP job, so that I can pin a job to a specific locale regardless of the scheduling thread's culture.
5. As an application developer, I want to pass an explicit `CultureInfo` when scheduling an "at time" job, so that a deferred job runs in a chosen locale.
6. As an application developer, I want to pass an explicit `CultureInfo` when scheduling a CRON job, so that a recurring job runs in a chosen locale on every occurrence.
7. As an application developer, I want a single `CultureInfo` argument to set both the formatting and UI culture, so that the common case is one simple parameter.
8. As an application developer, I want CRON jobs to default to the **invariant** culture, so that a recurring job registered at application startup behaves deterministically and does not accidentally inherit the startup thread's OS culture.
9. As an application developer, I want ASAP and "at time" jobs to default to the scheduling thread's culture, so that the common web-request case works with no extra code.
10. As an application developer, I want a job that itself schedules further jobs to pass its culture down automatically, so that a chain of jobs all runs in the originating user's culture without me threading a culture argument through every call.
11. As an application developer, I want a recurring CRON job to keep running in its registered culture on every future occurrence, so that recurrence does not silently reset the locale.
12. As an application developer, I want `PerformNowAsync` to keep running in my current thread's culture, so that immediate in-line execution behaves exactly as before.
13. As an application developer, I want the Captured Culture to survive a restart when using PostgreSQL storage, so that a job scheduled before a deploy still runs in the right culture afterwards.
14. As an application developer, I want the Captured Culture to behave identically whether I use in-memory or PostgreSQL storage, so that switching backends does not change job behavior.
15. As an application developer, I want to schedule a batch of jobs with one explicit culture, so that every job in the batch runs under that locale.
16. As an application developer, I want to combine an explicit culture with custom scheduling options (job name / group), so that I can control naming/grouping and locale together.
17. As an application developer, I want the executing thread's culture restored after each job, so that one job's culture never leaks into the next job that reuses the thread.
18. As an application developer upgrading the package, I want the new culture parameters added as new overloads, so that my existing scheduling call sites keep compiling without edits.
19. As an application developer, I want an unresolvable stored culture at execution to fail the job through the normal failure path (logged, `OnJobFailedAsync` invoked), so that a misconfiguration surfaces loudly instead of silently producing wrong-locale output.
20. As an application developer whose service scheduled jobs before this feature existed, I want those pre-existing jobs (with no Captured Culture) to keep executing in the runner thread's ambient culture, so that upgrading does not break in-flight work.
21. As a maintainer, I want the Captured Culture to stay internal (not on `ScheduledJobInfo`), so that the public query surface stays minimal and I can add it later if needed.
22. As an end user, I want background-generated output (invoices, notifications, reports) to be in my locale, so that the numbers, dates, and language match the rest of my experience.

## Implementation Decisions

**Representation of the Captured Culture**
- A Captured Culture is stored as **culture names** (strings), never as a live `CultureInfo` object — this is the only representation that round-trips through PostgreSQL serialization, and it makes the two storage backends behave identically.
- Two values are captured and stored: one for the formatting culture and one for the UI culture.
- The invariant culture is represented by the empty-string name. A **missing** Captured Culture (null) means "nothing was captured" (e.g. a job persisted before this feature) and is distinct from a captured invariant culture.
- Customized `CultureInfo` instances (hand-tweaked `NumberFormat`/`DateTimeFormat`) are **not** preserved — only the base culture name. This is an accepted, documented limitation.

**Scheduler public API (`IJobScheduler` and its implementation)**
- Add **new overloads** (additive, non-breaking) that take a **required** `CultureInfo`. A required parameter is used deliberately so the new overloads never collide with the existing ones on calls that pass `CancellationToken` positionally.
- **Full parity**: culture-taking variants of every existing shape — single-parameter, batch (`IEnumerable`), and options-carrying forms for `PerformAsapAsync`/`PerformAtAsync`; and both the `string` and `CronExpression` forms for `PerformCronAsync`. Eight new overloads in total.
- The explicit `CultureInfo` is applied to **both** the formatting and UI culture fields.
- `CancellationToken` remains the last parameter in every new overload. The culture argument is not added to `JobScheduleOptions`, and `PerformNowAsync` gets **no** culture overload.

**Culture Capture (defaults when no explicit culture is supplied)**
- `PerformAsapAsync` / `PerformAtAsync`: capture the scheduling thread's `CurrentCulture` and `CurrentUICulture` **independently**, read at the top of the method (before the first `await`).
- `PerformCronAsync`: default to the **invariant** culture for both values.

**Internal carrier & storage**
- The internal job-store item gains two nullable culture-name fields (formatting + UI). The in-memory backend carries them on the object with no serialization work.
- PostgreSQL: add two **nullable** text columns for the two culture names, via a new timestamped migration following the existing migration convention (ordered after the current latest migration). The job-data mapping (both directions) carries the two fields; a null column means no Captured Culture.
- The Captured Culture is **not** exposed on `ScheduledJobInfo`.

**Culture Reapplication (execution, in the job runner)**
- In the runner's per-job execution path: save the executing thread's current formatting and UI culture, resolve and set the job's Captured Culture, run the job, and **restore** the saved culture in a `finally`.
- Guard: only touch the thread culture when a Captured Culture is present (non-null). A null Captured Culture leaves the runner thread's ambient culture untouched (backward-compatible path for pre-feature jobs).
- Reapplication wraps the execution-time lifecycle: `ExecuteAsync`, `OnJobExecutedAsync`, and `OnJobFailedAsync`. It does **not** cover `OnJobScheduledAsync` (that already ran at schedule time on the caller's thread).
- Resolving the culture name happens **inside** the execution `try` block. There is **no** `catch` for `CultureNotFoundException`: an unresolvable culture (e.g. globalization-invariant mode, or a host missing that ICU data) rides the normal job-failure path — logged and routed to `OnJobFailedAsync`. Placing the resolution inside the `try` is required so the exception is observed rather than lost on the runner's fire-and-forget task.

**Culture Propagation**
- Child jobs require **no** new mechanism: while a job runs, its Captured Culture is the reapplied ambient culture, so any job it schedules (via the default overloads) captures it automatically through Culture Capture.
- CRON recurrence: when the runner schedules the next occurrence, it **copies the two culture-name fields forward** from the finishing job onto the next occurrence (the recurrence path builds the next job directly and bypasses the scheduler's capture logic, so the copy is explicit).

## Testing Decisions

**What makes a good test here:** assert **externally observable behavior** — the culture a job actually runs under, and whether culture names survive storage — never internal wiring. The primary technique is a test job that records the `CurrentCulture`/`CurrentUICulture` it observes *during execution* into its live parameters object, which the test inspects after the runner drains. To prove reapplication (not mere ambient inheritance), tests set the scheduling-thread culture, schedule, then **change the thread culture** before running the runner.

**Seam 1 — in-memory runner end-to-end (primary).** Use the existing runner harness that wires the scheduler + runner + in-memory storage and drains to completion (prior art: the existing runner-service tests). One seam covers:
- Ambient default: ASAP/At job scheduled under culture A, thread switched to B, job observed to run under A (both formatting and UI culture, including a case where the two differ).
- Explicit override: job scheduled with an explicit culture runs under it regardless of the scheduling thread.
- Child propagation: a job that schedules a child (via injected scheduler) with a default overload — the child runs under the parent's culture.
- Restore/isolation: after a job with a Captured Culture runs, the next job with no Captured Culture is unaffected.
- Unresolvable culture reaches the failure path (`OnJobFailedAsync`), not a swallowed exception.

**Seam 2 — job-data round-trip (unit).** Prior art: the existing job-data tests. Assert both culture names survive the to/from mapping, covering a specific culture, the invariant culture (empty-string name), and null.

**Seam 3 — PostgreSQL storage (integration, Docker required).** Prior art: the existing Postgres parameter/storage tests using the Postgres storage harness and shared container fixture. Schedule with a culture, read the job back through the real database, assert both columns persist. This is the only place the new columns + migration are exercised against a real database.

**CRON specifics** are verified at the **storage seam**, not through a live timed runner (the runner tests carry a standing TODO that CRON execution needs finer time control that does not yet exist): assert that `PerformCronAsync` stores the invariant culture by default and an explicit culture when supplied, and — where reachable at the storage level — that a recurrence carries the culture forward. Building new timing machinery for the runner is out of scope.

## Out of Scope

- Preserving customized `CultureInfo` instances (only the culture name is kept).
- Exposing the Captured Culture on `ScheduledJobInfo` or any public query/admin surface.
- Any culture handling for `PerformNowAsync` beyond its existing behavior (runs in the caller's ambient culture).
- Catching an unresolvable culture and falling back to invariant/ambient — the failure surfaces via `OnJobFailedAsync`.
- Adding culture to `JobScheduleOptions`.
- Building a new time-controllable CRON test harness.
- Backfilling culture onto jobs already persisted before this feature ships (they simply have no Captured Culture and run ambient).

## Further Notes

- **Behavior changes to call out in the changelog and release notes:** ASAP/At jobs now execute under the scheduling thread's culture instead of the runner thread's ambient culture; CRON jobs now execute under the invariant culture by default instead of the runner thread's ambient culture. Both are deterministic improvements but are observable for callers who previously relied on `DefaultThreadCurrentCulture` leaking into jobs.
- Applications running with globalization-invariant mode will throw when executing a job that captured a non-empty culture. This is intended — fix the configuration or schedule with the invariant culture.
- Because this touches the published `IJobScheduler` contract (additive overloads) and the PostgreSQL schema (new migration), ship it with an appropriate version bump.
- Prefer `CultureInfo.GetCultureInfo(name)` (cached, read-only) when rebuilding a culture at execution time.
