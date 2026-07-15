# Retry is a storage reschedule, not an in-process loop

A Job's Retry Policy (declared on the Job class, per exception type, catch-clause matching semantics) is executed by rescheduling the *same* storage row: `perform_at` is moved forward, a new `attempt` counter is bumped, and `started_at`/`started_by` are cleared. We rejected in-process retry loops (retries would die with the process, hold a concurrency slot during backoff, and betray what Postgres storage promises) and delete+insert (loses chain identity and is not atomic against concurrent same-name scheduling).

## Consequences

- One scheduled execution plus its retries form one **Execution Chain** (same `JobId`, same row). `OnJobFailedAsync` fires only when the chain definitively fails; `OnJobRetryAsync` fires per retried attempt. The chain outcome is determined solely by `ExecuteAsync` — runner-side lifecycle hooks are fire-safe observers and can never alter it (this deliberately removed the old behavior where an `OnJobExecutedAsync` throw invoked `OnJobFailedAsync`).
- **Supersession is silent by design.** A retry is only written if no pending job with the same `JobName` exists (enforced atomically by the partial unique index `idxu_jobs__job_name__not_started`). Newer scheduling intent wins; the abandoned chain fires no hooks, matching how named-job dedup already treats replaced jobs. Scheduling a job again is therefore the way to cancel a failing chain.
- **CRON occurrences wait for the chain.** The next occurrence is calculated only when the chain ends (success, budget depleted, non-retryable, superseded). Slots passed during a long retry chain are skipped, not backfilled — a job that has been failing for an hour should not receive a backlog.
- **Groups keep flowing.** A retrying job re-enters its group by its new `perform_at`; groups guarantee non-concurrency, not happens-before ordering across failures. Blocking a group on a poison job was rejected.
- `PerformNowAsync` ignores the Retry Policy: it is a direct invocation in the caller's context, not an Execution Chain, and the exception reaching the caller is the error handling.
- No jitter in v1; `ExecuteAsync` must be idempotent under retries (documented author responsibility).
