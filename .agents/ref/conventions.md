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
