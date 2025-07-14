using System;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Options for scheduling a job.
/// </summary>
public class JobScheduleOptions
{
   /// <summary>
   ///    Name for this scheduled job. If a job with the same name is not already started it will be replaced.
   /// </summary>
   public string JobName { get; set; } =
#if NET9_0_OR_GREATER
      Guid.CreateVersion7().ToString("N");
#else
      Guid.NewGuid().ToString("N");
#endif

   /// <summary>
   ///    Jobs in the same group will be executed in the order they were scheduled without running at the same time.
   ///    Set null to not use a group. Defaults to null.
   /// </summary>
   public string? Group { get; init; }
}