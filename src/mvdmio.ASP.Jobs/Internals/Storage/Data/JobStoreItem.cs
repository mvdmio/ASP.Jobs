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
   ///    Gets the name of the formatting culture (<see cref="System.Globalization.CultureInfo.CurrentCulture"/>) that the job
   ///    should run under, captured when the job was scheduled. The invariant culture is the empty string; <c>null</c> means no
   ///    culture was captured (e.g. a job persisted before culture capture existed) and the executing thread's culture is left untouched.
   /// </summary>
   public string? CultureName { get; init; }

   /// <summary>
   ///    Gets the name of the UI culture (<see cref="System.Globalization.CultureInfo.CurrentUICulture"/>) that the job should run
   ///    under, captured when the job was scheduled. Follows the same null/empty-string semantics as <see cref="CultureName"/>.
   /// </summary>
   public string? UICultureName { get; init; }

   /// <summary>
   ///    Gets the number of retries already consumed by this Execution Chain (0 for a job that has not yet been retried).
   /// </summary>
   public int Attempt { get; init; }

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