using System;
using System.Text.Json;
using mvdmio.ASP.Jobs.Internals.Storage.Data;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;

internal sealed class JobData
{
   public long? Id { get; set; }
   public required string JobType { get; set; }
   public required string ParametersType { get; set; }
   public required string ParametersJson { get; set; }
   public required string? CronExpression { get; set; }
   public required string JobName { get; set; }
   public required string? JobGroup { get; set; }
   public required DateTimeOffset PerformAt { get; set; }
   public DateTimeOffset? StartedAt { get; set; }
   public DateTimeOffset? CompletedAt { get; set; }

   public static JobData FromJobStoreItem(JobStoreItem jobStoreItem)
   {
      return new JobData {
         Id = null,
         JobType = jobStoreItem.JobType.AssemblyQualifiedName!,
         ParametersType = jobStoreItem.Parameters.GetType().AssemblyQualifiedName!,
         ParametersJson = JsonSerializer.Serialize(jobStoreItem.Parameters),
         CronExpression = jobStoreItem.CronExpression?.ToString(),
         JobName = jobStoreItem.Options.JobName,
         JobGroup = jobStoreItem.Options.Group,
         PerformAt = jobStoreItem.PerformAt,
         StartedAt = null,
         CompletedAt = null
      };
   }

   public JobStoreItem ToJobStoreItem()
   {
      var jobType = ResolveType(JobType);
      var parametersType = ResolveType(ParametersType);

      return new JobStoreItem {
         JobType = jobType,
         Parameters = JsonSerializer.Deserialize(ParametersJson, parametersType)!,
         CronExpression = CronExpression is null ? null : Cronos.CronExpression.Parse(CronExpression),
         PerformAt = PerformAt,
         Options = new JobScheduleOptions {
            JobName = JobName,
            Group = JobGroup
         }
      };
   }
   
   private static Type ResolveType(string assemblyQualifiedTypeName)
   {
      var type = Type.GetType(assemblyQualifiedTypeName);
      if (type is null)
         throw new InvalidOperationException($"Type '{assemblyQualifiedTypeName}' could not be resolved.");
      
      return type;
   }
}