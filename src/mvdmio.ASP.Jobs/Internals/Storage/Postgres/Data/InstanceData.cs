using System;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;

internal sealed class InstanceData
{
   public required string InstanceId { get; set; }
   public required string ApplicationName { get; set; }
   public required DateTime LastSeenAt { get; set; }
}