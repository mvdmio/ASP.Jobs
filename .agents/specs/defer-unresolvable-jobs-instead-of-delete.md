# Spec — Defer unresolvable jobs instead of deleting them

Status: ready-for-agent

Source: Bugsink [COMPLIANCE-6](https://bugsink.mvdm.io/issues/issue/d4406128-4da2-4e8a-b3c2-79d0ae0f7493/) (triaged 2026-08-10)

## Problem Statement

Several Worker Instances of the same application share one Postgres job table. During a rolling deploy, a new instance can schedule a job whose type only exists in the new build. An old instance that is still draining can claim that job, fail to load the type, and **delete the row**. The work is gone. The new instance has already finished its boot enqueue path, so a one-off job may never run until someone restarts the app again.

Production hit this with Compliance's ADR-0070 one-off attestation heal. The old release logged that the job type "no longer exists" and deleted the row. The type still existed on the new release that had just scheduled it.

Operators need jobs that this process cannot load to stay available for a process that can load them. Permanent removal of a job type should not silently destroy work that a peer instance still owns.

## Solution

When Postgres storage claims a due job and cannot resolve the job type or the parameters type, it **releases the claim** and **defers** the job a short time instead of deleting it. It logs a clear warning. Other due jobs still run. A peer Worker Instance that can resolve the type can claim the job later.

Listing helpers that already skip unresolvable rows keep skipping them. They must not delete either.

No automatic permanent cleanup of orphan rows is added in this change. Orphans from truly removed types may sit until a human deletes them or a later cleanup lands.

## User Stories

1. As an operator rolling out a new build, I want a job scheduled by the new instance to survive contact with an old instance, so that one-off and ASAP work still runs after the cutover.
2. As an operator, I want the old instance to keep running other due jobs while it cannot load a new type, so that deploy overlap does not stall the whole queue.
3. As a developer who removed a job type, I want the library to stop deleting those rows on sight, so that a mistaken type string or a peer's job is not destroyed by an aggressive cleanup.
4. As a developer reading logs, I want the warning to say the job was deferred, not deleted, so that the message matches what happened.
5. As a library consumer with many Worker Instances, I want claim-and-fail behavior to leave the shared table consistent for the next claimer, so that distributed processing remains safe.
6. As a test author, I want an integration test that inserts an unresolvable job next to a resolvable one, so that we prove the resolvable job runs and the unresolvable job remains.
7. As a test author, I want the unresolvable job's claim fields cleared and its perform time pushed forward, so that the same instance does not spin forever on that row.
8. As an operator of in-memory test hosts, I want no behavior change there, because that storage already holds live type objects and never reloads them from strings.
9. As a suite owner, I want this fixed in the jobs library and then consumed via a package bump, so that all six apps get the safer claim path.
10. As an operator of Compliance after COMPLIANCE-6, I want a documented follow-up to check whether the attestation heal completed, so that production dual-open attestation cleanup is not left half-done.

## Implementation Decisions

- Change only the Postgres claim path that today deletes when type resolution fails after a successful claim. In-memory storage does not reload types from strings and needs no parallel change.
- After a failed type or parameters type resolution: clear the claim (`started_at` / `started_by`), set `perform_at` to a short deferral from the storage clock (about one to five minutes is enough to avoid a tight loop), log a warning that names job name, id, and type, and continue looking for another job in the same wait loop.
- Do not delete the row in that path. Remove or rewrite the log text that says the job has been deleted.
- Listing paths that filter unresolvable jobs keep filtering only. Align their warning wording with "could not be loaded" without implying deletion if they do not delete today.
- Keep using assembly-qualified names and `Type.GetType` for resolution. Do not introduce a registered-type allow-list in this change; deferral alone fixes the rolling-deploy loss without a new configuration surface.
- No schema migration. No new public API. No change to scheduling, finalization after a successful run, or culture capture.
- After the library ships a new package version, consumers bump the package reference. That consumer bump is outside this library Spec but is required for production effect.

## Testing Decisions

- Prefer external behavior over private helpers: schedule or insert through storage, then call `WaitForNextJobAsync` and assert table state.
- Integration test (Postgres): one resolvable due job and one unresolvable due job (bogus assembly-qualified type string written the same way production stores types). Expect the resolvable job returned; expect the unresolvable row still present with claim cleared and `perform_at` deferred.
- Integration test: only unresolvable due jobs present — `WaitForNextJobAsync` does not delete them; under a short cancellation token it returns null without emptying the table.
- Existing unit tests that prove `ToJobStoreItem` returns null for unresolvable types remain valid; do not change that contract.
- Do not assert exact log message strings if the suite rarely does; assert data outcomes first.
- Prior art: `PostgresStorageTests` for claim/finalize; `JobDataTests` for resolution null.

## Out of Scope

- Automatic garbage collection of jobs whose types stay unresolvable for days or weeks.
- Filtering the SQL claim query by a registry of `RegisterJob` types.
- Changing how job type strings are stored (assembly-qualified names stay).
- Re-running or verifying Compliance's attestation heal in production (operator follow-up; see Further Notes).
- Bumping `mvdmio.ASP.Jobs` inside the suite monorepo (follow-up after package publish).
- Muting or resolving the Bugsink issue from this Spec alone before production heal status is known.

## Further Notes

### Verified production timeline (COMPLIANCE-6)

| Fact | Value |
| --- | --- |
| Issue | COMPLIANCE-6 |
| Level | warning (Serilog → Bugsink) |
| Logger | Postgres job storage |
| Job type | Compliance attestation heal job (ADR-0070 one-off) |
| Event time | 2026-08-10 05:07:09 UTC |
| Event release | `5565720f…` — **does not contain** the heal job type |
| Next release registered | `92b43904…` at 2026-08-10 05:07:14 UTC — **does contain** the heal job type |
| Old process start | 2026-08-09 23:51:02 UTC (matches prior release) |

Reading: new instance scheduled the heal; old instance claimed it, failed `Type.GetType`, deleted the row.

### Operator follow-up (not this Spec)

1. On the Compliance database: check whether `compliance.one_off_runs` has a row for `attestation_heal_adr_0070`.
2. If missing, restart Compliance (boot re-enqueues while the one-off is incomplete) or run the heal service once by hand.
3. Confirm dual-open attestation cleanup for live policies/procedures after the heal.

### Why not delete-after-TTL in this Spec

A time-to-live cleanup is a product decision (how long, who audits). Deferral alone stops data loss during deploy overlap. Orphan cleanup can be a later enhancement.
