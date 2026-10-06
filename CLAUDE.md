# mvdmio.ASP.Jobs

- Reply telegraph-style — low filler, direct — and quote errors verbatim. Ask when blocked or when the design is unclear.
- The main session orchestrates: delegate non-trivial work — explore, implement, test, review — to subagents, matching model and reasoning effort to the task.
- When asked to commit or push, commit on the current branch (`main`) and push it directly; create a branch only when the user names one. A push that changes `src/` publishes to NuGet whenever the library csproj's `<Version>` is new.
- Finish each change with `dotnet build`, then `dotnet test`, one `dotnet` process at a time across all agents: overlapping runs lock files and deadlock. Integration tests need Docker running (Testcontainers).

## Context

- Before writing code or running tests → `CODING_STANDARDS.md`.
- Before reading or writing an Issue → `.agents/refs/tracker.md`.
