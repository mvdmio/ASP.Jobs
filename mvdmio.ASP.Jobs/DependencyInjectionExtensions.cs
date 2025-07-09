using System;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Extension methods for dependency injection.
/// </summary>
[PublicAPI]
public static class DependencyInjectionExtensions
{
   /// <summary>
   ///    Add the jobs framework to the service collection. Can be configured with the <paramref name="configure" /> action.
   /// </summary>
   public static void AddJobs(this IServiceCollection services, Action<JobRunnerConfiguration>? configure = null)
   {
      // Build the job runner configuration.
      var configuration = new JobRunnerConfiguration();
      configure?.Invoke(configuration);

      services.AddSingleton(configuration.JobStorage ?? new InMemoryJobStorage());

      if (configuration.IsSchedulerEnabled)
         services.AddSingleton<IJobScheduler, JobScheduler>();

      if (configuration.IsRunnerEnabled)
         services.AddHostedService<JobRunnerService>();
   }

   /// <summary>
   ///    Add a job to the service collection.
   /// </summary>
   public static void RegisterJob<TJob>(this IServiceCollection services) where TJob : class, IJob
   {
      services.AddScoped<TJob>(); // So that you can inject the implementation directly into some classes.
      services.AddScoped<IJob, TJob>(); // So that you can inject a list if implementations into some classes.
   }
}