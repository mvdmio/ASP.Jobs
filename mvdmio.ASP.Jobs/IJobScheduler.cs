﻿using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
/// Interface for a job scheduler.
/// </summary>
[PublicAPI]
public interface IJobScheduler
{
   /// <summary>
   /// Schedule a job to be performed immediately.
   /// </summary>
   public Task PerformNowAsync<TJob, TParameters>(TParameters parameters, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>;
   
   /// <summary>
   /// Schedule a job to be performed as soon as possible.
   /// </summary>
   public Task PerformAsapAsync<TJob, TParameters>(TParameters parameters, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>;
   
   /// <summary>
   /// Schedule a job to be performed at a given time.
   /// </summary>
   public Task PerformAtAsync<TJob, TParameters>(TParameters parameters, DateTimeOffset performAt, CancellationToken cancellationToken = default)
      where TJob : IJob<TParameters>;
}