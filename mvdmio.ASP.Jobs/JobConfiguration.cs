﻿namespace mvdmio.ASP.Jobs;

/// <summary>
/// Configuration for the job system.
/// </summary>
public class JobConfiguration
{
   /// <summary>
   /// The maximum number of jobs that can be executed concurrently. Default is 1.
   /// </summary>
   public int MaxConcurrentJobs { get; init; } = 1;
}