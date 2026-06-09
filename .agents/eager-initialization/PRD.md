# PRD: Eager, explicit storage initialization

Status: ready-for-agent

> See [ADR 0001](../../docs/adr/0001-eager-explicit-initialization.md) and the [glossary](../../CONTEXT.md) for the terminology used here (Initialization, Initialization Guard, Instance Registration, Worker Instance).

## Problem Statement

When an application using this library starts against a **clean** PostgreSQL database, it crashes on boot. The hosted service that performs **Instance Registration** runs at startup and writes to `job_instances`, but the database migrations have not run yet, so that table does not exist. Migrations are only run lazily, the first time job storage is touched by a scheduling or runner operation — which happens *after* registration. The library has no single, ordered **Initialization** step that is guaranteed to run before anything else accesses the database, and initialization logic is scattered across the storage class as a side effect of normal operations.

## Solution

Introduce one clear **Initialization** step (running migrations) that is guaranteed to complete before any job is scheduled or any worker registers.

- In ASP.NET applications it "just works": Initialization runs automatically at host start, before any other startup work, with no consumer action required.
- In non-ASP.NET hosts (or code that schedules before the host has started), consumers call a single `InitializeJobsAsync()` facade themselves.
- Initialization is never triggered lazily. Accessing storage before Initialization is a usage error that fails fast with a clear, actionable exception telling the caller to initialize first.

The result: normal consumers never think about initialization, the clean-database crash is gone, and the rest of the code no longer carries init concerns.

## User Stories

1. As an application developer, I want the library to create its schema automatically on a clean database at startup, so that my app boots successfully the first time without manual migration steps.
2. As an application developer, I want Initialization to complete before Instance Registration, so that registering a worker never hits a missing `job_instances` table.
3. As an application developer, I want Initialization to complete before the job runner starts polling, so that the runner never queries a non-existent `jobs` table.
4. As an application developer, I want Initialization to complete before my application begins serving requests, so that the first scheduled job in a request handler always finds a ready database.
5. As an application developer running multiple instances, I want every instance to initialize safely at the same time against a shared clean database, so that a scaled-out or rolling deployment starts cleanly.
6. As a developer of a non-ASP.NET host (console, worker, tests), I want a single explicit method to initialize the library, so that I can use Postgres storage without an ASP.NET host lifecycle.
7. As a developer who seeds or schedules jobs before the host has started, I want a documented way to initialize first, so that my early scheduling code works.
8. As a developer who accidentally schedules a job before Initialization, I want a clear, actionable exception, so that I immediately understand I must call `InitializeJobsAsync()` rather than debugging a cryptic SQL error.
9. As an operator, I want the application to fail fast at startup if Initialization fails (bad connection string, unreachable database, failed migration), so that I never run a process that is up but silently broken.
10. As a maintainer, I want migrations to run exactly once per process regardless of how many times initialization is requested, so that the auto (host) and manual (facade) paths compose harmlessly.
11. As a maintainer, I want a single component that owns the Initialization trigger, so that there is one obvious place that initialization flows through.
12. As a maintainer, I want the migration mechanism to live in the storage backend, so that backend-specific concerns stay encapsulated and InMemory storage can opt out cleanly.
13. As a maintainer, I want the storage class to stop owning scattered lazy-init logic, so that storage methods are simpler and only concerned with their actual operation.
14. As a maintainer, I want the service that performs Instance Registration to be named for what it does, so that the codebase no longer conflates registration with initialization.
15. As a developer using InMemory storage, I want Initialization to be a harmless no-op, so that the in-memory path is unaffected and the public API is uniform across backends.
16. As a developer, I want `InitializeJobsAsync()` to be discoverable alongside `AddJobs()`, so that the registration and initialization APIs are symmetric.
17. As a maintainer, I want correctness to not depend on hosted-service registration order, so that consumers wiring services in any order still get correct initialization.
18. As a contributor, I want an integration test proving a clean-database host startup succeeds, so that the original bug cannot silently regress.
19. As a contributor, I want a test proving the Initialization Guard throws before init and passes after, so that the fail-fast contract is locked in.

## Implementation Decisions

### Concepts

Per the glossary: **Initialization** = running migrations to ready the schema (idempotent, never lazy). **Instance Registration** = a Worker Instance announcing itself in `job_instances`, after Initialization. **Initialization Guard** = the fail-fast check on storage access before Initialization.

### Modules

- **`IJobInitializer` / `JobInitializer`** (new, public). The single canonical Initialization trigger. Host- and backend-agnostic. `InitializeAsync(CancellationToken)` delegates to `IJobStorage.InitializeAsync`. This is the stable public handle, since `IJobStorage` is internal. Deep module: minimal interface, encapsulates "trigger initialization once."
- **`IJobStorage.InitializeAsync(CancellationToken)`** (new method on the internal storage interface). The backend mechanism.
  - `PostgresJobStorage.InitializeAsync`: runs the database migrations and sets an in-process `_isInitialized` flag, guarded for idempotency by the existing semaphore. The current `RunDbMigrations` logic moves here; `EnsureInitializedAsync` is removed.
  - `InMemoryJobStorage.InitializeAsync`: no-op; the backend is considered always-initialized.
