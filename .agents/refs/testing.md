# Testing

## Projects

- Unit: `test/mvdmio.ASP.Jobs.Tests.Unit/` — helpers in `Utils/`.
- Integration: `test/mvdmio.ASP.Jobs.Tests.Integration/` — fixtures in `Fixtures/`.
- Both target `net10.0` only.

## Test utilities

| Utility | Project | Purpose |
|---------|---------|---------|
| `TestClock` | Unit | Controllable time for testing scheduled jobs |
| `TestJob` | Unit | Job with configurable delay and exception behavior |
| `JobTestServices` | Unit | Sets up test DI containers |
| `PostgresFixture` | Integration | Shared PostgreSQL container for the whole assembly |

## Running tests

- One project — the fastest loop; the unit project needs no Docker:
  `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj`
- Narrow further by method or class substring:
  `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj --filter "FullyQualifiedName~HandleCrash"` (or `~JobRunnerServiceTests`)
