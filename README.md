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

3. Create a job by implementing the `IJob` interface:
```csharp
public class MyJobParameters
{
   public string Parameter { get; set; }
}

public class MyJob : IJob<MyJobParameters>
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
   services.AddJob<MyJob, MyJobParameters>();
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
      await _jobScheduler.PerformNowAsync<MyJob>(new MyJobParameters());  // Runs the job immediately and waits for completion.
      await _jobScheduler.PerformAsapAsync<MyJob>(new MyJobParameters()); // Runs the job on a separate thread as soon as a slot becomes available.
      await _jobScheduler.PerformAtAsync<MyJob>(DateTime.Now, new MyJobParameters());  // Runs the job on a separate thread at the given time.

      return Ok();
   }
}
```

## Scheduled jobs (CRON)
You can schedule any job to run repeatedly using CRON expressions.

This library uses the [Cronos](https://github.com/HangfireIO/Cronos) nuget package to parse CRON expressions.

To schedule a job to run repeatedly, use the `PerformCronAsync` method on the `IJobScheduler` interface. Usually this is done during application startup.

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