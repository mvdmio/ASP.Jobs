# 02 — Name the job runner's pool after the program

Status: done
Depends on: 01

## What to build

ASP.Jobs' own Postgres connection pool, the keyed `"Jobs"` `DatabaseConnectionFactory`, is now named after the host program with `.Jobs` added, for example `mvdmio.Compliance.Web.Jobs`. Every connection the job storage opens shows that name in `pg_stat_activity.application_name`, and Database.PgSQL's traces use it as the data source name. An operator can then tell the job runner's connections from the app's own pool, and one app's job runner from another's.

- **Dependency.** Raise `mvdmio.Database.PgSQL` from 0.29.0 to 0.41.0. That release came from the blocking Issue (mvdmio/Database.PgSQL#3). It adds `DatabaseConnectionFactorySettings` (`MaxPoolSize`, `ApplicationName`) and a `DatabaseConnectionFactory(DatabaseConnectionFactorySettings)` constructor, caps every pool at 10 by default, and sets the data source's diagnostic name to the pool name.
  - 0.41.0 was pushed to nuget.org on 2026-10-09 at about 10:25 UTC and may still be indexing. If restore cannot find it, wait and retry. Do not commit a local package source.
  - Anything the newer package breaks or flags in ASP.Jobs is fixed in this step. Its analyzers ship with it.
- **The name rule.** The pool name is the entry assembly's simple name plus `.Jobs`. When there is no entry assembly, or its name is null or empty, the name is the `applicationName` given to `UsePostgresStorage` plus `.Jobs`.
  - Put this rule in a small internal pure function that takes the entry assembly name (nullable) and the application name. A unit test can then cover the fallback, which no integration run can reach.
- **The registration.** `JobConfigurationBuilder.SetupServices` registers the keyed `"Jobs"` factory by delegate. Today it uses `AddKeyedSingleton<DatabaseConnectionFactory>("Jobs")`. Since 0.41.0 the factory has two constructors, so type activation is no longer safe.
  - The delegate builds the factory with `DatabaseConnectionFactorySettings { ApplicationName = <the name> }`. It reads `ApplicationName` from `IOptions<PostgresJobStorageConfiguration>` for the fallback.
  - ASP.Jobs sets no `MaxPoolSize`. The connection string's `Maximum Pool Size` applies when it is there, otherwise the Database.PgSQL default of 10.
- **One factory for everything.** `PostgresJobStorage`, including its migrations and the dedicated `Db.WaitAsync` LISTEN connection, and `PostgresJobInstanceRepository` already take `[FromKeyedServices("Jobs")]`. Keep it that way, so every job-storage connection carries the name.
- **Versioning.** Bump the package `<Version>` from 4.8.0 to 4.9.0.
- **Docs.** In the README `## PostgreSQL storage` section that step 01 created, add that the job storage's connections are named `<entry assembly>.Jobs`, falling back to `<applicationName>.Jobs`. Say that the pool is capped by the connection string's `Maximum Pool Size`, or else the Database.PgSQL default of 10. Add one sentence about the pool name to the XML doc of `UsePostgresStorage`, and leave its signature unchanged.

Out of scope: a cap set by ASP.Jobs, and the instance id's machine-name condition, which looks inverted in `PostgresJobStorageConfiguration.InstanceId`. Do not touch either. Do not touch the Changelog either.

## Footprint

Projects: `src/mvdmio.ASP.Jobs`, `test/mvdmio.ASP.Jobs.Tests.Unit`, `test/mvdmio.ASP.Jobs.Tests.Integration`

- `src/mvdmio.ASP.Jobs/mvdmio.ASP.Jobs.csproj` — the `mvdmio.Database.PgSQL` `PackageReference` (0.29.0 to 0.41.0) and `<Version>` (4.8.0 to 4.9.0).
- `src/mvdmio.ASP.Jobs/JobConfigurationBuilder.cs` — `SetupServices` (the keyed `"Jobs"` `DatabaseConnectionFactory` registration) and the `UsePostgresStorage` XML doc.
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/` — the new internal pool-name function. It can go in a new small file here, or beside the registration if that reads better.
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorageConfiguration.cs` — `ApplicationName`. Read only.
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/PostgresJobStorage.cs` — constructor `[FromKeyedServices("Jobs")]`, `Db`, `RunDbMigrations`, `SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt` (the LISTEN). Verify that all of these keep going through the keyed factory.
- `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/Repository/PostgresJobInstanceRepository.cs` — constructor `[FromKeyedServices("Jobs")]`. Verify only.
- `test/mvdmio.ASP.Jobs.Tests.Unit/` — a new unit test for the pool-name function.
- `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/` — a new integration test, or a new test in `CleanDatabaseStartupTests.cs`. Follow that class's prior art: its own `PostgreSqlContainer`, and a host built through `AddJobs(o => o.UsePostgresStorage(...))`. Do not use `PostgresStorageHarness`, which builds its own factory and would bypass the name.
- `test/mvdmio.ASP.Jobs.Tests.Integration/Fixtures/PostgresFixture.cs` — touch only if the new default cap of 10 starves existing integration tests. A test-only cap through `DatabaseConnectionFactorySettings` is fine there. ASP.Jobs itself still sets none.
- `README.md` — the `## PostgreSQL storage` section.

## Acceptance criteria

- [ ] `mvdmio.ASP.Jobs.csproj` references `mvdmio.Database.PgSQL` 0.41.0, and its `<Version>` is 4.9.0.
- [ ] A unit test pins the name rule: entry assembly `Foo.Web` with application name `bar` gives `Foo.Web.Jobs`. A null or empty entry assembly name with application name `bar` gives `bar.Jobs`.
- [ ] An integration test starts a host with Postgres storage through `AddJobs` and `UsePostgresStorage` on a dedicated container. It waits until the runner sits in its LISTEN, which shows in `pg_stat_activity.query` as `LISTEN jobs_updated`.
  - It checks that every client connection to the test database other than the test's own (`pid <> pg_backend_pid()`) has `application_name` equal to the entry assembly's simple name plus `.Jobs`. The LISTEN connection must be among them.
  - It works out the expected name from the entry assembly at run time, and does not hard-code it.
- [ ] The keyed `"Jobs"` factory is registered by delegate with an explicit `ApplicationName` and no `MaxPoolSize`.
- [ ] `PostgresJobStorage` and `PostgresJobInstanceRepository` still get only the keyed `"Jobs"` factory, so the migrations, the LISTEN connection and the instance repository all carry the name.
- [ ] The README `## PostgreSQL storage` section states the pool name, its fallback and the cap rule. The `UsePostgresStorage` XML doc mentions the pool name, and the public API is unchanged.
- [ ] `dotnet build`, then `dotnet test`, run one after the other, pass for the whole solution, integration tests included. They need Docker.

## Outcome

- `mvdmio.Database.PgSQL` raised to 0.41.0; package `<Version>` 4.9.0.
- Name rule: `internal static string JobsPoolName.Create(string? entryAssemblyName, string applicationName)` in `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/JobsPoolName.cs`.
- `JobConfigurationBuilder.SetupServices` registers the keyed `"Jobs"` factory by delegate with `DatabaseConnectionFactorySettings { ApplicationName = JobsPoolName.Create(Assembly.GetEntryAssembly()?.GetName().Name, options.ApplicationName) }` and no `MaxPoolSize`. The `UsePostgresStorage` XML doc gains one sentence; its signature is unchanged.
- Verified: `PostgresJobStorage` (migrations via `Db`, LISTEN via `Db.WaitAsync`) and `PostgresJobInstanceRepository` still take only `[FromKeyedServices("Jobs")]`.
- Tests: `test/mvdmio.ASP.Jobs.Tests.Unit/JobsPoolNameTests.cs` (name rule and fallback); `test/mvdmio.ASP.Jobs.Tests.Integration/Postgres/JobsPoolNameTests.cs` (dedicated container, host through `AddJobs`/`UsePostgresStorage`, waits for the LISTEN, checks every other client connection's `application_name`). The LISTEN is matched with `query ILIKE 'LISTEN%jobs_updated%'`, so quoting of the channel name does not matter.
- README `## PostgreSQL storage` states the pool name, its fallback and the cap rule.
- Footprint drift: `PostgresFixture.cs` is touched, not for the cap (the default of 10 starved nothing) but because 0.41.0 marks `DatabaseMigrator(DatabaseConnection, params Assembly[])` obsolete (CS0618); it now passes `NullLoggerFactory.Instance`.
- Whole suite: unit 97/97, integration 108/108.

- Checker: the integration test now opens its observer connection once (`await db.OpenAsync(ct)`) and disposes it, so `pid <> pg_backend_pid()` always excludes the observer. README and the `UsePostgresStorage` XML doc now also name the "entry assembly has no name" fallback. Usings in `PostgresFixture.cs` reordered.
- Checker re-run: whole suite green (unit 97/97, integration 108/108). With the suffix changed to `.Broken`, the integration test fails, so it guards the name.

Safety fact: every connection the job storage opens, the LISTEN connection included, carries `<entry assembly>.Jobs` as its `application_name`, falling back to `<applicationName>.Jobs`; if false, operators cannot tell the job runner's pool from the app's own pool on a shared server, and the keyed factory may fail to resolve now that it has two constructors (rung 3)
Proof: `dotnet test test/mvdmio.ASP.Jobs.Tests.Integration/mvdmio.ASP.Jobs.Tests.Integration.csproj --filter "FullyQualifiedName~JobsPoolNameTests"` exit 0 — Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1
Merge risk: easy — reverting restores the unnamed pool and Database.PgSQL 0.29.0; nothing persisted, no release published; affects operators reading `pg_stat_activity`, and hosts that now get Database.PgSQL 0.41.0's default cap of 10 on the job pool and its transitive dependency bump
