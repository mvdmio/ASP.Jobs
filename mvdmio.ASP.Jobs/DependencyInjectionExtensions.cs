﻿using System.Linq;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.JobBus;

namespace mvdmio.ASP.Jobs;

/// <summary>
/// Extension methods for dependency injection.
/// </summary>
[PublicAPI]
public static class DependencyInjectionExtensions
{
   /// <summary>
   /// Add the jobs framework to the service collection. Includes both the scheduler and the runner.
   /// </summary>
   /// <param name="services"></param>
   public static void AddJobs(this IServiceCollection services)
   {
      services.AddJobsScheduler();
      services.AddJobsRunner();
   }

   /// <summary>
   /// Add the job scheduler to the service collection.
   /// </summary>
   public static void AddJobsScheduler(this IServiceCollection services)
   {
      services.AddSingleton<IJobScheduler, JobScheduler>();
      
      // Hardcoded to in-memory job bus for now
      if(services.All(x => x.ImplementationType != typeof(InMemoryJobBus)))
         services.AddSingleton<IJobBus, InMemoryJobBus>();
   }

   /// <summary>
   /// Add the job runner to the service collection.
   /// </summary>
   public static void AddJobsRunner(this IServiceCollection services)
   {
      services.AddHostedService<JobRunnerService>();
      
      // Hardcoded to in-memory job bus for now
      if(services.All(x => x.ImplementationType != typeof(InMemoryJobBus)))
         services.AddSingleton<IJobBus, InMemoryJobBus>();
   }

   /// <summary>
   /// Add a job to the service collection.
   /// </summary>
   public static void AddJob<TJob, TParameters>(this IServiceCollection services)
      where TJob : class, IJob<TParameters>
   {
      services.AddScoped<TJob>();
   }
}