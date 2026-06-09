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

Reuse these utilities rather than rolling your own.

## Running tests

- Whole solution: `dotnet test`
- One project (prefer this while iterating — faster, no Docker for unit tests):
  `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj`
- Single test by method-name substring:
  `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj --filter "Name~SchedulesJobAtUtcTime"`
- By fully-qualified name (class/namespace substring):
  `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj --filter "FullyQualifiedName~JobScheduler"`
- Use `--filter` aggressively; integration tests are slower and need Docker.
- Never run build and test (or two test runs) in parallel — keep `dotnet` steps sequential to avoid file locks and deadlocks.
