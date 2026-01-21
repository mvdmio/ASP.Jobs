using System;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Extension methods for configuring job services in the dependency injection container.
/// </summary>
[PublicAPI]
public static class DependencyInjectionExtensions
{
   /// <param name="services">The service collection to add the jobs framework to.</param>
   extension(IServiceCollection services)
   {
      /// <summary>
      ///    Adds the jobs framework to the service collection.
      /// </summary>
      /// <param name="configure">An optional action to configure the job system.</param>
      public void AddJobs(Action<JobConfigurationBuilder>? configure = null)
      {
         // Build the job runner configuration.
         var configurationBuilder = new JobConfigurationBuilder();
         configure?.Invoke(configurationBuilder);

         // Let the configuration register the configured services.
         configurationBuilder.SetupServices(services);
      }

      /// <summary>
      ///    Registers a job type with the service collection, making it available for scheduling and execution.
      /// </summary>
      /// <typeparam name="TJob">The type of job to register. Must implement <see cref="IJob"/>.</typeparam>
      public void RegisterJob<TJob>() where TJob : class, IJob
      {
         services.AddScoped<TJob>();       // So that you can inject the implementation directly into some classes.
         services.AddScoped<IJob, TJob>(); // So that you can inject a list if implementations into some classes.
      }
   }

   /// <param name="builder">The tracer provider builder to add the job activity source to.</param>
   extension(TracerProviderBuilder builder)
   {
      /// <summary>
      ///    Adds the job activity source to the OpenTelemetry tracing configuration.
      /// </summary>
      public void AddJobs()
      {
         // Add the job activity source to the OpenTelemetry tracing.
         builder.AddSource("mvdmio.ASP.Jobs");
      }
   }
   
   
}