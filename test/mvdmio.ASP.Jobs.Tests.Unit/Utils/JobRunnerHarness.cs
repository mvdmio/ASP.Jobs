using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Encapsulates a fully-wired in-memory <see cref="JobScheduler"/> + <see cref="JobRunnerService"/> + <see cref="InMemoryJobStorage"/>
/// for use across runner-related tests. Removes constructor boilerplate that was duplicated in multiple test classes.
/// </summary>
internal sealed class JobRunnerHarness
{
   public TestClock Clock { get; }
   public InMemoryJobStorage Storage { get; }
   public JobScheduler Scheduler { get; }
   public JobRunnerService Runner { get; }

   public JobRunnerHarness(int maxConcurrentJobs = 10, int jobChannelCapacity = 50)
   {
      Clock = new TestClock();
      Storage = new InMemoryJobStorage(Clock);

      var services = new JobTestServices().Services;
      services.AddSingleton<IJobStorage>(Storage);
      var serviceProvider = services.BuildServiceProvider();

      var options = Options.Create(new JobRunnerOptions {
         MaxConcurrentJobs = maxConcurrentJobs,
         JobChannelCapacity = jobChannelCapacity
      });

      Scheduler = new JobScheduler(serviceProvider, Storage, Clock);
      Runner = new JobRunnerService(serviceProvider, options, NullLogger<JobRunnerService>.Instance);
   }

   /// <summary>
   /// Starts the runner, waits for all scheduled / in-progress jobs to drain, then stops the runner.
   /// </summary>
   public async Task RunAndDrainAsync(CancellationToken ct)
   {
      await Runner.StartAsync(ct);
      await WaitForAllJobsToFinishAsync(ct);
      await Runner.StopAsync(ct);
   }

   public async Task WaitForAllJobsToFinishAsync(CancellationToken ct)
   {
      while (true)
      {
         var scheduled = await Storage.GetScheduledJobsAsync(ct);
         var inProgress = await Storage.GetInProgressJobsAsync(ct);

         if (!scheduled.Any() && !inProgress.Any())
            return;

         await Task.Delay(10, ct);
      }
   }
}
