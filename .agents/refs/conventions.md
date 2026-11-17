# Coding Conventions

## General

- This library ships to NuGet, so treat the public API as a contract: preserve its shape unless the change is an intentional, called-out break.

## Visibility

- **Public API is minimal** — only `IJob`, `Job<T>`, `IJobScheduler`, `LoggedParameterAttribute`, and configuration classes are public.
- Non-public types live in `Internals/` and are marked `internal`.
- Test projects get access via `InternalsVisibleTo`.

## Async / await

- All public APIs are async with `CancellationToken` support.
- Handle `TaskCanceledException` / `OperationCanceledException` appropriately.

## Concurrency

- Never return a live view of a collection other threads change (such as `InMemoryJobStorage`'s job dictionaries); return a copy taken under the lock that guards it.

## PostgreSQL storage

- Scope every query on `mvdmio.jobs` to `application_name`: several applications share the table, and rows of another application are jobs this one never runs.
- Inside `InTransactionAsync`, run every statement on the `DatabaseConnection` that opened the transaction (`var db = Db;`, then `db.` throughout). The `Db` property builds a new wrapper on each access, so a statement on `Db` runs outside the transaction.
- Give every wait on a `NOTIFY` a time limit (see `MaxWaitTime`): a notification sent between the query and the start of `LISTEN` is lost.

## Code style

- **Implicit usings:** disabled in the main project, enabled in test projects.
- Prefer explicit domain types over primitive bags; use `required` init properties where the project already does.

## Error handling

- In tests, assert the exact behavior or message when it matters.
