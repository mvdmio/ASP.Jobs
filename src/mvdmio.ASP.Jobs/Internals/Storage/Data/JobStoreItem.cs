using System;
using Cronos;

namespace mvdmio.ASP.Jobs.Internals.Storage.Data;

/// <summary>
///    Represents a job item stored in the job storage system.
/// </summary>
internal sealed class JobStoreItem
{
   /// <summary>
   ///    Gets the unique identifier for this job instance.
   /// </summary>
   public Guid JobId { get; init; } = NewGuid();
   
   /// <summary>
   ///    Gets the type of job to execute.
   /// </summary>
   public required Type JobType { get; init; }
   
   /// <summary>
   ///    Gets the parameters to pass to the job when executed.
   /// </summary>
   public required object Parameters { get; init; }
   
   /// <summary>
   ///    Gets the scheduling options for this job.
   /// </summary>
   public required JobScheduleOptions Options { get; init; }
   
   /// <summary>
   ///    Gets the UTC time at which this job should be performed.
   /// </summary>
   public required DateTime PerformAt { get; init; }
   
   /// <summary>
   ///    Gets the optional CRON expression for recurring jobs.
   /// </summary>
   public CronExpression? CronExpression { get; init; }

   /// <summary>
   ///    Generates a new GUID for job identification.
   /// </summary>
   /// <returns>A new GUID.</returns>
   private static Guid NewGuid()
   {
#if NET9_0_OR_GREATER
      return Guid.CreateVersion7();
#else
      return Guid.NewGuid();
#endif      
   }
}