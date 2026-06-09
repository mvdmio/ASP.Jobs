# Eager, explicit initialization with a fail-fast guard

**Status:** accepted

Postgres storage requires its schema (migrations) to exist before any access. We run **Initialization** (migrations) eagerly: automatically at host start via an `IHostedLifecycleService.StartingAsync` hook (which completes before any `StartAsync`, so it precedes Instance Registration, the runner, and request handling regardless of registration order), and manually via the `InitializeJobsAsync()` service-provider facade for non-ASP.NET hosts or code that schedules before host start. Storage never migrates lazily as a side effect of scheduling; accessing storage before Initialization throws `JobStorageNotInitializedException` with an actionable message. Failure to initialize is fail-fast — it aborts host startup rather than starting a silently-broken process.

## Context

Previously, migrations ran lazily inside `PostgresJobStorage` (`EnsureInitializedAsync`), but the hosted service that performs Instance Registration ran first and touched `job_instances` before the table existed — crashing every clean-database boot. There was no single, ordered initialization step.

## Considered options

- **Eager init + fail-fast guard (chosen).** One canonical trigger (`IJobInitializer`), backend mechanism in `IJobStorage.InitializeAsync`, guard on the public storage surface. Normal consumers do nothing; misuse (scheduling before init) fails loudly and explains itself.
- **Eager init + a single lazy safety-net.** Keep an idempotent lazy call at the storage boundary so too-early scheduling self-heals. Rejected: re-introduces implicit, order-dependent behavior we wanted to eliminate; the clarity of an explicit contract was judged more valuable than self-healing.
- **Keep lazy init, funnel all paths (including registration) through it.** Rejected: leaves initialization implicit and scattered, the root cause of the original bug.

## Consequences

- The behavior is a public contract: scheduling before initialization throws. Moving back to lazy init later would be additive; this direction is the sticky one.
- Cross-process safety (many instances migrating a clean DB at once) is delegated to `DatabaseMigrator`'s advisory lock; this library only guarantees in-process idempotency via a semaphore.
- `InitializeAsync` is idempotent, so the auto (host) and manual (facade) paths compose harmlessly.
