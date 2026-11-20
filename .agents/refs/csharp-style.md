# C# style

Rules for C# in every repo. A repo's own conventions add to these and win where they differ.

## Formatting

- Indent 3 spaces. LF line endings; final newline.
- File-scoped namespaces (`namespace Foo;`) that follow the folder structure.

## Naming

| Element | Convention | Example |
|---|---|---|
| Types, methods, properties | PascalCase | `DatabaseConnection` |
| Private fields | `_camelCase` | `_connection` |
| Parameters, locals | camelCase | `connectionString` |
| Constants | UPPER_SNAKE_CASE | `DEFAULT_TIMEOUT` |
| Interfaces | `I` prefix | `IDbMigration` |
| Generic parameters | `T` prefix | `TEntity` |
| Async methods | `Async` suffix | `ExecuteAsync` |

## Code

- `var` when the type is apparent from the right-hand side.
- Braces for multi-line statements; prefer pattern matching, `?.` and `??`.
- Nullable reference types on: settle nullability with guards, pattern matching, or `?`. Use `!` only where a preceding check or an API contract guarantees non-null out of the compiler's sight.
- Access modifiers always explicit.
- Keep using directives minimal: no duplicates, no dead imports.

## Public API

- XML doc comments on every public member of the shipped library project: `<summary>`, `<param>`, `<typeparam>`, `<returns>` as they apply. `<inheritdoc />` on implementations of documented interface members. No `///` comment anywhere else.
- Public async APIs take an optional `CancellationToken ct = default`.

## Errors

- Log or propagate every caught exception; a silent catch hides the failure.
- Quote the real exception text when reporting or logging a failure.
