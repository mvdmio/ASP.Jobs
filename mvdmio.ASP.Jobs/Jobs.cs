using System;
using System.Threading;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Utils;

namespace mvdmio.ASP.Jobs;

/// <summary>
/// Legacy class for backwards compatibility to ASP.NET applications.
/// Use dependency injection in modern applications instead.
/// </summary>
[PublicAPI]
public static class Jobs
{
   private static readonly ServiceCollection _services = new();
   
   /// <inheritdoc cref="IJobScheduler" />
   public static IJobScheduler Scheduler { get; private set; } = null!;
   private static JobRunnerService Runner { get; set; } = null!;
   
   /// <summary>
   ///   Add a new job to the service collection.
   /// </summary>
   public static void Add<TJob, TParameters>() 
      where TJob : Job<TParameters>
   {
      _services.AddJob<TJob, TParameters>();
   }
   
   /// <summary>
   ///   Start the job runner background service.
   ///   After calling 'start' it is no longer allowed to add new jobs to the service collection.
   /// </summary>
   public static void Start(JobConfiguration? configuration = null)
   {
      var serviceProvider = _services.BuildServiceProvider();
      var jobStorage = new InMemoryJobStorage();
      
      Scheduler = new JobScheduler(serviceProvider, jobStorage);
      Runner = new JobRunnerService(serviceProvider, jobStorage, new OptionsWrapper<JobConfiguration>(configuration ?? new JobConfiguration()));

      AsyncHelper.RunSync(() => Runner.StartAsync(CancellationToken.None));
   }

   /// <summary>
   ///   Stop the job runner background service.
   /// </summary>
   public static void Stop()
   {
      AsyncHelper.RunSync(() => Runner.StopAsync(CancellationToken.None));
   }
}