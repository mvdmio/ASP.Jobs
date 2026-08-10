# Dependencies & CI/CD

## Main library

| Package | Purpose |
|---------|---------|
| `Cronos` | CRON expression parsing |
| `mvdmio.Database.PgSQL` | PostgreSQL database access |
| `OpenTelemetry.Api` | Distributed tracing |
| `Serilog` | Structured logging |
| `Microsoft.Extensions.DependencyInjection` (+ `.Abstractions`) | DI integration |
| `Microsoft.Extensions.Hosting.Abstractions` | `BackgroundService` support |
| `Microsoft.Bcl.AsyncInterfaces` | Async interface polyfills |
| `JetBrains.Annotations` | Code annotations |
| `PolySharp` | Polyfills for newer language features on older TFMs |

## Tests

| Package | Purpose |
|---------|---------|
| `xunit.v3` | Test framework |
| `NSubstitute` | Mocking |
| `AwesomeAssertions` | Assertions |
| `Testcontainers.PostgreSql` | PostgreSQL integration tests |

## CI/CD

- **Pipeline:** `.github/workflows/publish-nuget.yml`
- **Triggers:** push to `main` with changes under `src/**`, or manual dispatch.
- **Actions:** build, test, publish to NuGet.org.
