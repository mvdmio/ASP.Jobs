using System;
using System.Text.Json;
using Cronos;
using mvdmio.ASP.Jobs.Internals.Storage.Data;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;

/// <summary>
///    Data transfer object representing a job stored in the PostgreSQL database.
/// </summary>
internal sealed class JobData
{
   /// <summary>
   ///    Gets the unique identifier for the job.
   /// </summary>
   public required Guid Id { get; init; }
   
   /// <summary>
   ///    Gets the assembly-qualified type name of the job.
   /// </summary>
   public required string JobType { get; init; }
   
   /// <summary>
   ///    Gets the assembly-qualified type name of the job parameters.
   /// </summary>
   public required string ParametersType { get; init; }
   
   /// <summary>
   ///    Gets the JSON-serialized job parameters.
   /// </summary>
   public required string ParametersJson { get; init; }
   
   /// <summary>
   ///    Gets the optional CRON expression for recurring jobs.
   /// </summary>
   public required string? CronExpression { get; init; }
   
   /// <summary>
   ///    Gets the application name that owns this job.
   /// </summary>
   public required string ApplicationName { get; init; }
   
   /// <summary>
   ///    Gets the unique name for this job instance.
   /// </summary>
   public required string JobName { get; init; }
   
   /// <summary>
   ///    Gets the optional group name for sequential job execution.
   /// </summary>
   public required string? JobGroup { get; init; }
   
   /// <summary>
   ///    Gets the UTC time at which the job should be performed.
   /// </summary>
   public required DateTime PerformAt { get; init; }
   
   /// <summary>
   ///    Gets or sets the UTC time when the job was started, or null if not started.
   /// </summary>
   public DateTime? StartedAt { get; set; }
   
   /// <summary>
   ///    Gets or sets the instance ID that started the job, or null if not started.
   /// </summary>
   public string? StartedBy { get; set; }

   /// <summary>
   ///    Creates a <see cref="JobData"/> instance from a <see cref="JobStoreItem"/>.
   /// </summary>
   /// <param name="applicationName">The application name to associate with the job.</param>
   /// <param name="jobStoreItem">The job store item to convert.</param>
   /// <returns>A new <see cref="JobData"/> instance.</returns>
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

   /// <summary>
   ///    Converts this <see cref="JobData"/> to a <see cref="JobStoreItem"/>.
   /// </summary>
   /// <returns>A new <see cref="JobStoreItem"/> instance.</returns>
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

   /// <summary>
   ///    Resolves a type from its assembly-qualified name.
   /// </summary>
   /// <param name="assemblyQualifiedTypeName">The assembly-qualified type name.</param>
   /// <returns>The resolved type.</returns>
   /// <exception cref="InvalidOperationException">Thrown when the type cannot be resolved.</exception>
   private static Type ResolveType(string assemblyQualifiedTypeName)
   {
      var type = Type.GetType(assemblyQualifiedTypeName);
      if (type is null)
         throw new InvalidOperationException($"Type '{assemblyQualifiedTypeName}' could not be resolved.");

      return type;
   }

   /// <summary>
   ///    Parses a CRON expression string into a <see cref="CronExpression"/>.
   /// </summary>
   /// <param name="cronExpression">The CRON expression string, or null.</param>
   /// <returns>The parsed CRON expression, or null if the input was null.</returns>
   private static CronExpression? ParseCronExpression(string? cronExpression)
   {
      if (cronExpression is null)
         return null;

      if (cronExpression.Split(' ').Length == 6)
         return Cronos.CronExpression.Parse(cronExpression, CronFormat.IncludeSeconds);

      return Cronos.CronExpression.Parse(cronExpression);
   }
}