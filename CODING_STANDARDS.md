# Coding Standards

## Design

- Add an abstraction when its second caller appears.
- Prefer explicit domain types over primitive bags; use `required` init properties where the project already does.
- Keep files under roughly 500 lines when practical; test files may run longer.

## Public API

- The library ships to NuGet, so its public API is a contract: keep its shape, or make the break intentional and call it out.
- Public surface: the types in the root `mvdmio.ASP.Jobs` namespace, plus `PostgresJobStorageConfiguration`. Every other type is `internal` and lives in `Internals/` or `Utils/`; test projects reach it through `InternalsVisibleTo`.
- Document every public member with XML docs (`GenerateDocumentationFile` is on).
- Change code in place. Add a backward-compat shim only for real consumers or persisted data.

## Domain language

- Name domain concepts — in types, members, test names, Issue titles, proposals — with the terms `CONTEXT.md` defines; its _Avoid_ lines name the synonyms to replace.
- A concept missing from `CONTEXT.md` is a signal: either the language is invented (reconsider it) or the glossary has a real gap (note it for `/grill-with-docs`).
- When your work contradicts an ADR in `docs/adr/`, say so explicitly instead of overriding it:

  > _Contradicts ADR-0003 (retry is a storage reschedule) — but worth reopening because…_

## Code style

- Write code that compiles for every target: the library builds for `net8.0`, `net9.0`, and `net10.0` (`LangVersion=latest`).
- Nullable reference types are on; respect the annotations.
- Implicit usings are off in `src/` (on in test projects): write each `using` the file needs, and only those.
- Private fields: `_camelCase`. Namespaces: file-scoped.
- Access modifiers: always explicit.

## Async

- Public APIs that do I/O are async and take a `CancellationToken`, optional as `ct = default`.

## Error handling

- Surface every caught exception: rethrow it, or log it with the exception object (`_logger.LogError(ex, …)`) so the real exception text survives.
- Shutdown cancellation is the one deliberate exception: catch it with `when (ex is TaskCanceledException or OperationCanceledException)` and comment that it is expected.

## Tests

- xUnit v3 with `AwesomeAssertions`; unit tests mock with `NSubstitute`.
- Build on the existing test utilities and fixtures (`.agents/refs/testing.md` lists them) before writing new helpers.
- Test database code against real PostgreSQL through `Testcontainers.PostgreSql`.
- A new storage backend gets integration tests built on the existing fixtures.

## Postgres migrations

- Add one class per schema change in `src/mvdmio.ASP.Jobs/Internals/Storage/Postgres/Migrations/`, implementing `IDbMigration` (the `mvdmio.Database.PgSQL.Migrations` framework).
- Name the class and file `_YYYYMMDDHHMM_DescriptiveName`, set `Identifier` to the same timestamp, and implement `UpAsync`.
