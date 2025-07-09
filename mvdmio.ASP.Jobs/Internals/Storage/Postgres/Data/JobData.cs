using System;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;

internal sealed class JobData
{
   public long? Id { get; set; }
   public required string JobType { get; set; }
   public required string ParametersJson { get; set; }
   public required string ParametersType { get; set; }
   public required string? CronExpression { get; set; }
   public required string JobName { get; set; }
   public required string? JobGroup { get; set; }
   public required DateTime PerformAt { get; set; }
   public DateTime? StartedAt { get; set; }
   public DateTime? CompletedAt { get; set; }
}