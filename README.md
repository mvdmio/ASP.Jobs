# mvdmio.ASP.Jobs

A library for scheduling jobs in ASP.NET Core applications.

## Usage

1. Add the NuGet package to your project:

```bash
dotnet add package mvdmio.ASP.Jobs
```

2. Register the job scheduler and job runner in your `Startup.cs`:

```csharp
public void ConfigureServices(IServiceCollection services)
{
   services.AddJobs();
   -- OR --
   services.AddJobScheduler();
   services.AddJobRunner();
}
```

3. Create a job by implementing the `Job` abstract class:

```csharp
public class MyJobParameters
{
   public string Parameter { get; set; }
}

public class MyJob : Job<MyJobParameters>
{
    public async Task ExecuteAsync(MyJobParameters parameters, CancellationToken cancellationToken)
    {
        // Your job logic here
    }
}
```

4. Register the job in your DI container:

```csharp
public void ConfigureServices(IServiceCollection services)
{
   services.RegisterJob<MyJob>();
}
```

5. Schedule the job in your application:

```csharp
public void MyController : Controller
{
   private readonly IJobScheduler _jobScheduler;

   public MyController(IJobScheduler jobScheduler)
   {
      _jobScheduler = jobScheduler;
   }

   public async Task<IActionResult> ScheduleJob()
   {
      await _jobScheduler.PerformNowAsync<MyJob, MyJobParameters>(new MyJobParameters());  // Runs the job immediately and waits for completion.
      await _jobScheduler.PerformAsapAsync<MyJob, MyJobParameters>(new MyJobParameters()); // Runs the job on a separate thread as soon as a slot becomes available.
      await _jobScheduler.PerformAtAsync<MyJob, MyJobParameters>(DateTime.Now, new MyJobParameters());  // Runs the job on a separate thread at the given time.

      return Ok();
   }
}
```

## Initialization

The library prepares its storage (for PostgreSQL, by running database migrations) through a single **Initialization**
step. In ASP.NET hosts this runs automatically at host start, before any job is scheduled or any worker registers, so
**you normally do not need to do anything**.

If you use the library outside an ASP.NET host (console app, worker, tests), or you schedule jobs *before* the host has
started, call `InitializeJobsAsync()` on your `IServiceProvider` first:

```csharp
await serviceProvider.InitializeJobsAsync();
```

Scheduling a job before Initialization has completed throws `JobStorageNotInitializedException` with an actionable
message — storage is never migrated lazily as a side effect of scheduling.

## Scheduled jobs (CRON)

You can schedule any job to run repeatedly using CRON expressions.

This library uses the [Cronos](https://github.com/HangfireIO/Cronos) nuget package to parse CRON expressions.

To schedule a job to run repeatedly, use the `PerformCronAsync` method on the `IJobScheduler` interface. Usually this is
done during application startup.

```csharp
var jobScheduler = application.Services.GetRequiredService<IJobScheduler>();
jobScheduler.PerformCronAsync<MyJob>("* * * * * *", new MyJobParameters()); // Run every second
jobScheduler.PerformCronAsync<MyJob>("* * * * *", new MyJobParameters());   // Run every minute
jobScheduler.PerformCronAsync<MyJob>("*/5 * * * *", new MyJobParameters()); // Run every 5 minutes
jobScheduler.PerformCronAsync<MyJob>("0 * * * *", new MyJobParameters());   // Run once an hour at the beginning of the hour
jobScheduler.PerformCronAsync<MyJob>("0 0 * * *", new MyJobParameters());   // Run once a day at midnight
jobScheduler.PerformCronAsync<MyJob>("0 0 * * 0", new MyJobParameters());   // Run once a week at midnight on Sunday morning
jobScheduler.PerformCronAsync<MyJob>("0 0 1 * *", new MyJobParameters());   // Run once a month at midnight of the first day of the month
```

## Culture

Scheduled jobs run later on background threads whose culture is arbitrary. To keep locale-sensitive work (date/number
formatting, translated resources) correct, every scheduled job captures a culture when it is scheduled and runs under
that culture — both `CurrentCulture` and `CurrentUICulture` — when it executes.

By default:

- `PerformAsapAsync` and `PerformAtAsync` capture the **scheduling thread's** culture (e.g. the user's culture on an
  ASP.NET request thread). `CurrentCulture` and `CurrentUICulture` are captured independently.
- `PerformCronAsync` defaults to the **invariant** culture, since CRON jobs are usually registered at application
  startup where the thread culture is not meaningful.
- `PerformNowAsync` runs inline on the calling thread, so it already uses your current culture — nothing is captured.

To pin a job to a specific culture, pass a `CultureInfo` (applied to both the formatting and UI culture):

```csharp
var culture = new CultureInfo("nl-NL");

await _jobScheduler.PerformAsapAsync<MyJob, MyJobParameters>(new MyJobParameters(), culture);
await _jobScheduler.PerformAtAsync<MyJob, MyJobParameters>(DateTime.UtcNow.AddHours(1), new MyJobParameters(), culture);
_jobScheduler.PerformCronAsync<MyJob, MyJobParameters>("0 0 * * *", new MyJobParameters(), culture);
```

A job that schedules further jobs propagates its culture automatically — while the job runs, its culture is the ambient
culture, so any job it schedules (without an explicit culture) captures it. CRON occurrences keep running in the culture
the CRON was registered with.

> Notes:
> - Only the culture **name** is stored, so a customized `CultureInfo` (hand-tweaked `NumberFormat`/`DateTimeFormat`)
>   falls back to its base culture.
> - If a captured culture cannot be resolved when the job executes (e.g. the app runs with `InvariantGlobalization`
>   enabled, or the host is missing that culture), the job fails through the normal failure path (`OnJobFailedAsync`).

## .NET Framework

It is possible to use this library in .NET Framework applications. However, since those applications don't generally use
Dependency Injection you can use the static `Jobs` class for registering jobs, scheduling jobs, and starting the job
runner process.

```csharp
// In your application startup code
Jobs.Register<MyJob>(); // All jobs must be registered before starting the job runner
Jobs.Start();
    
// In you application shutdown code
Jobs.Stop();

// In your application code
Jobs.Scheduler.PerformNow<MyJob, MyJobParameters>(new MyJobParameters());
Jobs.Scheduler.PerformAsap<MyJob, MyJobParameters>(new MyJobParameters());
Jobs.Scheduler.PerformAt<MyJob, MyJobParameters>(DateTime.Now, new MyJobParameters());
```