# mvdmio.ASP.Jobs

Job scheduling library for ASP.NET Core — immediate, deferred, and CRON-based recurring background jobs — shipped as the `mvdmio.ASP.Jobs` NuGet package (MIT).

## Every task

- Work style: telegraph, low-filler, direct. Quote errors verbatim.
- If blocked or the design is unclear, ask.
- The main session orchestrates: delegate non-trivial work (explore, implement, test, review) to subagents, picking model and reasoning effort to fit the task.
- When asked to commit or push, commit on the current branch (`main`) and push it directly. Create a branch only when the user names one.
- Finish every change green: `dotnet build`, then `dotnet test`. Run `dotnet` commands one at a time — overlapping runs (build with test, or two test runs) lock files and deadlock. Integration tests need Docker running (Testcontainers).

## Context

- Before exploring an area → `CONTEXT.md` (glossary) and the `docs/adr/` records that touch it.
- Before writing or changing code → `CODING_STANDARDS.md`.
- Before changing anything under `src/` → `.agents/refs/architecture.md`.
- Before writing tests or running a subset → `.agents/refs/testing.md`.
- Before changing packages or the release pipeline → `.agents/refs/dependencies.md`.
- Before reading or writing an Issue → `.agents/refs/tracker.md`.
