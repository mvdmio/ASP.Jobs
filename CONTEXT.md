# mvdmio.ASP.Jobs

A job scheduling library for ASP.NET Core. This glossary fixes the language used to talk about how the library prepares its storage and runs background jobs, so that overloaded words (especially "initialization") have one agreed meaning.

## Language

**Initialization**:
Making a storage backend ready for use — for Postgres, running the database migrations so the schema exists at the latest version. Idempotent and must complete once before any other storage access. It is never triggered lazily as a side effect of scheduling; accessing storage before Initialization is a usage error (see **Initialization Guard**).
_Avoid_: setup, bootstrap, registration

**Initialization Guard**:
The check that fails fast with a clear, actionable exception when storage is accessed before Initialization has completed in the current process. Tells the caller to run Initialization first (e.g. call `InitializeJobsAsync`).
_Avoid_: lazy init, auto-init

**Job Initializer**:
The single component responsible for performing Initialization. Exposed as `IJobInitializer`. Every path that needs a ready database funnels through it.
_Avoid_: initialization service (that name historically meant instance registration)

**Instance Registration**:
A running worker process announcing itself as a live job processor by writing a row to `job_instances`. Depends on Initialization having already completed.
_Avoid_: initialization, startup

**Worker Instance**:
A single running process that may schedule and/or execute jobs. Identified by `InstanceId`. Many Worker Instances can share one database.
_Avoid_: node, server, client

## Flagged ambiguities

- **"Initialization" historically meant two different things.** `PostgresInitializationService` performed *Instance Registration*, while the actual *Initialization* (migrations) happened lazily inside storage. Going forward, "Initialization" means migrations only; instance announcement is always called *Instance Registration*.

## Example dialogue

> **Dev:** On a clean database the app crashes on boot. Is registration broken?
> **Expert:** Registration is fine — the problem is it runs before Initialization. The `job_instances` table doesn't exist yet because migrations haven't run.
> **Dev:** So who runs the migrations?
> **Expert:** The Job Initializer. In ASP.NET it's invoked automatically at host start; elsewhere a Worker Instance calls `InitializeJobsAsync` itself. Instance Registration only happens after Initialization reports done.
