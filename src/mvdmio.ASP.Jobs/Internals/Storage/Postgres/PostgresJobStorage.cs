using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

internal sealed class PostgresJobStorage : IJobStorage, IDisposable, IAsyncDisposable
{
   private readonly DatabaseConnectionFactory _dbConnectionFactory;
   private readonly IOptions<PostgresJobStorageConfiguration> _configuration;
   private readonly IClock _clock;
   
   private PostgresJobStorageConfiguration Configuration => _configuration.Value;
   private DatabaseConnection Db => _dbConnectionFactory.ForConnectionString(Configuration.DatabaseConnectionString);
   
   public PostgresJobStorage(
      [FromKeyedServices("Jobs")] DatabaseConnectionFactory dbConnectionFactory,
      IOptions<PostgresJobStorageConfiguration> configuration,
      IClock clock
   ) {
      _configuration = configuration;
      _dbConnectionFactory = dbConnectionFactory;
      _clock = clock;
   }

   public Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default)
   {
      return ScheduleJobsAsync([jobItem], ct);
   }

   public async Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default)
   {
      var jobData = items.Select(x => JobData.FromJobStoreItem(Configuration.ApplicationName, x));

      foreach (var job in jobData)
      {
         await Db.Dapper.ExecuteAsync(
            """
            INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at)
            VALUES (:id, :job_type, :parameters_json, :parameters_type, :cron_expression, :application_name, :job_name, :job_group, :perform_at)
            ON CONFLICT (application_name, job_name) WHERE started_at IS NULL 
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
               { "application_name", job.ApplicationName },
               { "job_name", job.JobName },
               { "job_group", job.JobGroup },
               { "perform_at", job.PerformAt },
               { "instance_id", Configuration.InstanceId }
            }
         );
      }
      
      await Db.Dapper.ExecuteAsync("NOTIFY jobs_updated");
   }

   public async Task<JobStoreItem?> WaitForNextJobAsync(CancellationToken ct = default)
   {
      try
      {
         while (!ct.IsCancellationRequested)
         {
            var now = _clock.UtcNow;
         
            var selectedJob = await Db.Dapper.QueryFirstOrDefaultAsync<JobData>(
               """
               UPDATE mvdmio.jobs
               SET started_at = :now,
                   started_by = :instance_id
               WHERE id = (
                  SELECT id
                  FROM mvdmio.jobs
                  WHERE application_name = :application_name
                    AND perform_at <= :now
                    AND started_at IS NULL
                  ORDER BY perform_at, created_at
                  LIMIT 1
                  FOR UPDATE SKIP LOCKED
               )
               RETURNING id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, started_at, started_by
               """,
               new Dictionary<string, object?> {
                  { "now", now },
                  { "instance_id", Configuration.InstanceId },
                  { "application_name", Configuration.ApplicationName }
               }
            );

            if (selectedJob is not null)
               return selectedJob.ToJobStoreItem();

            await SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(now, ct);
         }

         return null;
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped.
         return null;
      }
   }

   public async Task FinalizeJobAsync(JobStoreItem job, CancellationToken ct = default)
   {
      await Db.Dapper.ExecuteAsync(
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
      var jobData = await Db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, started_at, started_by
         FROM mvdmio.jobs
         WHERE started_at IS NULL
         ORDER BY perform_at, created_at
         """
      );
      
      return jobData.Select(x => x.ToJobStoreItem());
   }

   public async Task<IEnumerable<JobStoreItem>> GetInProgressJobsAsync(CancellationToken ct = default)
   {
      var jobData = await Db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, started_at, started_by
         FROM mvdmio.jobs
         WHERE started_at IS NOT NULL
         ORDER BY perform_at, created_at
         """
      );
      
      return jobData.Select(x => x.ToJobStoreItem());
   }

   private async Task SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(DateTime now, CancellationToken ct)
   {
      var minPerformAt = await Db.Dapper.QueryFirstOrDefaultAsync<DateTime?>(
         """
         SELECT MIN(perform_at)
         FROM mvdmio.jobs
         WHERE started_at IS NULL
         """
      );
         
      TimeSpan? timeUntilNextPerformAt = minPerformAt.HasValue ? minPerformAt.Value - now : null;

      if (timeUntilNextPerformAt.HasValue && timeUntilNextPerformAt.Value <= TimeSpan.Zero)
         return;
         
      if (timeUntilNextPerformAt.HasValue)
      {
         await Task.WhenAny(
            Task.Delay(timeUntilNextPerformAt.Value, ct),
            Db.WaitAsync("jobs_updated", ct)
         );   
      }
      else
      {
         await Db.WaitAsync("jobs_updated", ct);
      }
   }

   public void Dispose()
   {
      _dbConnectionFactory.Dispose();
   }

   public async ValueTask DisposeAsync()
   {
      await _dbConnectionFactory.DisposeAsync();
   }
}