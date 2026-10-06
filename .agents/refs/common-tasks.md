# Common Tasks

## Add a new job

1. Create a class inheriting `Job<TArgument>` or `Job`.
2. Implement `ExecuteAsync` with the job logic.
3. Register the job in DI with `services.RegisterJob<TJob>()`.
4. Schedule it through `IJobScheduler`.

## Add a database migration

1. Create a migration class in `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/Migrations/`.
2. Name it `_YYYYMMDDHHMM_DescriptiveName.cs`.
3. Implement `UpAsync`.

Migrations use the `mvdmio.Database.PgSQL.Migrations` framework.

## Extend storage

1. Implement the `IJobStorage` interface.
2. Add a configuration method to `JobConfigurationBuilder`.
3. Add integration tests using the appropriate test fixtures.
