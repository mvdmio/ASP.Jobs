# Testing

Test stack: **xUnit v3** + **NSubstitute** (mocking) + **AwesomeAssertions** (fluent assertions).

## Unit tests

- Location: `test/mvdmio.ASP.Jobs.Tests.Unit/`
- Mock dependencies with `NSubstitute`.
- Assert with `AwesomeAssertions`.
- Use `TestClock` for time-dependent tests and `TestJob` for controllable job behavior.

## Integration tests

- Location: `test/mvdmio.ASP.Jobs.Tests.Integration/`
- Use `Testcontainers.PostgreSql` for database tests; **Docker must be running**.
- Use `PostgresFixture` for the shared database container.

## Test utilities

| Utility | Purpose |
|---------|---------|
| `TestClock` | Controllable time for testing scheduled jobs |
| `TestJob` | Job with configurable delay and exception behavior |
| `JobTestServices` | Helper for setting up test DI containers |
| `LoggedParametersJob` | Job whose Job Parameters hold Logged Parameters (one nested) and unmarked secrets, and that throws on request; for asserting what log lines and spans show. Has its own throw switch because `TestJob` lacks the nested Logged Parameter and the secrets |
| `UnmarkedParametersJob` | Job whose Job Parameters mark nothing as logged, and that throws on request; for asserting that no parameter values are shown |
| `ReadCountingJob` | Job whose one Logged Parameter counts its getter reads; for asserting whether a run built the Logged Parameters, and how often |
| `SpanListenerCollection` | xunit collection name for tests that depend on whether the job activity source has a listener (listeners hear the whole process, so those tests run serially) |
| `RecordingLogger<T>` | Logger that records every entry at or above an optional minimum level, default all (level, message, exception and the named values of the message template), to assert which lines were logged, how often, and what they show |

Reuse these utilities rather than rolling your own.

## Running tests

- Whole solution: `dotnet test`
- One project (prefer this while iterating — faster, no Docker for unit tests):
  `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj`
- Single test by method-name substring (use `FullyQualifiedName~`; `Name~` matches no tests here and the run exits with "No test matches"):
  `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj --filter "FullyQualifiedName~SchedulesJobAtUtcTime"`
- By fully-qualified name (class/namespace substring):
  `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj --filter "FullyQualifiedName~JobScheduler"`
- Use `--filter` aggressively; integration tests are slower and need Docker.
- Never run build and test (or two test runs) in parallel — keep `dotnet` steps sequential to avoid file locks and deadlocks.
