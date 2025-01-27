using System.Linq;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.JobBus;

namespace mvdmio.ASP.Jobs;

[PublicAPI]
public static class DependencyInjectionExtensions
{
   public static void AddJobs(this IServiceCollection services)
   {
      services.AddJobsScheduler();
      services.AddJobsRunner();
   }
   
   public static void AddJobsScheduler(this IServiceCollection services)
   {
      services.AddSingleton<IJobScheduler, JobScheduler>();
      
      // Hardcoded to in-memory job bus for now
      if(services.All(x => x.ImplementationType != typeof(InMemoryJobBus)))
         services.AddSingleton<IJobBus, InMemoryJobBus>();
   }

   public static void AddJobsRunner(this IServiceCollection services)
   {
      services.AddHostedService<JobRunnerService>();
      
      // Hardcoded to in-memory job bus for now
      if(services.All(x => x.ImplementationType != typeof(InMemoryJobBus)))
         services.AddSingleton<IJobBus, InMemoryJobBus>();
   }
   
   public static void AddJob<TJob, TParameters>(this IServiceCollection services)
      where TJob : class, IJob<TParameters>
   {
      services.AddScoped<TJob>();
   }
}