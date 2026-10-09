# Coding Conventions

## General

- Prefer small diffs over broad refactors.
- Avoid speculative abstractions — add them when a second caller actually appears.
- No backward-compat shims unless required by real consumers or persisted data. This library ships to NuGet, so treat the public API as a contract: preserve its shape unless the change is an intentional, called-out break.
- Keep files under roughly 500 lines when practical (test files may exceed this).

## Naming

- **Public types / methods / properties:** PascalCase.
- **Private fields:** `_camelCase`.
- **Locals / parameters:** camelCase.
- **Interfaces:** prefixed with `I` (e.g. `IJob`, `IJobScheduler`, `IClock`).
- **Async methods:** suffixed with `Async` (e.g. `ExecuteAsync`, `PerformNowAsync`).
- **Namespaces:** follow the folder structure (e.g. `mvdmio.ASP.Jobs.Internals.Storage.Postgres`).
- **File-scoped namespaces** are used throughout.

## Visibility

- **Public API is minimal** — only `IJob`, `Job<T>`, `IJobScheduler`, and configuration classes are public.
- Non-public types live in `Internals/` and are marked `internal`.
- Test projects get access via `InternalsVisibleTo`.

## Async / await

- All public APIs are async with `CancellationToken` support.
- Use the `ct = default` pattern for optional cancellation tokens.
- Handle `TaskCanceledException` / `OperationCanceledException` appropriately.

## Concurrency

- Never return a live view of a collection other threads change (such as `InMemoryJobStorage`'s job dictionaries); return a copy taken under the lock that guards it.

## PostgreSQL storage

- Scope every query on `mvdmio.jobs` to `application_name`: several applications share the table, and rows of another application are jobs this one never runs.
- Inside `InTransactionAsync`, run every statement on the `DatabaseConnection` that opened the transaction (`var db = Db;`, then `db.` throughout). The `Db` property builds a new wrapper on each access, so a statement on `Db` runs outside the transaction.
- Give every wait on a `NOTIFY` a time limit (see `MaxWaitTime`): a notification sent between the query and the start of `LISTEN` is lost.

## Code style

- **Nullable reference types:** enabled — respect nullability annotations.
- **Implicit usings:** disabled in the main project, enabled in test projects.
- **XML documentation:** required on all public members (`GenerateDocumentationFile` is on).
- **Access modifiers:** always explicit.
- Prefer explicit domain types over primitive bags; use `required` init properties where the project already does.
- Keep using directives minimal — remove duplicates and dead imports.

## Error handling

- Never swallow exceptions silently.
- Quote the real exception text when reporting/logging a failure.
- In tests, assert the exact behavior or message when it matters.
