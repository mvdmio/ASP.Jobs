using System;
using System.Text.Json;
using Cronos;
using mvdmio.ASP.Jobs.Internals.Storage.Data;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;

internal sealed class JobData
{
   public required Guid Id { get; init; }
   public required string JobType { get; init; }
   public required string ParametersType { get; init; }
   public required string ParametersJson { get; init; }
   public required string? CronExpression { get; init; }
   public required string ApplicationName { get; init; }
   public required string JobName { get; init; }
   public required string? JobGroup { get; init; }
   public required DateTime PerformAt { get; init; }
   public DateTime? StartedAt { get; set; }
   public string? StartedBy { get; set; }

   public static JobData FromJobStoreItem(string applicationName, JobStoreItem jobStoreItem)
   {
      return new JobData {
         Id = jobStoreItem.JobId,
         JobType = jobStoreItem.JobType.AssemblyQualifiedName!,
         ParametersType = jobStoreItem.Parameters.GetType().AssemblyQualifiedName!,
         ParametersJson = JsonSerializer.Serialize(jobStoreItem.Parameters),
         CronExpression = jobStoreItem.CronExpression?.ToString(),
         ApplicationName = applicationName,
         JobName = jobStoreItem.Options.JobName,
         JobGroup = jobStoreItem.Options.Group,
         PerformAt = jobStoreItem.PerformAt,
         StartedAt = null
      };
   }

   public JobStoreItem ToJobStoreItem()
   {
      var jobType = ResolveType(JobType);
      var parametersType = ResolveType(ParametersType);

      return new JobStoreItem {
         JobId = Id,
         JobType = jobType,
         Parameters = JsonSerializer.Deserialize(ParametersJson, parametersType)!,
         CronExpression = ParseCronExpression(CronExpression),
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

   private static CronExpression? ParseCronExpression(string? cronExpression)
   {
      if (cronExpression is null)
         return null;

      if (cronExpression.Split(' ').Length == 6)
         return Cronos.CronExpression.Parse(cronExpression, CronFormat.IncludeSeconds);

      return Cronos.CronExpression.Parse(cronExpression);
   }
}