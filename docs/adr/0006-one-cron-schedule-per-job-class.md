# One CRON Schedule per Job class per application

A Job class has at most one **CRON Schedule** within one application. When an app schedules a class on a CRON, the library deletes every other CRON row of that class in the same application that is waiting to run. It finds them by the class's short name, which every CRON row name carries, and not by the stored assembly-qualified type, which changes with each assembly version. So changing the expression in code replaces the old schedule instead of adding a second one next to it. Before 5.0.0 the row's name included the expression, so a changed expression added a new row and the old one ran on forever. Nothing in the public API could remove it. Every app had to clean it up with a hand-written migration (mvdmio-suite#280).

## Considered options

- **Replace by default (chosen) or only when the caller asks.** Making replacement opt-in, through a flag on `PerformCronAsync`, would have shipped as a minor version and broken nobody. But it keeps the silent leftover as the default, and the leftover is what every caller hits by accident. One Job class running on two expressions in one app is rare, and almost any pair of times fits in one expression (`0 9,17 * * *`). So the breaking change ships as 5.0.0, and the README example that scheduled one class on seven expressions goes away.
- **Drop the expression from the row's name.** The name would then be `cron_<JobType>`, and the existing upsert would overwrite the expression on its own. But rows already stored under the old names would still be left behind once. A name change also does not help callers that scheduled one class on two expressions, so the replacement still needs a delete. Keeping the name and deleting the other rows does both jobs with one rule.

## Consequences

- **A rolling deploy can put an old row back.** A Worker Instance on the old build that is running the old schedule when the new build starts schedules its next occurrence after the new build has cleaned up. That row runs until the app schedules the class again, normally on its next deploy. The library does not guard against this. The cost is extra runs of a job the app already runs, until the next start.
- **Only CRON rows waiting to run are replaced.** A row that a Worker Instance has Claimed is left alone, and so are one-off jobs (now, ASAP, at a time) of the same class. Other applications that share the database keep their own CRON Schedule for a class with the same name.
