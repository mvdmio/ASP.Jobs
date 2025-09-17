using System;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

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
   public static void AddJobs(this IServiceCollection services, Action<JobConfigurationBuilder>? configure = null)
   {
      // Build the job runner configuration.
      var configurationBuilder = new JobConfigurationBuilder();
      configure?.Invoke(configurationBuilder);

      // Let the configuration register the configured services.
      configurationBuilder.SetupServices(services);
   }

   /// <summary>
   ///    Add a job to the service collection.
   /// </summary>
   public static void RegisterJob<TJob>(this IServiceCollection services) where TJob : class, IJob
   {
      services.AddScoped<TJob>(); // So that you can inject the implementation directly into some classes.
      services.AddScoped<IJob, TJob>(); // So that you can inject a list if implementations into some classes.
   }
   
   /// <summary>
   ///   Add the job activity source to the OpenTelemetry tracing.
   /// </summary>
   public static void AddJobs(this TracerProviderBuilder builder)
   {
      // Add the job activity source to the OpenTelemetry tracing.
      builder.AddSource("mvdmio.ASP.Jobs");
   }
}