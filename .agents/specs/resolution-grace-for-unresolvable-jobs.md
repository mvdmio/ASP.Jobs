# Spec — Resolution Grace for Unresolvable Jobs

**Status:** ready-for-agent

Replaces `.agents/ideas/defer-unresolvable-jobs-instead-of-delete.md`. Decision recorded in `docs/adr/0005-unresolvable-jobs-are-deferred-then-deleted-on-a-clock.md`. Terms used here are defined in `CONTEXT.md`: **Unresolvable Job**, **Resolution Grace**, **Claim**, **Worker Instance**, **Retry Budget**.

Source incident: Bugsink COMPLIANCE-6, events on 2026-08-10 and 2026-09-07.

## Problem Statement

Several Worker Instances of one application share a single Postgres job table. During a rolling deploy, a new instance schedules a job whose job class exists only in the new build. An old instance that is still draining Claims that job, cannot load the class from the type name stored with it, and deletes the row. The work is gone. The new instance has already finished its boot enqueue path, so a one-off job may never run again until somebody restarts the application.

This destroyed real work twice in production. A one-off attestation heal was scheduled by a new release, deleted by an old one, and never ran.

The obvious fix is to stop deleting and let the job wait. That fix has its own cost, and the cost is why the earlier Idea was rejected. A job class that a developer genuinely removes leaves a row that no build can ever run. If the library only ever defers, that row stays forever, and every Worker Instance re-Claims it and logs a warning about it for the life of the table. One kind of permanent damage is traded for another.

Operators need both ends fixed. A job must survive contact with an instance that cannot load it. A job that no live build can run must eventually go away on its own.

## Solution

A Worker Instance that Claims a due job and cannot load its job class or its parameters class stops deleting the row. Instead it stamps the row with the moment the job was first found Unresolvable, releases the Claim without moving the job's scheduled time, and remembers the job in a short-lived list so that it does not Claim the same job again straight away. It logs at Debug and carries on looking for other work.

The job is now due and unclaimed, exactly where it was in the queue. A Worker Instance running a build that can load the class Claims it and runs it, with no delay added by the instance that could not.

The stamp opens the **Resolution Grace**: a fixed window of five minutes in which the job may find an instance able to run it. Five minutes is chosen because that is the scale of a rolling deploy. It is long enough for a draining instance to disappear and a new one to pick the job up, and short enough that a job class somebody deleted does not leave litter behind.

When the window closes and the job is still Unresolvable, the Worker Instance that next Claims it confirms it still cannot load the class, deletes the row, and logs one warning. That warning names a real problem an operator can act on, and it is the only warning this design produces.

Rolling a deployment *back* to a build that lacks a job class is not a case this library protects. Rolling forward is.

## User Stories