- **Initialization Guard** (in `PostgresJobStorage`). Each public DB-touching method (`ScheduleJobAsync`/`ScheduleJobsAsync`, `WaitForNextJobAsync`, `FinalizeJobAsync`, `GetScheduledJobsAsync`, `GetInProgressJobsAsync`, `DeleteJobByIdAsync`) checks the in-process flag first and throws **`JobStorageNotInitializedException`** if not set. `InitializeAsync` is exempt. The exception message is actionable, e.g. directing the caller to run `InitializeJobsAsync()` before scheduling outside the normal application lifecycle.
- **`JobInitializationHostedService`** (new, implements `IHostedLifecycleService`). Calls `IJobInitializer.InitializeAsync` in `StartingAsync`. Registered for all backends (no-op for InMemory). Does not catch — failures propagate and abort host startup (fail-fast).
- **`PostgresInstanceRegistrationService`** (renamed from `PostgresInitializationService`). Performs Instance Registration in `StartAsync` and release/unregister in `StopAsync`, unchanged in behavior. No longer carries any initialization concern.
- **`InitializeJobsAsync()`** (new, `IServiceProvider` extension, sibling of `AddJobs`). Resolves `IJobInitializer` and calls `InitializeAsync`. No scope needed (singletons).
- **`JobConfigurationBuilder.SetupServices`** (modified). Registers `IJobInitializer` and `JobInitializationHostedService` for all backends; registers the renamed `PostgresInstanceRegistrationService` and `PostgresCleanupService` for Postgres.

### Ordering and guarantees

- Initialization runs in `StartingAsync`, which the host invokes for all hosted services before any `StartAsync`. This guarantees Initialization precedes Instance Registration (in `StartAsync`), the runner's first storage access (in its `ExecuteAsync`), and request handling — **independent of registration order**.
- The Initialization Guard's flag is **per-process** ("this process ran initialization"), not "the shared DB is migrated." Even if another instance already migrated the shared DB, this process must still go through its own (then idempotent) `InitializeAsync`.
- The guard is enforced only on the public `IJobStorage` surface. `PostgresJobInstanceRepository` methods are internal and only called by lifecycle-ordered hosted services (after Initialization), so they are protected by ordering, not a guard.
- `PerformNowAsync` executes inline and never touches storage, so it neither needs nor triggers Initialization.

### Failure and idempotency

- Fail-fast: a failed Initialization propagates out of `StartingAsync` and aborts host startup. The manual facade likewise propagates.
- `InitializeAsync` is idempotent; calling it via the host hook and the manual facade (in any order, any number of times) runs migrations once.

### Cross-process safety (dependency, out of scope here)

Concurrent migration by multiple instances against a clean DB is made safe by an advisory lock inside `DatabaseMigrator` (in the `mvdmio.Database.PgSQL` package), handled in a separate effort. This library assumes that lock exists and only guarantees in-process idempotency via its semaphore.

## Testing Decisions

Good tests assert **external, observable behavior** through public/consumer-facing entry points (host startup, scheduling, the thrown exception type) — not private flags, call counts, or migration internals. Integration tests use the existing PostgreSQL Testcontainers setup (Docker required).

Tests to write:

1. **Clean-database host startup (proves the bug fixed).** Build a host configured with Postgres storage pointed at an empty database, start the host, and assert: startup completes without throwing, the schema/tables exist (migrations ran), and the Worker Instance is registered in `job_instances`. This is the regression guard for the original crash.
2. **Initialization Guard behavior.** Against a storage instance that has not been initialized, a public scheduling/query operation throws `JobStorageNotInitializedException`; after Initialization, the same operation succeeds.

Prior art: existing integration tests under `test/mvdmio.ASP.Jobs.Tests.Integration` and the `PostgresFixture` Testcontainers setup (which migrates a shared container up front). The shared fixture is left as-is; new init/guard tests stand up their own clean-database scenarios rather than reusing the pre-migrated shared container.

Explicitly not covered by tests this round (acceptable per scope decision): InitializeAsync idempotency assertions and the InMemory no-op path.

## Out of Scope

- The `DatabaseMigrator` advisory lock for safe concurrent multi-instance migration — handled in a separate session in the `mvdmio.Database.PgSQL` package. This PRD assumes it exists.
- Any change to InMemory storage behavior beyond adding a no-op `InitializeAsync`.
- Changes to the public job-scheduling API (`IJobScheduler`) surface, job lifecycle, or runner behavior.
- Reworking the shared integration test fixture (`PostgresFixture`).
- Retry/back-off policies for failed Initialization (fail-fast only).
- Health-check integration for initialization status.

## Further Notes

- This is a published NuGet library; the only new **public** surface is `IJobInitializer`, the `InitializeJobsAsync()` extension, and `JobStorageNotInitializedException`. `IJobStorage.InitializeAsync` is internal. The rename of `PostgresInitializationService` is internal and not a breaking change.
- The behavioral contract "scheduling before Initialization throws" is a deliberate, documented choice (ADR 0001). Moving back to lazy initialization later would be additive; this direction is the sticky one.
- Suggested verification: `dotnet build` then `dotnet test` (Docker running), run sequentially.
