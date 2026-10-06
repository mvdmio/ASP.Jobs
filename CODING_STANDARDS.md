# Coding Conventions

## General

- Avoid speculative abstractions — add them when a second caller actually appears.
- No backward-compat shims unless required by real consumers or persisted data. This library ships to NuGet, so treat the public API as a contract: preserve its shape unless the change is an intentional, called-out break.
- Keep files under roughly 500 lines when practical (test files may exceed this).

## Naming

- **Private fields:** `_camelCase`.
- **File-scoped namespaces** are used throughout.

## Visibility

- **Public API is minimal** — only types in the root `mvdmio.ASP.Jobs` namespace are public, plus `PostgresJobStorageConfiguration`.
- Non-public types live in `Internals/` and `Utils/` and are marked `internal`.
- Test projects get access via `InternalsVisibleTo`.

## Async / await

- All public APIs that do I/O are async with `CancellationToken` support.
- Use the `ct = default` pattern for optional cancellation tokens.

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
