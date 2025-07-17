using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

internal sealed class PostgresJobStorage : IJobStorage
{
   private readonly IClock _clock;
   private readonly DatabaseConnection _db;

   public PostgresJobStorageConfiguration Configuration { get; }

   public PostgresJobStorage(PostgresJobStorageConfiguration configuration, IClock clock)
   {
      Configuration = configuration;
      
      _clock = clock;
      _db = configuration.DatabaseConnection;
   }

   public Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default)
   {
      return ScheduleJobsAsync([jobItem], ct);
   }

   public async Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default)
   {
      var jobData = items.Select(JobData.FromJobStoreItem);

      foreach (var job in jobData)
      {
         await _db.Dapper.ExecuteAsync(
            """
            INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, job_name, job_group, perform_at)
            VALUES (:id, :job_type, :parameters_json, :parameters_type, :cron_expression, :job_name, :job_group, :perform_at)
            ON CONFLICT (job_name) WHERE started_at IS NULL 
            DO UPDATE SET
                id = EXCLUDED.id,
                parameters_json = EXCLUDED.parameters_json,
                perform_at = EXCLUDED.perform_at
            """,
            new Dictionary<string, object?> {
               { "id", job.Id },
               { "job_type", job.JobType },
               { "parameters_json", new TypedQueryParameter(job.ParametersJson, NpgsqlDbType.Jsonb ) },
               { "parameters_type", job.ParametersType },
               { "cron_expression", job.CronExpression },
               { "job_name", job.JobName },
               { "job_group", job.JobGroup },
               { "perform_at", job.PerformAt },
               { "instance_id", Configuration.InstanceId }
            }
         );
      }
      
      await _db.Dapper.ExecuteAsync("NOTIFY jobs_updated");
   }

   public async Task<JobStoreItem?> WaitForNextJobAsync(CancellationToken ct = default)
   {
      try
      {
         while (true)
         {
            var now = _clock.UtcNow;
         
            var selectedJob = await _db.Dapper.QueryFirstOrDefaultAsync<JobData>(
               """
               UPDATE mvdmio.jobs
               SET started_at = :now,
                   started_by = :instance_id
               WHERE id = (
                  SELECT id
                  FROM mvdmio.jobs
                  WHERE perform_at <= :now
                    AND started_at IS NULL
                  ORDER BY perform_at, created_at
                  LIMIT 1
                  FOR UPDATE SKIP LOCKED
               )
               RETURNING id, job_type, parameters_json, parameters_type, cron_expression, job_name, job_group, perform_at, started_at, started_by
               """,
               new Dictionary<string, object?> {
                  { "now", now },
                  { "instance_id", Configuration.InstanceId }
               }
            );

            if (selectedJob is not null)
               return selectedJob.ToJobStoreItem();

            await SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(now, ct);
         }  
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
         return null;
      }
   }

   private async Task SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(DateTime now, CancellationToken ct)
   {
      var minPerformAt = await _db.Dapper.QueryFirstOrDefaultAsync<DateTime?>(
         """
         SELECT MIN(perform_at)
         FROM mvdmio.jobs
         WHERE started_at IS NULL
         """
      );
         
      TimeSpan? timeUntilNextPerformAt = minPerformAt.HasValue ? minPerformAt.Value - now : null;

      if (timeUntilNextPerformAt.HasValue && timeUntilNextPerformAt.Value <= TimeSpan.Zero)
         return;
         
      await _db.Dapper.ExecuteAsync("LISTEN jobs_updated");

      if (timeUntilNextPerformAt.HasValue)
      {
         await Task.WhenAny(
            Task.Delay(timeUntilNextPerformAt.Value, ct),
            _db.Connection.WaitAsync(ct)
         );   
      }
      else
      {
         await _db.Connection.WaitAsync(ct);
      }
   }

   public async Task FinalizeJobAsync(JobStoreItem job, CancellationToken ct = default)
   {
      await _db.Dapper.ExecuteAsync(
         """
         DELETE FROM mvdmio.jobs
         WHERE id = :id
         """,
         new Dictionary<string, object?> {
            { "id", job.JobId }
         }
      );
   }

   public async Task<IEnumerable<JobStoreItem>> GetScheduledJobsAsync(CancellationToken ct = default)
   {      
      var jobData = await _db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, job_name, job_group, perform_at, started_at, started_by
         FROM mvdmio.jobs
         WHERE started_at IS NULL
         ORDER BY perform_at, created_at
         """
      );
      
      return jobData.Select(x => x.ToJobStoreItem());
   }

   public async Task<IEnumerable<JobStoreItem>> GetInProgressJobsAsync(CancellationToken ct = default)
   {
      var jobData = await _db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, job_name, job_group, perform_at, started_at, started_by
         FROM mvdmio.jobs
         WHERE started_at IS NOT NULL
         ORDER BY perform_at, created_at
         """
      );
      
      return jobData.Select(x => x.ToJobStoreItem());
   }
}