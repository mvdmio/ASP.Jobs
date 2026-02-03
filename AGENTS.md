# AGENTS.md - OpenCode AI Agent Instructions

This file provides context and guidelines for AI agents working on this codebase.

## Project Overview

**mvdmio.ASP.Jobs** is a job scheduling library for ASP.NET Core applications. It provides a simple API to schedule and execute background jobs with support for immediate execution, deferred execution, and CRON-based recurring schedules.

- **Author**: Michiel van der Meer (mvdmio)
- **License**: MIT
- **NuGet Package**: `mvdmio.ASP.Jobs`

## Technology Stack

| Component | Technology |
|-----------|------------|
| Language | C# (.NET) |
| Target Frameworks | .NET 8.0, .NET 9.0, .NET 10.0 (multi-targeting) |
| Language Version | Latest (C# 12+) |
| Framework Type | ASP.NET Core library |
| Test Framework | xUnit v3 |
| Mocking | NSubstitute |
| Assertions | AwesomeAssertions |
| Integration Tests | Testcontainers (PostgreSQL) |

## Project Structure

```
mvdmio.ASP.Jobs/
├── src/
│   └── mvdmio.ASP.Jobs/              # Main library
│       ├── Internals/                 # Internal implementations (not public API)
│       │   ├── JobRunnerService.cs    # BackgroundService for job execution
│       │   ├── JobScheduler.cs        # IJobScheduler implementation
│       │   └── Storage/               # Job storage implementations
│       │       ├── InMemoryJobStorage.cs
│       │       └── Postgres/          # PostgreSQL storage
│       │           ├── Migrations/    # Database migrations
│       │           └── Repository/    # Repository pattern
│       ├── Utils/                     # Utility classes
│       ├── IJob.cs                    # Job interface
│       ├── IJobScheduler.cs           # Scheduler interface
│       └── DependencyInjectionExtensions.cs
├── test/
│   ├── mvdmio.ASP.Jobs.Tests.Unit/           # Unit tests
│   └── mvdmio.ASP.Jobs.Tests.Integration/    # Integration tests
├── .github/workflows/                 # CI/CD
└── README.md                          # Usage documentation
```

## Build Commands

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run all tests
dotnet test

# Run tests with verbosity
dotnet test --verbosity normal

# Create NuGet package
dotnet pack
```

## Key Patterns and Conventions

### Naming Conventions

- **Interfaces**: Prefixed with `I` (e.g., `IJob`, `IJobScheduler`, `IClock`)
- **Async methods**: Suffixed with `Async` (e.g., `ExecuteAsync`, `PerformNowAsync`)
- **Namespaces**: Follow folder structure (e.g., `mvdmio.ASP.Jobs.Internals.Storage.Postgres`)
- **File-scoped namespaces**: Used throughout the codebase

### Visibility Rules

- **Public API**: Minimal surface - only `IJob`, `Job<T>`, `IJobScheduler`, and configuration classes
- **Internal classes**: Placed in `Internals/` folder and marked `internal`
- **Test access**: Uses `InternalsVisibleTo` for test projects

### Async/Await Pattern

- All public APIs are async with `CancellationToken` support
- Use `ct = default` pattern for optional cancellation tokens
- Handle `TaskCanceledException` / `OperationCanceledException` appropriately

### Job Lifecycle

Jobs implement the following lifecycle methods:

1. `OnJobScheduledAsync` - Called when job is scheduled (preparation)
2. `ExecuteAsync` - Main job execution logic
3. `OnJobExecutedAsync` - Called after successful execution
4. `OnJobFailedAsync` - Called on exception

### Storage Abstraction

Two storage implementations:

1. `InMemoryJobStorage` - Default, non-persistent, single-instance
2. `PostgresJobStorage` - Distributed, persistent, multi-instance support

### Database Migrations

- Location: `Internals/Storage/Postgres/Migrations/`
- Naming convention: `_YYYYMMDDHHMM_MigrationName.cs`
- Uses `mvdmio.Database.PgSQL.Migrations` framework

## Testing Guidelines

### Unit Tests

- Location: `test/mvdmio.ASP.Jobs.Tests.Unit/`
- Use `NSubstitute` for mocking dependencies
- Use `AwesomeAssertions` for fluent assertions
- Use `TestClock` for time-dependent tests
- Use `TestJob` for controllable job behavior

### Integration Tests

- Location: `test/mvdmio.ASP.Jobs.Tests.Integration/`
- Use `Testcontainers.PostgreSql` for database tests
- Use `PostgresFixture` for shared database container

### Test Utilities

- `TestClock`: Controllable time for testing scheduled jobs
- `TestJob`: Job with configurable delay and exception behavior
- `JobTestServices`: Helper for setting up test DI containers

## Code Style

- **Nullable reference types**: Enabled
- **Implicit usings**: Disabled in main project, enabled in tests
- **XML documentation**: Required on all public members
- **Access modifiers**: Always explicit (`internal` for non-public types)

## Dependencies

### Main Library

| Package | Purpose |
|---------|---------|
| `Cronos` | CRON expression parsing |
| `mvdmio.Database.PgSQL` | PostgreSQL database access |
| `OpenTelemetry.Api` | Distributed tracing |
| `Serilog` | Structured logging |
| `Microsoft.Extensions.DependencyInjection` | DI integration |
| `Microsoft.Extensions.Hosting.Abstractions` | BackgroundService support |

### Tests

| Package | Purpose |
|---------|---------|
| `xunit.v3` | Test framework |
| `NSubstitute` | Mocking |
| `AwesomeAssertions` | Assertions |
| `Testcontainers.PostgreSql` | PostgreSQL integration tests |

## CI/CD

- **Pipeline**: `.github/workflows/publish-nuget.yml`
- **Triggers**: Push to `main` (changes in `src/**`) or manual dispatch
- **Actions**: Build, test, publish to NuGet.org

## Key Features

1. **Immediate execution** - `PerformNowAsync()`: Synchronous job execution
2. **ASAP execution** - `PerformAsapAsync()`: Queue for immediate processing
3. **Scheduled execution** - `PerformAtAsync()`: Run at specific UTC time
4. **CRON scheduling** - `PerformCronAsync()`: Recurring jobs using CRON expressions
5. **Job groups**: Sequential execution within groups
6. **Job naming**: Deduplication by name
7. **Multi-threaded execution**: Configurable via `JobRunnerThreadsCount`
8. **OpenTelemetry tracing**: Activity source `"mvdmio.ASP.Jobs"`

## Common Tasks

### Adding a New Job

1. Create a class inheriting from `Job<TArgument>` or implementing `IJob`
2. Implement `ExecuteAsync` with the job logic
3. Register the job in DI using the configuration builder
4. Schedule using `IJobScheduler`

### Adding a New Migration

1. Create a new migration class in `Internals/Storage/Postgres/Migrations/`
2. Name it `_YYYYMMDDHHMM_DescriptiveName.cs`
3. Implement `UpAsync` and `DownAsync` methods

### Extending Storage

1. Implement `IJobStorage` interface
2. Add configuration method to `JobConfigurationBuilder`
3. Add integration tests with appropriate test fixtures

## Important Files

| File | Purpose |
|------|---------|
| `src/mvdmio.ASP.Jobs/IJob.cs` | Core job interface and base class |
| `src/mvdmio.ASP.Jobs/IJobScheduler.cs` | Scheduler interface |
| `src/mvdmio.ASP.Jobs/DependencyInjectionExtensions.cs` | DI registration |
| `src/mvdmio.ASP.Jobs/JobConfigurationBuilder.cs` | Fluent configuration API |
| `src/mvdmio.ASP.Jobs/Internals/JobRunnerService.cs` | Background job processor |
| `src/mvdmio.ASP.Jobs/Internals/Storage/IJobStorage.cs` | Storage abstraction |

## Notes for AI Agents

- Always run `dotnet build` to verify changes compile
- Always run `dotnet test` to verify tests pass
- Keep public API minimal - prefer internal implementations
- Follow existing async/await patterns consistently
- Add XML documentation to any new public members
- Use the existing test utilities when writing tests
- PostgreSQL integration tests require Docker to be running