1. As an operator rolling out a new build, I want a job scheduled by the new instance to survive contact with an old instance, so that one-off and ASAP work still runs after the cutover.
2. As an operator, I want the old instance to keep running every other due job while it cannot load one job class, so that deploy overlap never stalls the queue.
3. As an operator, I want a job that an old instance could not load to be picked up by a capable instance immediately, so that a deploy does not add delay to ASAP work.
4. As an operator, I want the old instance to touch the deferred job once and then leave it alone, so that a draining instance cannot keep taking the job away from the instance that can run it.
5. As a developer who removed a job class, I want the rows for that class to disappear on their own within minutes, so that the table does not accumulate work nobody can do.
6. As a developer reading logs, I want a deferral to be quiet, because deploy overlap is normal and fixes itself.
7. As a developer reading logs, I want exactly one warning when a job is deleted, naming the job name, the job id, the type name, and how long the job stayed unloadable, so that I know which job class to clean up after.
8. As an operator who watched the old behaviour destroy an attestation heal, I want the log line to describe what actually happened, so that "the type no longer exists" no longer appears when the type exists on a peer instance.
9. As a developer who points an existing job name at a different job class, I want the new class to be the one that runs, so that re-scheduling a name actually changes what it does.
10. As a developer who re-schedules a job under a name that was already found Unresolvable, I want the window to start again from scratch, so that a fresh schedule is not deleted because of an old stamp.
11. As an operator whose job failed and is waiting on a retry, I want a stamp left over from an earlier deploy not to get the job deleted, so that work a live build can run is never destroyed by stale bookkeeping.
12. As a developer relying on the Retry Policy, I want a deferral to leave the attempt count alone, so that a job that waits for a peer keeps its full Retry Budget.
13. As an operator whose new instance re-enqueued a job while an old instance held the Claim, I want the stale claimed row dropped rather than released, so that the table keeps one pending row per job name and the newer schedule wins.
14. As a library consumer with many Worker Instances, I want a failed Claim to leave the shared table consistent for the next Claimer, so that distributed processing stays safe.
15. As an operator whose instance restarts mid-window, I want the restarted instance to behave correctly from the row alone, so that losing the in-memory list changes nothing.
16. As an operator running CRON jobs, I want nothing about recurrence to change, so that occurrences keep being scheduled as they are today.
17. As an operator of in-memory test hosts, I want no behaviour change there, because that storage holds live type objects and can never have an Unresolvable Job.
18. As a library consumer, I want no new configuration and no new public method, so that upgrading is a package bump and nothing else.
19. As a library consumer on an existing database, I want the upgrade to migrate itself, so that no manual schema work is needed.
20. As a developer listing scheduled jobs, I want rows I cannot load to keep being skipped and never deleted by the listing, so that reading the queue never changes it.
21. As a test author, I want to prove that a resolvable job still runs when an unresolvable job sits ahead of it in the queue.
22. As a test author, I want to prove that the unresolvable job's Claim is cleared and its scheduled time is left untouched, so that the capable instance sees it as due.
23. As a test author, I want to prove that the same instance does not Claim the skipped job again within the window, so that it cannot spin.
24. As a test author, I want to prove that the job is deleted once the window has closed and only then.
25. As a test author, I want to drive the five minutes through the storage clock rather than by waiting, so that the suite stays fast.
26. As a suite owner, I want the fix to live in the jobs library rather than in each application, so that every application gets the safer Claim path from one place.

## Implementation Decisions

### Schema

- One new column on the jobs table: `unresolvable_since`, a nullable timestamp with time zone. It records the moment a Worker Instance first found the job Unresolvable. It is the only clock this design reads.
- One new migration, following the existing pattern: a single file under the Postgres migrations namespace implementing the migration interface, with an identifier matching its filename timestamp. Migrations are discovered by reflection, so there is no list to edit.
- No other schema change. No new index; the column is only ever read from a row already fetched by id or already being updated.

### The Claim path

The Postgres storage Claim loop changes as follows.

- The Claim query gains one clause in its inner `SELECT`, excluding the job ids currently in this instance's skip list. Ordering, the application filter, the due filter, and `FOR UPDATE SKIP LOCKED` are unchanged.
- After a successful Claim, the instance tries to load the job class and the parameters class, as it does today.
- **Both load, and the row carries no stamp:** unchanged. The job runs.
- **Both load, and the row carries a stamp:** the instance clears `unresolvable_since` on that row, then runs the job. This is not an optimisation; see "A stale stamp is never trusted" below.
- **Either fails to load, and the row carries no stamp or a stamp newer than five minutes:** the instance defers the job. It releases the Claim and writes the stamp in one statement, adds the job id to its skip list, logs at Debug, and continues the wait loop looking for another job.
- **Either fails to load, and the row carries a stamp five minutes old or older:** the instance deletes the row, logs one warning, and continues the wait loop.

The deferral statement clears `started_at` and `started_by`, sets `unresolvable_since` to the storage clock's current time only when it is currently null, and leaves `perform_at` alone. Leaving the scheduled time alone is the point: the job stays exactly where it was in the queue, so an instance that can run it takes it with no added delay.

### Guarding the release

- The release carries the same `NOT EXISTS` guard the retry reschedule already uses: it is refused when another pending row already holds the same application name and job name. The partial unique index on those two columns for unstarted rows forbids two.
- When the guard matches no row, that other pending row is the newer schedule and carries the same work. The Claimed row is superseded, so the instance deletes it and logs that it was superseded rather than deferred.
- A unique violation arriving concurrently — an insert landing between the check and the commit — is caught and treated the same way. The driver wraps it, so the Postgres error is read from the inner exception, exactly as the retry path does.

### The skip list

