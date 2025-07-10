using System.Threading;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Utils;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Legacy class for backwards compatibility to ASP.NET applications.
///    Use dependency injection in modern applications instead.
/// </summary>
[PublicAPI]
public static class Jobs
{
   private static readonly ServiceCollection _services = new();
   private static JobRunnerService _runner = null!;

   /// <inheritdoc cref="IJobScheduler" />
   public static IJobScheduler Scheduler { get; private set; } = null!;

   /// <summary>
   ///    Add a new job to the service collection.
   /// </summary>
   public static void Register<TJob>() where TJob : class, IJob
   {
      _services.RegisterJob<TJob>();
   }

   /// <summary>
   ///    Start the job runner background service.
   ///    After calling 'start' it is no longer allowed to add new jobs to the service collection.
   /// </summary>
   public static void Start(JobConfiguration? configuration = null)
   {
      var serviceProvider = _services.BuildServiceProvider();
      var jobStorage = new InMemoryJobStorage();

      Scheduler = new JobScheduler(serviceProvider, jobStorage);
      _runner = new JobRunnerService(serviceProvider, jobStorage, new OptionsWrapper<JobConfiguration>(configuration ?? new JobConfiguration()));

      AsyncHelper.RunSync(() => _runner.StartAsync(CancellationToken.None));
   }

   /// <summary>
   ///    Stop the job runner background service.
   /// </summary>
   public static void Stop()
   {
      AsyncHelper.RunSync(() => _runner.StopAsync(CancellationToken.None));
   }
}