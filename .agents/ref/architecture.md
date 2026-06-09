# Architecture & Layout

## Project structure

```
mvdmio.ASP.Jobs/
├── src/
│   └── mvdmio.ASP.Jobs/                 # Main library
│       ├── Internals/                   # Internal implementations (not public API)
│       │   ├── JobRunnerService.cs      # BackgroundService for job execution
│       │   ├── JobScheduler.cs          # IJobScheduler implementation
│       │   └── Storage/                 # Job storage implementations
│       │       ├── InMemoryJobStorage.cs
│       │       └── Postgres/            # PostgreSQL storage
│       │           ├── Migrations/      # Database migrations
│       │           └── Repository/      # Repository pattern
│       ├── Utils/                       # Utility classes
│       ├── IJob.cs                      # Job interface
│       ├── IJobScheduler.cs             # Scheduler interface
│       └── DependencyInjectionExtensions.cs
├── test/
│   ├── mvdmio.ASP.Jobs.Tests.Unit/          # Unit tests
│   └── mvdmio.ASP.Jobs.Tests.Integration/   # Integration tests
├── .github/workflows/                   # CI/CD
└── Readme.md                            # Usage documentation
```

## Job lifecycle

Jobs implement these lifecycle methods, called in order:

1. `OnJobScheduledAsync` — when the job is scheduled (preparation)
2. `ExecuteAsync` — main execution logic
3. `OnJobExecutedAsync` — after successful execution
4. `OnJobFailedAsync` — on exception

## Storage abstraction

Two implementations behind `IJobStorage`:

1. `InMemoryJobStorage` — default; non-persistent, single-instance
2. `PostgresJobStorage` — distributed, persistent, multi-instance

## Key features

- **Immediate execution** — `PerformNowAsync()`: synchronous execution
- **ASAP execution** — `PerformAsapAsync()`: queue for immediate processing
- **Scheduled execution** — `PerformAtAsync()`: run at a specific UTC time
- **CRON scheduling** — `PerformCronAsync()`: recurring jobs via CRON expressions
- **Job groups** — sequential execution within a group
- **Job naming** — deduplication by name
- **Multi-threaded execution** — configurable via `JobRunnerThreadsCount`
- **OpenTelemetry tracing** — activity source `"mvdmio.ASP.Jobs"`

## Important files

| File | Purpose |
|------|---------|
| `src/mvdmio.ASP.Jobs/IJob.cs` | Core job interface and base class |
| `src/mvdmio.ASP.Jobs/IJobScheduler.cs` | Scheduler interface |
| `src/mvdmio.ASP.Jobs/DependencyInjectionExtensions.cs` | DI registration |
| `src/mvdmio.ASP.Jobs/JobConfigurationBuilder.cs` | Fluent configuration API |
| `src/mvdmio.ASP.Jobs/Internals/JobRunnerService.cs` | Background job processor |
| `src/mvdmio.ASP.Jobs/Internals/Storage/IJobStorage.cs` | Storage abstraction |
