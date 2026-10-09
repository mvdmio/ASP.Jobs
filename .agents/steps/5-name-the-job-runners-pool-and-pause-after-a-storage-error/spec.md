# Name the job runner's pool, and pause after a storage error

Blocked by: the Database.PgSQL Issue "Cap and name every connection pool by default" (mvdmio/Database.PgSQL#3).

## Problem Statement

ASP.Jobs keeps its job storage in Postgres through its own `DatabaseConnectionFactory`, registered under the key "Jobs". That factory builds a separate connection pool from the host app's own pool, even though both use the same connection string. Two problems follow.

**Its connections can't be told apart.** On a Postgres server that several apps share, every ASP.Jobs connection shows up in `pg_stat_activity` like any other, with no application name. Database.PgSQL is about to name every pool after the program's entry assembly. ASP.Jobs' pool would then carry the same name as the host app's pool, so the two still couldn't be told apart. The `applicationName` that `UsePostgresStorage` receives stays inside the job storage: it is written into the job and instance tables and forms part of the instance id. It never reaches Npgsql.

**The producer spins when the storage fails.** When fetching the next job throws, for example because Postgres is restarting or has run out of connection slots, the producer loop logs "Error while fetching next job from storage" and tries again at once, with no delay. During an outage every app that hosts ASP.Jobs spins in this loop. Each pass tries to open a new connection and logs another error, and a host that forwards errors to Sentry turns each one into an event. When Postgres refuses connections with `53300: sorry, too many clients already`, the loop adds connection attempts at exactly the moment the server is full.

## Solution

The job runner's pool is named after the program, with `.Jobs` added, for example `mvdmio.Compliance.Web.Jobs`. Its cap is the Database.PgSQL default, unless the connection string sets one.

After a storage error, the producer waits before it tries again. The first wait is 1 second. Each further consecutive error doubles the wait, up to 30 seconds. A successful fetch resets it.

## User Stories

1. As an operator of a Postgres server shared by several apps, I want ASP.Jobs' connections to carry their own name, so that I can tell the job runner's connections from the app's own connections.
2. As an operator, I want that name to include the host program's name, so that I can tell one app's job runner from another's.
3. As an operator, I want ASP.Jobs' pool capped by the same default as every other Database.PgSQL pool, so that the job runner cannot take every connection slot.
4. As a developer whose connection string sets `Maximum Pool Size`, I want that value to apply to ASP.Jobs' pool too, so that one setting governs both pools.
5. As an operator, I want the job runner to pause after a storage error, so that a database outage does not turn into a tight loop of connection attempts.
6. As an operator, I want the pause to grow while the errors continue, so that a long outage produces a handful of errors rather than thousands.
7. As an operator, I want the pause capped at 30 seconds, so that jobs resume within half a minute of the database coming back.
8. As an operator, I want the pause to reset after a successful fetch, so that one blip doesn't slow the next one down.
9. As an operator shutting an app down, I want shutdown to cut the pause short, so that stopping the app doesn't wait out a backoff.
10. As a developer reading traces, I want ASP.Jobs' database spans to carry the `.Jobs` name, so that I can tell job-storage queries from the app's own queries.

## Implementation Decisions

- **Dependency.** Requires the Database.PgSQL release from the blocking Issue, whose connection factory takes a pool cap and a name. Raise ASP.Jobs' minimum Database.PgSQL version (0.29.0 today) to that release.
- **The keyed "Jobs" factory is built with an explicit name:** the entry assembly's simple name plus `.Jobs`. When there is no entry assembly, use the `applicationName` given to `UsePostgresStorage`, plus `.Jobs`. ASP.Jobs sets no cap, so the connection string's `Maximum Pool Size` applies when present, and otherwise the Database.PgSQL default of 10.
- **Every connection the job storage opens goes through that factory,** including the dedicated LISTEN connection, the migrations and the instance repository, so they all carry the name. This already holds today: `PostgresJobStorage` (its migrations and `Db.WaitAsync` LISTEN included) and `PostgresJobInstanceRepository` both take the keyed "Jobs" factory. Keep it that way.
- **Traces.** Database.PgSQL sets the data source's diagnostic name to the same name, so ASP.Jobs' database spans carry the `.Jobs` name with no further work here.
- **Backoff in the producer loop** (the job runner service's produce loop, where "Error while fetching next job from storage" is logged today):
  - After a caught error, wait before the next attempt.
  - The wait starts at 1 second and doubles on each consecutive error, up to 30 seconds.
  - A fetch that returns without an error, with or without a job, resets it to 1 second for the next error.
  - The wait observes the stopping token, so shutdown ends it at once.
  - Each error is still logged once, as today. Only the gap between attempts changes.
  - The values are fixed inside the library and not configurable.
- **Versioning.** Bump the minor version after the current release (v4.8.0 at the time of writing). The README notes the pool name and the backoff.

## Testing Decisions

- A good test drives the job runner through its public setup against a real Postgres, or through a storage stand-in, and checks behaviour seen from outside: names in `pg_stat_activity`, and the spacing or count of fetch attempts. It doesn't check private fields.
- Integration test, using the existing Testcontainers Postgres tests as prior art: start a runner with Postgres storage through `AddJobs` and `UsePostgresStorage`, as `CleanDatabaseStartupTests` does (`PostgresStorageHarness` builds its own factory and would bypass the name), then check that its connections, the LISTEN connection included, show `<entry assembly>.Jobs` in `pg_stat_activity.application_name`.
- Backoff test: use a storage stand-in that throws on fetch. Show that consecutive attempts are spaced 1, 2, 4 … seconds apart, capped at 30, and that one success resets the spacing.
  - The runner takes an `IClock`, but it only reads the time (`UtcNow`, `Now`) and cannot drive a wait. No new time seam is added.
  - The wait sequence is computed by a small pure function: given the number of consecutive errors, it returns the next wait. A unit test pins that sequence exactly: 1, 2, 4, 8, 16, 30, 30 seconds, and 1 second again after a reset.
  - The loop test counts the attempts made within a few seconds, and checks they are a handful, not hundreds.
  - Follow the existing job runner service tests (`JobRunnerServiceTests`, `JobRunnerHarness`). The unit test project already references NSubstitute for a throwing storage stand-in.
- A shutdown test shows that stopping the runner during a backoff wait returns promptly.

## Out of Scope

- Any change to how jobs run, how many run at once (`MaxConcurrentJobs`), or the claim and LISTEN/NOTIFY design.
- A cap set by ASP.Jobs itself.
- Retry behaviour inside individual jobs, and backoff for the cleanup service.
- Changes in the mvdmio suite, which takes the new version in mvdmio-suite #263.

## Further Notes

- Origin: the grilling of mvdmio-suite #263 and #264 on 2026-10-08 and 2026-10-09.
- During exploration, the instance id seemed to use the machine name only when the machine name is empty, which looks like an inverted condition. It is not part of this Issue.

