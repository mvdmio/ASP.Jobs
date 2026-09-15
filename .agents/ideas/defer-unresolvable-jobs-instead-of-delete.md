# Spec — Defer unresolvable jobs instead of deleting them

**Status:** wontfix
**Spec:** `.agents/specs/resolution-grace-for-unresolvable-jobs.md`

Superseded. This Idea deferred Unresolvable Jobs indefinitely, which stopped the data loss but left a row and a repeating warning for every job class anybody deletes. The Spec keeps the deferral and closes it with a five-minute Resolution Grace, after which the job is deleted. See `docs/adr/0005-unresolvable-jobs-are-deferred-then-deleted-on-a-clock.md`.

Source: Bugsink [COMPLIANCE-6](https://bugsink.mvdm.io/issues/issue/d4406128-4da2-4e8a-b3c2-79d0ae0f7493/) (triaged 2026-08-10)

## Problem Statement

Several Worker Instances of the same application share one Postgres job table. During a rolling deploy, a new instance can schedule a job whose type only exists in the new build. An old instance that is still draining can claim that job, fail to load the type, and **delete the row**. The work is gone. The new instance has already finished its boot enqueue path, so a one-off job may never run until someone restarts the app again.

Production hit this with Compliance's ADR-0070 one-off attestation heal. The old release logged that the job type "no longer exists" and deleted the row. The type still existed on the new release that had just scheduled it.

Operators need jobs that this process cannot load to stay available for a process that can load them. Permanent removal of a job type should not silently destroy work that a peer instance still owns.

## Solution

When Postgres storage claims a due job and cannot resolve the job type or the parameters type, it **releases the claim** and **defers** the job a short time instead of deleting it. It logs a clear warning. Other due jobs still run. A peer Worker Instance that can resolve the type can claim the job later.

One case still deletes. A claimed row can only go back to `started_at IS NULL` if no other pending row shares its `(application_name, job_name)`. When one does exist, that pending row is the newer schedule and carries the same work, so the claimed row is superseded and is deleted. No work is lost: the replacement is already waiting.

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
10. As an operator whose new instance re-enqueued a job while an old instance held the claim, I want the stale claimed row dropped rather than released, so that the table keeps one pending row per job name and the newer schedule wins.
11. As an operator of Compliance after COMPLIANCE-6, I want a documented follow-up to check whether the attestation heal completed, so that production dual-open attestation cleanup is not left half-done.

## Implementation Decisions

- Change only the Postgres claim path that today deletes when type resolution fails after a successful claim. In-memory storage does not reload types from strings and needs no parallel change.
- After a failed type or parameters type resolution, in one statement: clear the claim (`started_at` / `started_by`) and set `perform_at = now + LEAST(GREATEST(now - created_at, INTERVAL '1 minute'), INTERVAL '1 hour')`, taking `now` from the storage clock. The first deferral is one minute, each further deferral roughly doubles, and the wait caps at one hour. A peer that can load the type picks the job up within a minute or two, while a type that is genuinely gone settles at one claim per hour per instance instead of one per minute.
- Guard the release against the partial unique index `idxu_jobs__application__job_name__not_started`. Add a `NOT EXISTS` clause for another pending row with the same `application_name` and `job_name`, exactly as `TryScheduleRetryAsync` does. When the guard matches no row, delete the claimed row instead and log that it was superseded. Also catch the `PostgresErrorCodes.UniqueViolation` that arrives as the `InnerException` of a `QueryException`, for the insert that lands between the check and the commit, and treat it the same way.
- Log a warning that names the job name, the job id, the type string, and the new `perform_at`. Then continue looking for another job in the same wait loop.
- Do not delete the row when the claim can simply be released. Deletion is reserved for the superseded case above. Rewrite the log text so it no longer says the job has been deleted.
- A deferral is not a failed run. Leave `attempt` unchanged, so a job that waits for a peer keeps its full retry budget. Do not route the deferral through `TryScheduleRetryAsync`: that path increments `attempt` and belongs to the retry chain.
- Listing paths (`FilterResolvableJobs`) keep filtering and still never delete. Change the phrase "the type no longer exists" in both that warning and the claim-path warning. The type may exist on a peer instance running a different build, which is the case this Spec is about. Say "could not be loaded in this process" instead.
- Keep using assembly-qualified names and `Type.GetType` for resolution. Do not introduce a registered-type allow-list in this change; deferral alone fixes the rolling-deploy loss without a new configuration surface.
- No schema migration. No new public API. No change to scheduling, finalization after a successful run, or culture capture.
- After the library ships a new package version, consumers bump the package reference. That consumer bump is outside this library Spec but is required for production effect.

## Testing Decisions

- Write the unresolvable row with raw SQL through `PostgresFixture.DatabaseConnection`, the way `PostgresStorageTests` already writes rows directly. `ScheduleJobAsync` takes a live `Type` and cannot express an unresolvable job. Write the resolvable row through storage, then assert on table state after calling `WaitForNextJobAsync`.
- Integration test (Postgres): one unresolvable due job and one resolvable due job. Give the unresolvable row the earlier `perform_at`, because the claim query is `ORDER BY perform_at, created_at LIMIT 1` and would otherwise never claim it. Expect the resolvable job returned; expect the unresolvable row still present, with `started_at` and `started_by` null and `perform_at` one minute past the test clock.
- Integration test (Postgres): an unresolvable claimed row plus a second pending row with the same `application_name` and `job_name`. Expect `WaitForNextJobAsync` to delete the unresolvable row rather than release it, and expect the pending row untouched. This is the rolling-deploy shape, where the new instance re-enqueues at boot while the old instance holds the claim.
- Integration test: only unresolvable due jobs present — `WaitForNextJobAsync` does not delete them; under a short cancellation token it returns null without emptying the table.
- Existing unit tests that prove `ToJobStoreItem` returns null for unresolvable types remain valid; do not change that contract.
- Do not assert log message strings. `PostgresStorageHarness` builds the storage with `NullLoggerFactory.Instance`, so no log output reaches a test. Assert table state and return values.
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

Two events, same issue, same claim-then-delete path. The type in each event existed only on the **new** release.

| Fact | 2026-08-10 | 2026-09-07 |
| --- | --- | --- |
| Issue | COMPLIANCE-6 | COMPLIANCE-6 (event 2) |
| Level | warning (Serilog → Bugsink) | warning |
| Logger | Postgres job storage | Postgres job storage |
| Job type | `AttestationHealJob` (ADR-0070 one-off) | `ArchiveAttestationHealJob` (ADR-0101 one-off) |
| Event time | 2026-08-10 05:07:09 UTC | 2026-09-07 07:21:40 UTC |
| Event release | `5565720f…` — **does not contain** the type | `54774846…` — **does not contain** the type (process up since 2026-09-05 09:15) |
| Type added | later the same morning | `ef26978ba` 2026-09-07 00:26 UTC |

Reading: new instance scheduled the one-off; old instance claimed it, failed `Type.GetType`, deleted the row.

Checked on 2026-09-07 against the live Compliance database:

- `attestation_heal_adr_0070` completed 2026-08-11 (the first event self-healed on a later boot).
- `attestation_heal_archive_cancels_live` is **still missing**. No `ArchiveAttestationHealJob` row remains in `mvdmio.jobs`.
- Leftover Open attestation on already-archived documents: 3 Policy Approval + 21 Policy Awareness on 1 archived Policy; 2 archived Procedures have none.

### Operator follow-up (not this Spec)

1. Restart production Compliance (boot re-enqueues while `attestation_heal_archive_cancels_live` is absent) or run `AttestationHealService.HealArchivedAttestationAsync` once.
2. Confirm `compliance.one_off_runs` then has `attestation_heal_archive_cancels_live`, and the leftover Open Approval/Awareness on the archived Policy are Cancelled.
3. After this library Spec ships, bump `mvdmio.ASP.Jobs` in the suite. Do not mute COMPLIANCE-6 until that bump is on production.

### Accepted cost: the warning repeats

Today a removed job type logs one warning and the row disappears. After this change the row stays, so every instance re-claims it and logs again on each deferral. The backoff in the Implementation Decisions caps this at about one warning per hour per instance, plus the first few minutes of faster attempts. Expect a slow trickle in Bugsink for any type that is truly gone. That trickle is the signal that a human needs to delete the row, and it stops when they do.

### Why not delete-after-TTL in this Spec

A time-to-live cleanup is a product decision (how long, who audits). Deferral alone stops data loss during deploy overlap. Orphan cleanup can be a later enhancement.
