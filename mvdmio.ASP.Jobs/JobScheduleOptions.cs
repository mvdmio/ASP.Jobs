using System;

namespace mvdmio.ASP.Jobs;

/// <summary>
/// Options for scheduling a job.
/// </summary>
public class JobScheduleOptions
{
   /// <summary>
   /// Id for this job schedule. If a job with the same ID already exists, it will be replaced.
   /// Defaults to a new GUID v7.
   /// </summary>
   public string JobId { get; init; } = Guid.CreateVersion7().ToString();
   
   /// <summary>
   /// Jobs in the same group will be executed in the order they were scheduled without running at the same time.
   /// Set null to not use a group. Defaults to null.
   /// </summary>
   public string? Group { get; init; }
}