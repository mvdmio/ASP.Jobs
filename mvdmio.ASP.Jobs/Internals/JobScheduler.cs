using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals.JobBus;
using Serilog;

namespace mvdmio.ASP.Jobs.Internals;

internal class JobScheduler : IJobScheduler
{
   private readonly IServiceProvider _services;
   private readonly IJobBus _jobBus;

   public JobScheduler(IServiceProvider services, IJobBus jobBus)
   {
      _services = services;
      _jobBus = jobBus;
   }

   public async Task PerformNowAsync<TJob, TParameters>(TParameters parameters, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      var scope = _services.CreateScope();
      var job = scope.ServiceProvider.GetRequiredService<TJob>();
      
      await job.OnJobScheduledAsync(parameters, cancellationToken);
      await job.ExecuteAsync(parameters, cancellationToken);
   }

   public async Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      var scope = _services.CreateScope();
      var job = scope.ServiceProvider.GetRequiredService<TJob>();
      
      await job.OnJobScheduledAsync(parameters, cancellationToken);
      await _jobBus.AddJobAsync<TJob, TParameters>(parameters, DateTimeOffset.Now, cancellationToken);
      
      Log.Information("Scheduled job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
   }

   public async Task PerformAtAsync<TJob, TParameters>(TParameters parameters, DateTimeOffset performAt, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      var scope = _services.CreateScope();
      var job = scope.ServiceProvider.GetRequiredService<TJob>();
      
      await job.OnJobScheduledAsync(parameters, cancellationToken);
      await _jobBus.AddJobAsync<TJob, TParameters>(parameters, performAt, cancellationToken);
      
      Log.Information("Scheduled Job: {JobType} with parameters: {@Parameters} to run at {Time}", typeof(TJob).Name, parameters, performAt);
   }
}