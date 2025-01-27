using System;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using Serilog;

namespace mvdmio.ASP.Jobs.Internals;

internal class JobScheduler : IJobScheduler
{
   private readonly IServiceProvider _services;
   private readonly IJobStorage _jobStorage;

   public JobScheduler(IServiceProvider services, IJobStorage jobStorage)
   {
      _services = services;
      _jobStorage = jobStorage;
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
      await _jobStorage.AddJobAsync<TJob, TParameters>(parameters, DateTime.UtcNow, cancellationToken);
      
      Log.Information("Scheduled job: {JobType} with parameters: {@Parameters}", typeof(TJob).Name, parameters);
   }

   public async Task PerformAtAsync<TJob, TParameters>(DateTime performAtUtc, TParameters parameters, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      var scope = _services.CreateScope();
      var job = scope.ServiceProvider.GetRequiredService<TJob>();
      
      await job.OnJobScheduledAsync(parameters, cancellationToken);
      await _jobStorage.AddJobAsync<TJob, TParameters>(parameters, performAtUtc, cancellationToken);
      
      Log.Information("Scheduled Job: {JobType} with parameters: {@Parameters} to run at {Time}", typeof(TJob).Name, parameters, performAtUtc);
   }

   public async Task PerformCronAsync<TJob, TParameters>(CronExpression cronExpression, TParameters parameters, bool runImmediately = false, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>
   {
      var scope = _services.CreateScope();
      var job = scope.ServiceProvider.GetRequiredService<TJob>();

      await job.OnJobScheduledAsync(parameters, cancellationToken);
      await _jobStorage.AddCronJobAsync<TJob, TParameters>(parameters, cronExpression, runImmediately, cancellationToken);

      Log.Information("Scheduled Job: {JobType} with parameters: {@Parameters} to run on schedule {CronExpression}", typeof(TJob).Name, parameters, cronExpression.ToString());
   }
}