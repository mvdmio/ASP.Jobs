# mvdmio.ASP.Jobs

Job scheduling library for ASP.NET Core — schedule and run background jobs with immediate, deferred, and CRON-based recurring execution. Shipped as the `mvdmio.ASP.Jobs` NuGet package (MIT). Work style: telegraph, low-filler, direct.

## Essentials

- **Build tool:** .NET SDK (`dotnet`). Multi-targets `net8.0;net9.0;net10.0` with `LangVersion=latest`.
- **Build:** `dotnet build`
- **Test:** `dotnet test` — PostgreSQL integration tests need **Docker running** (Testcontainers).
- Before finishing any change, confirm it builds (`dotnet build`) then tests pass (`dotnet test`). Run these **sequentially, never in parallel** — overlapping `dotnet` runs cause file locks/deadlocks.
- This is a published NuGet library: treat the public API as a contract. No backward-compat shims and no API changes unless intentional and called out.

## Universal rules

- **Never branch.** This repo uses a single-branch workflow — when asked to commit/push, commit on the current branch (`main`) and push directly. Only create a branch when the user explicitly asks for one by name.
- The main session is the orchestrator. Unless the task is trivial, delegate the actual work (explore, implement, test, review) to subagents using a model and reasoning level appropriate for the task.
- Search early. Quote exact errors. If blocked or the design is unclear, ask.

## Reference docs

Read the relevant file before working in that area:

- [Architecture & layout](.agents/refs/architecture.md) — project structure, job lifecycle, storage, key files & features
- [Coding conventions](.agents/refs/conventions.md) — naming, visibility, async, code style
- [Testing](.agents/refs/testing.md) — unit & integration patterns, test utilities
- [Common tasks](.agents/refs/common-tasks.md) — adding a job, a migration, or a storage backend
- [Dependencies & CI/CD](.agents/refs/dependencies.md) — package list and publish pipeline

## Agent skills

### Issue tracker

Specs and issues live as markdown under `.agents/specs/` and `.agents/issues/`. See `.agents/refs/issue-tracker.md`.

### Triage labels

Default vocabulary (needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix). See `.agents/refs/triage-labels.md`.

### Domain docs

Single-context (`CONTEXT.md` + `docs/adr/` at the repo root). See `.agents/refs/domain.md`.
