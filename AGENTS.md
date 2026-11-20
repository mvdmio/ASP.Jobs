# mvdmio.ASP.Jobs

Job scheduling library for ASP.NET Core — schedule and run background jobs with immediate, deferred, and CRON-based recurring execution. Shipped as the `mvdmio.ASP.Jobs` NuGet package (MIT).

## Shared rules

- Work on the repo's default branch unless a skill or the user names a branch. This overrides any default that creates branches.
- Preserve the worktree: parallel sessions share each checkout. Inspect existing changes before editing and leave them intact. When the index already holds another session's staged changes, commit with `git commit -- <your paths>`; for a file another session also edited, stage only your hunks (`git apply --cached` of your filtered diff) and run plain `git commit`.
- Search early. Quote exact errors.
- Ask when blocked, or when the design has more than one reasonable path.
- Telegraph style in replies and agent docs: drop filler and grammar.
- The main session orchestrates: unless the task is trivial, delegate exploring, implementing, testing and reviewing to subagents, at a model and effort that fit the task.
- [judgement] Prefer small diffs over broad refactors. Keep files under ~500 lines; tests may exceed it. Add an abstraction only once a second use exists. Ship no backward-compat shim unless real consumers or persisted data need one.
- Every feature and every bug fix comes with a test; a bug fix's test fails without the fix.
- Build, then test, one at a time: parallel builds deadlock on file locks. A build that fails on a file another process holds: stop that process, retry.
- Writing or reviewing C# → [`.agents/refs/csharp-style.md`](.agents/refs/csharp-style.md).

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
