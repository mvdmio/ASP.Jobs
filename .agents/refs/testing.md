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
| `RecordingLogger<T>` | Logger that records every entry (level, message, exception and the named values of the message template), to assert which lines were logged, how often, and what they show |
| `SerilogCapture` | Records events on Serilog's static `Log.Logger` (what `JobScheduler` logs through) and restores it on dispose; test classes using it join the `[Collection(SerilogCapture.Name)]` xunit collection and match events by their own unique id |

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
