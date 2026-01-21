using System;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;

/// <summary>
///    Data transfer object representing a job processing instance in the PostgreSQL database.
/// </summary>
internal sealed class InstanceData
{
   /// <summary>
   ///    Gets or sets the unique identifier of the instance.
   /// </summary>
   public required string InstanceId { get; set; }
   
   /// <summary>
   ///    Gets or sets the name of the application this instance belongs to.
   /// </summary>
   public required string ApplicationName { get; set; }
   
   /// <summary>
   ///    Gets or sets the UTC time when this instance was last seen (heartbeat).
   /// </summary>
   public required DateTime LastSeenAt { get; set; }
}