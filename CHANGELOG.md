# Changelog

## 2026-10-10: PostgreSQL storage now honours job groups
With PostgreSQL storage, jobs that share a `Group` now run one at a time across every worker process of the same application, in the order they are due, as in-memory storage already did; before, every due job in a group could run at once and take every job slot. The next job in a group starts as soon as the running one finishes, is rescheduled for a retry, or is released at shutdown, but if a worker process stops without shutting down cleanly while it runs a grouped job, the group waits until that process is found stopped, about five to six minutes after its last heartbeat. Idle worker processes now also check the database at least every 30 seconds even when nothing is due, and no longer check over and over without pause while another application's jobs are due.

## 2026-10-09: Samplers can tell jobs apart
The job runner now gives each job span its name, `Job: <JobType.Name>`, and its `job.*` tags at the moment the span starts, so an OpenTelemetry sampler can decide by `job.type`, for example to record only 1 in 100 runs of a busy job. The exported span keeps the same display name, tags, events and status, and a span the sampler drops still carries the name and tags for code that reads the current span; only the span's operation name changes, from `PerformJob` to `Job: <JobType.Name>`. With in-memory storage, `GetScheduledJobsAsync` now returns a copy of the scheduled jobs, as PostgreSQL storage already does, instead of a live list that could throw "Collection was modified" while jobs ran.

## 2026-10-09: Named job pool and error backoff
The job storage's PostgreSQL connections now show in `pg_stat_activity` and in database traces under the program's name plus `.Jobs`, for example `MyCompany.Web.Jobs`, and that pool is capped at 10 connections unless the connection string sets `Maximum Pool Size`, where before it could grow to 100. When fetching the next job fails, for example while PostgreSQL restarts or has run out of connection slots, the job runner now waits 1 second before it tries again and doubles the wait on each further error up to 30 seconds, where before it retried at once and logged an error on every pass. A successful fetch resets the wait, and shutting the app down cuts it short.

## 2026-10-08: Batch scheduling stores jobs together
Scheduling a list of jobs with `PerformAsapAsync` or `PerformAtAsync` now stores the whole list in one database write and wakes listening workers once, instead of once per job. A batch is now all-or-nothing: when any job in it fails to schedule, for example because its `OnJobScheduledAsync` hook throws, no job from that batch is stored, where before the jobs ahead of the failure stayed scheduled. Each job still runs its own scheduling hook and keeps its own name, culture and run time.

## 2026-09-15: Jobs keep the scheduling culture

ASAP and timed jobs now run under the culture of the thread that scheduled them, instead of the runner thread's ambient culture. Recurring CRON jobs now run under the invariant culture by default. You can pass an explicit culture when scheduling, and a job that schedules further jobs hands that culture down automatically.

## 2026-09-16: Jobs survive a rolling deploy
When a worker process meets a scheduled job whose class it cannot load — normally because it is running an older build partway through a deploy — it now leaves the job in place for five minutes instead of deleting it straight away, so a peer process on a newer build can pick it up. If nothing manages to run the job within that window, it is deleted and a single warning names it. Scheduling a job again under the same name reopens the window and updates the stored job class, which it previously did not.