- A set of job ids held in the Postgres storage object, each entry expiring after five minutes.
- Its only purpose is to stop the same instance Claiming a job it just failed to load, in a tight loop. Without it the instance would take the same earliest-due job on every pass.
- The entry expiring is what causes the deletion. Once it expires, the instance Claims the job again, confirms it still cannot load the class, finds the stamp now old enough, and deletes. So the skip expiry and the Resolution Grace are deliberately the same five minutes.
- The set lives and dies with the process. A restarted instance simply Claims the job again; if the stamp is already old enough it deletes on the spot, and if it is not, it defers as before. Nothing about correctness depends on the set surviving.
- Entries are removed when they expire, and when their job is deleted, so the set cannot grow without bound.

### A stale stamp is never trusted

A stamp on its own never causes a deletion. Deletion always happens at a Claim, where the instance has just re-confirmed that it cannot load the class.

The stamp is also cleared whenever the job proves runnable:

- A Claim that loads both classes clears the stamp before running the job.
- The retry reschedule clears the stamp in its `SET` list.

Without this, a job stamped by an old instance, run by a new one, and put back onto a long retry backoff would carry a stamp far older than the window. The next Claim by an old instance would delete it immediately, with no grace at all — destroying work a live build can run.

### The scheduling upsert

The scheduling insert's `ON CONFLICT ... DO UPDATE` list must be extended. Today it sets only the id, the parameters, both cultures, the scheduled time, and the attempt count.

- Add `unresolvable_since`, set to null, so that re-scheduling a job name reopens the window from the start.
- Add `job_type`, `parameters_type`, `cron_expression`, and `job_group`.

The second group closes a pre-existing bug that is independent of this feature and must be called out in the changelog rather than slipped in. Because `job_type` was never in the update list, re-scheduling a job name onto an existing pending row kept the **old** job class. Pointing an existing job name at a different class silently kept running the old one.

### Logging

- Deferral logs at Debug. Deploy overlap is normal and self-correcting, and the warning trickle it would otherwise produce is the cost this Spec exists to remove.
- Deletion after the window closes logs one warning, naming the job name, the job id, the stored type name, and how long the job stayed unloadable.
- Supersession logs at Information, matching what the retry path already does for the same situation.
- The phrase "the type no longer exists" is removed everywhere it appears. The type may well exist, on a peer instance running a different build — that is the whole case this Spec is about. The replacement wording says the type could not be loaded in this process.

### Deliberate non-changes

- Five minutes is a constant in the library. There is no configuration option, because this is a published package whose public API is a contract.
- No registry of the job types a Worker Instance can run, and no filtering of the Claim query by registered types. The five-minute window removes the need, and such a registry could not be built accurately: a job registered in the container directly, rather than through the library's registration helper, runs today and would be stranded by registry filtering.
- Assembly-qualified type names stay the stored form.
- In-memory storage is untouched. It holds live type objects and never reloads them from a string, so it can never have an Unresolvable Job.
- The listing helpers keep skipping rows they cannot load and still never delete. Only their wording changes.
- The periodic cleanup service is untouched. Its heartbeat and its stale-instance sweep keep working as they do.
- No change to scheduling semantics, to finalization after a successful run, to CRON recurrence, or to culture capture.
- A deferral is not a failed attempt. The attempt count is untouched and a deferral never routes through the retry reschedule, so a job waiting for a peer keeps its full Retry Budget.

### Clock

Every time this feature reads or writes comes from the storage clock the Claim query already uses, never from the system clock directly. This is what lets tests move the five minutes without waiting.

## Testing Decisions

A good test here asserts what an operator could observe: which job the storage hands back, and what the jobs table holds afterwards. It does not reach into the skip list, the SQL, or the private methods.

### Seam

One seam, and it already exists: the Postgres storage object driven through the integration fixture, the same way the existing Postgres storage tests drive it. Every case below is expressed as "set the table up, call the storage, assert on the return value and the table". No new seam is introduced.

An unresolvable row cannot be created through the scheduling API, because that API takes a live type and every live type resolves. Such rows are written with raw SQL through the fixture's database connection, which is what the existing Postgres storage tests already do for rows they need to shape directly. Resolvable rows are written through the storage itself.

Time is moved by the storage clock, never by sleeping.

