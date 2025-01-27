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
      await _jobScheduler.PerformNowAsync<MyJob>();  // Runs the job immediately and waits for completion.
      await _jobScheduler.PerformAsapAsync<MyJob>(); // Runs the job on a separate thread as soon as a slot becomes available.
      await _jobScheduler.PerformNowAsync<MyJob>();  // Runs the job on a separate thread at the given time.

      return Ok();
   }
}
```