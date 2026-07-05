# Jobs capture, reapply, and propagate a culture

**Status:** accepted

Scheduled jobs run later on background thread-pool threads whose culture is arbitrary, so a job started from a user-localized context (e.g. an ASP.NET request) would otherwise format dates/numbers and resolve resources under the wrong locale. We give each scheduled job a **Captured Culture** (both `CurrentCulture` and `CurrentUICulture`), taken at schedule time, and **reapply** it on the executing thread for the duration of the run. `PerformAsapAsync`/`PerformAtAsync`/`PerformCronAsync` each gain an optional explicit `CultureInfo`; without it, Asap/At snapshot the two ambient values of the scheduling thread while CRON defaults to the invariant culture. `PerformNowAsync` is unchanged — it runs in-line in the caller's culture already. Child-job **Culture Propagation** falls out for free: a running job's culture is the reapplied ambient one, so any job it schedules captures it automatically; CRON occurrences copy the Captured Culture forward.

## Context

Jobs are serialized to storage (in-memory or Postgres) and executed asynchronously by `JobRunnerService` on shared thread-pool threads. There was no relationship between the culture at scheduling time and the culture at execution time.

## Considered options

- **Represent the Captured Culture by culture *name* only (chosen).** The Postgres path must serialize, and only the name reliably round-trips. Both storage backends therefore behave identically. Trade-off: a *customized* `CultureInfo` (hand-tweaked `NumberFormat`/`DateTimeFormat`) collapses to its base culture — explicitly unsupported. Rejected keeping the live object for the in-memory path: same job would behave differently per backend, a footgun for a published library.
- **Additive overloads with a required `CultureInfo` (chosen).** `ct` is passed positionally at nearly every call site, so inserting an optional culture parameter before it would break source compat. Full parity: culture-taking variants of every existing shape (single, batch, options; string/`CronExpression` for CRON) — 8 new overloads. Rejected the single-signature breaking change despite this repo's "no shims" leaning, because callers should not have to recompile-and-edit.
- **Explicit culture as a method parameter, not on `JobScheduleOptions` (chosen).** Keeps `JobScheduleOptions` and its round-trip unchanged, and works uniformly for CRON (which has no options overload).
- **On a failed culture resolution, let it throw (chosen).** If a stored culture name can't be resolved at execution (e.g. `InvariantGlobalization=true`, or a host missing that ICU data), the exception rides the normal job-failure path — logged and routed to `OnJobFailedAsync`. Rejected catching + falling back to invariant/ambient: a misconfiguration should surface, not silently degrade. Resolution happens *inside* the execution `try` specifically so the throw is observed rather than lost on the runner's fire-and-forget task.

## Consequences

- **Behavior change:** Asap/At jobs now execute under the scheduling thread's culture rather than the runner thread's ambient culture; CRON jobs now execute under the invariant culture by default rather than the runner thread's ambient. Deterministic, but observable for callers who relied on `DefaultThreadCurrentCulture` leaking into jobs.
- A new Postgres migration adds two nullable columns (`culture`, `ui_culture`); a null value means no Captured Culture (pre-feature rows), which is distinct from a captured invariant culture (empty-string name).
- Customized `CultureInfo` instances are not preserved — only the culture name.
- Apps running with globalization-invariant mode will throw when executing jobs that captured a non-empty culture; that is intended (fix the configuration or schedule with the invariant culture).
- The Captured Culture is internal — it is not exposed on `ScheduledJobInfo`.