### Cases

1. An unresolvable due job and a resolvable due job, the unresolvable one scheduled earlier so that the Claim query reaches it first. The resolvable job is returned. The unresolvable row is still there, with its Claim fields cleared, its scheduled time unchanged, and a stamp set to the test clock.
2. The same instance called again inside the window does not return the skipped row and does not re-Claim it. Its Claim fields stay null.
3. A row whose stamp is already five minutes old or older is deleted when Claimed, and the call goes on to return another due job if there is one.
4. A stamped row whose classes do load has its stamp cleared and is returned for execution.
5. An unresolvable Claimed row beside a second pending row with the same application name and job name. The unresolvable row is deleted rather than released, and the pending row is untouched. This is the rolling-deploy shape, where the new instance re-enqueues at boot while the old instance holds the Claim.
6. Only unresolvable due jobs present, all inside the window. Nothing is deleted, and the call returns null under a short cancellation token without emptying the table.
7. Re-scheduling a job under a name whose pending row carries a stamp clears the stamp, and updates the stored job class, parameters class, CRON expression, and group. This covers both the window reopening and the pre-existing job class bug.
8. The listing helpers still skip rows they cannot load and leave every one of them in the table.

### Prior art

The existing Postgres storage tests for Claim and finalize behaviour, and the existing unit tests proving that a row with an unloadable type converts to null. That null-conversion contract does not change.

### What is not asserted

Log message text. The integration harness builds the storage with a null logger factory, so no log output reaches a test. Assertions are on table state and return values.

## Out of Scope

- Cleaning up an Unresolvable Job that never comes due. Resolution Grace opens at Claim, and a job is only Claimed once due, so a row scheduled months ahead whose class is deleted next week rests in the table until it comes due. Filed as `.agents/ideas/sweep-never-due-unresolvable-jobs.md`.
- A registry of the job types each Worker Instance can run, and any Claim-query filtering based on it.
- Making the five-minute window configurable.
- Changing how job type names are stored.
- Protecting a deployment that rolls **back** to a build lacking a job class.
- Re-running or verifying the Compliance attestation heal in production. That is operator follow-up; see Further Notes.
- Bumping the package inside the consuming monorepo. That follows the package publish.
- Muting or resolving the Bugsink issue before the production heal status is known.

## Further Notes

### Verified production timeline (COMPLIANCE-6)

Two events, the same issue, the same Claim-then-delete path. The job class in each event existed only on the **new** release.

| Fact | 2026-08-10 | 2026-09-07 |
| --- | --- | --- |
| Level | warning | warning |
| Logger | Postgres job storage | Postgres job storage |
| Job class | `AttestationHealJob` (one-off) | `ArchiveAttestationHealJob` (one-off) |
| Event time | 2026-08-10 05:07:09 UTC | 2026-09-07 07:21:40 UTC |
| Event release | does not contain the class | does not contain the class (process up since 2026-09-05 09:15) |
| Class added | later the same morning | 2026-09-07 00:26 UTC |

Reading: the new instance scheduled the one-off, the old instance Claimed it, failed to load the class, and deleted the row.

Checked on 2026-09-07 against the live Compliance database: the first heal completed on 2026-08-11, having healed itself on a later boot. The second is still missing, with no row left in the jobs table. Left behind: three Policy Approval and twenty-one Policy Awareness attestations still open on one archived Policy.

### Operator follow-up, not this Spec

1. Restart production Compliance so that the boot enqueue path runs again while the heal is absent, or run the heal service once by hand.
2. Confirm the heal is recorded, and that the leftover open attestations on the archived Policy are cancelled.
3. After this library ships, bump the package in the consuming suite. Do not mute COMPLIANCE-6 until that bump is in production.

### What replaced the earlier Idea, and why

The earlier Idea deferred Unresolvable Jobs indefinitely, with a backoff doubling from one minute to one hour, and accepted a permanent warning trickle for any job class truly removed. That accepted cost was rejected: it swaps one permanent problem for another, and leaves the table growing by one row for every job class anybody ever deletes.

The alternative considered and rejected in its place was a registry of job types published by each Worker Instance, so that deletion could wait for proof rather than a clock. It is recorded in ADR-0005 along with why the clock won.
