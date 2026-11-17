# mvdmio.ASP.Jobs

Job scheduling library for ASP.NET Core — schedule and run background jobs with immediate, deferred, and CRON-based recurring execution. Shipped as the `mvdmio.ASP.Jobs` NuGet package (MIT).

## Essentials

- **Build tool:** .NET SDK (`dotnet`). Multi-targets `net10.0;net11.0` with `LangVersion=latest`.
- **Build:** `dotnet build`
- **Test:** `dotnet test` — PostgreSQL integration tests need **Docker running** (Testcontainers).
- Before finishing any change, confirm it builds (`dotnet build`) then tests pass (`dotnet test`).
- This is a published NuGet library: treat the public API as a contract. No API changes unless intentional and called out.

## Reference docs

Read the relevant file before working in that area:

- [Architecture & layout](.agents/refs/architecture.md) — project structure, job lifecycle, storage, key files & features
- [Coding conventions](.agents/refs/conventions.md) — naming, visibility, async, code style
- [Testing](.agents/refs/testing.md) — unit & integration patterns, test utilities
- [Common tasks](.agents/refs/common-tasks.md) — adding a job, a migration, or a storage backend
- [Dependencies & CI/CD](.agents/refs/dependencies.md) — package list and publish pipeline

## Agent skills

### Issue tracker

Work is tracked as GitHub Issues in `mvdmio/ASP.Jobs`. See `.agents/refs/tracker.md`.

### Domain docs

Single-context (`CONTEXT.md` + `docs/adr/` at the repo root). See `.agents/refs/domain.md`.
