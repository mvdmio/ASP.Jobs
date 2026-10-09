# 01 — Pause the producer after a storage error

Status: built
Depends on: none

## What to build

When the job runner's producer loop fails to fetch the next job from storage (it catches the exception and logs "Error while fetching next job from storage"), it now waits before it tries again, instead of retrying at once. During a Postgres outage a host then makes a handful of attempts, not thousands. Postgres outages include a restart and `53300: sorry, too many clients already`.

The rule, fixed inside the library and not configurable:

- The loop counts consecutive errors. Each caught error, other than cancellation, adds one to the count, is logged once exactly as today, and is followed by a wait.
- A small pure function computes the wait from the count of consecutive errors, the current error included. The wait is 1 second times 2 to the power of (count minus 1), capped at 30 seconds:

  | consecutive errors | 1 | 2 | 3 | 4 | 5 | 6 | 7 | any higher count |
  |---|---|---|---|---|---|---|---|---|
  | wait | 1 s | 2 s | 4 s | 8 s | 16 s | 30 s | 30 s | 30 s |

  A long outage can push the count very high. The function must still return 30 s for any large count, up to `int.MaxValue`, with no overflow and no exception.
- When a fetch returns without an error, the count goes back to 0, so the next error waits 1 s again. This holds whether the fetch returned a job or `null`.
- The wait observes the stopping token. Stopping the runner during a wait ends it at once. The producer then leaves its loop the same way it does on any other cancellation: no error is logged and the channel writer is completed.
- No new log line per wait, and no new time seam. `IClock` only reads the time and stays as it is. The wait is a real delay.

Out of scope: the cleanup service, retries inside jobs, `MaxConcurrentJobs`, and the Claim and LISTEN/NOTIFY design.

The README gets a new short `## PostgreSQL storage` section. It names `UsePostgresStorage(applicationName, connectionString)` in one line and says that after a storage error the runner waits 1 s before it tries again, doubling the wait on each further consecutive error up to 30 s. A successful fetch resets the wait, and shutdown cuts it short. Step 02 adds the pool name to this same section.

## Footprint

Projects: `src/mvdmio.ASP.Jobs`, `test/mvdmio.ASP.Jobs.Tests.Unit`, `test/mvdmio.ASP.Jobs.Tests.Integration`

- `src/mvdmio.ASP.Jobs/Internals/JobRunnerService.cs` — `ProduceJobsAsync` (the catch that logs "Error while fetching next job from storage"), plus the new internal pure wait function. It can live here as an `internal static` member or in a small new internal file under `src/mvdmio.ASP.Jobs/Internals/`.
- `test/mvdmio.ASP.Jobs.Tests.Unit/` — a new test class for the wait function and the loop, next to `JobRunnerServiceTests.cs`. For the storage stand-in, use an NSubstitute `IJobStorage` registered in a `ServiceCollection`, then construct `JobRunnerService` directly, as `JobRunnerHarness` does.
- `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/JobRunnerHarness.cs` — only if letting it take a stand-in `IJobStorage` is cleaner than building the runner in the test.
- `src/mvdmio.ASP.Jobs/Internals/Storage/Interfaces/IJobStorage.cs` — `WaitForNextJobAsync`. Read only: this is what the stand-in throws from.
- `README.md` — the new `## PostgreSQL storage` section.

## Acceptance criteria

- [ ] A unit test pins the wait function exactly: counts 1 to 7 give 1, 2, 4, 8, 16, 30 and 30 seconds. Count 1 gives 1 s again, which is the wait after a reset. A very large count, `int.MaxValue` included, gives 30 s without throwing.
- [ ] A loop test runs the runner against a stand-in whose fetch always throws, for a few seconds (about 3.5 s). It shows only a handful of fetch attempts (about 3, at 0 s, 1 s and 3 s), not hundreds.
- [ ] A loop test shows that a success resets the wait. The stand-in throws twice, returns `null` once, then keeps throwing. The gap after the first error following the success is about 1 s, well below the 4 s it would be without the reset.
- [ ] A shutdown test stops the runner during a backoff wait. `StopAsync` returns well before that wait would have ended, and no error is logged for the cancelled wait.
- [ ] Each fetch error is still logged once with the existing message, and the wait adds no other log line.
- [ ] The existing `JobRunnerServiceTests`, `JobRunnerConcurrencyTests` and `JobRunnerRetryTests` still pass unchanged.
- [ ] The README has the `## PostgreSQL storage` section with the backoff note.
- [ ] `dotnet build`, then `dotnet test`, run one after the other, pass for every project on the `Projects:` line. The integration tests need Docker.

## Outcome

- `JobRunnerService.ProduceJobsAsync` counts consecutive fetch errors (saturating at `int.MaxValue`), resets the count after any fetch that returns, and after each logged error awaits `Task.Delay(GetStorageErrorBackoff(count), ct)`; a cancelled delay breaks the loop like any other cancellation (writer completed, nothing logged).
- The pure wait function is `internal static TimeSpan JobRunnerService.GetStorageErrorBackoff(int consecutiveErrors)` in `src/mvdmio.ASP.Jobs/Internals/JobRunnerService.cs`; it throws `ArgumentOutOfRangeException` for counts below 1.
- Tests: `test/mvdmio.ASP.Jobs.Tests.Unit/JobRunnerStorageErrorBackoffTests.cs` (wait sequence, handful of attempts in 3.5 s, reset after success, prompt stop during a wait). New test helper `test/mvdmio.ASP.Jobs.Tests.Unit/Utils/RecordingLogger.cs` records log entries. `JobRunnerHarness` is unchanged.
- README: new `## PostgreSQL storage` section, placed after `## Initialization`; Step 02 adds the pool name there.
- Footprint drift: none.

Safety fact: after a fetch error the producer waits 1, 2, 4 … 30 s before the next fetch, resets after a successful fetch, and a stop during the wait returns at once; if false, a Postgres outage turns back into a tight loop of connection attempts and error logs, or shutdown hangs for up to 30 s (rung 3)
Proof: `dotnet test test/mvdmio.ASP.Jobs.Tests.Unit/mvdmio.ASP.Jobs.Tests.Unit.csproj --filter "FullyQualifiedName~JobRunnerStorageErrorBackoffTests"` exit 0 — Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12
Merge risk: easy — reverting the commit restores the immediate retry; nothing persisted or published; affects hosts whose storage fails (job pickup resumes up to 30 s after recovery)
