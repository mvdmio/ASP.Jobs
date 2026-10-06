# Coding Standards

## Design

- Add an abstraction when its second caller appears.
- Keep source files under ~500 lines where practical; test files may run longer.

## Public API

- The public API — the root `mvdmio.ASP.Jobs` namespace plus `PostgresJobStorageConfiguration` — is a NuGet contract: keep its shape, or break it on purpose and call the break out.
- Change code in place; add a compat shim only when real consumers or persisted data need one.
- XML-doc every public member. Make every other type `internal`, under `Internals/` or `Utils/`.

## Domain language

- Use the terms `CONTEXT.md` defines for domain concepts, in code and prose alike; its _Avoid_ lines list the synonyms to replace. A concept it lacks is invented language (rename it) or a glossary gap (flag it for `/grill-with-docs`).
- Before changing an area, read the `docs/adr/` records that touch it. When your work contradicts one, say so instead of overriding it: _Contradicts ADR-0003 (retry is a storage reschedule) — but worth reopening because…_

## Code style

- Indent with 3 spaces.
- Use only APIs that `net8.0` has: the library builds for `net8.0`, `net9.0`, and `net10.0`.
- `src/` has implicit usings off: write every `using` a file needs, and only those.
- Name `CancellationToken` parameters `ct`, as `ct = default` on interface and public methods.

## Error handling

- Log every caught exception with the exception object, or rethrow it. Shutdown cancellation is the one silent catch: `catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)`, commented as expected.

## Tests

- Build on the helpers in the Unit project's `Utils/` and the Integration project's `Fixtures/` before writing new ones.
- Test Postgres code against a real database through the Integration project's `PostgresFixture` (Testcontainers).
- Iterate on the Unit project, filtering by class or method substring: `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit --filter "FullyQualifiedName~RetryPolicyTests"`
