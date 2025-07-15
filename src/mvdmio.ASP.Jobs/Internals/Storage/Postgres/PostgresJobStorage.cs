using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Connectors;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Models;
using NpgsqlTypes;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    Configuration options for the Postgres job storage.
/// </summary>
[PublicAPI]
public sealed class PostgresJobStorageConfiguration
{
   /// <summary>
   ///    The database connection to use for the job storage.
   /// </summary>
   public required DatabaseConnection DatabaseConnection { get; set; }
}

internal sealed class PostgresJobStorage : IJobStorage
{
   private readonly IClock _clock;
   private readonly DatabaseConnection _db;
   
   private readonly SemaphoreSlim _migrationsLock = new(1, 1);
   private bool _migrationsRun = false;

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
      await EnsureMigrationsRunAsync(ct);
      
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
               { "perform_at", job.PerformAt }
            }
         );
      }
      
      await _db.Dapper.ExecuteAsync("NOTIFY jobs_updated");
   }

   public async Task<JobStoreItem?> WaitForNextJobAsync(TimeSpan? maxWaitTime = null, CancellationToken ct = default)
   {
      await EnsureMigrationsRunAsync(ct);
      
      var startTime = _clock.UtcNow;
      
      while (true)
      {
         var now = _clock.UtcNow;
         
         var selectedJob = await _db.Dapper.QueryFirstOrDefaultAsync<JobData>(
            """
            UPDATE mvdmio.jobs
            SET started_at = :now
            WHERE id = (
               SELECT id
               FROM mvdmio.jobs
               WHERE perform_at <= :now
                 AND started_at IS NULL
               ORDER BY perform_at ASC, created_at ASC
               LIMIT 1
               FOR UPDATE SKIP LOCKED
            )
            RETURNING id, job_type, parameters_json, parameters_type, cron_expression, job_name, job_group, perform_at, started_at
            """,
            new Dictionary<string, object?> {
               { "now", now }
            }
         );

         if (selectedJob is not null)
            return selectedJob.ToJobStoreItem();

         if (maxWaitTime.HasValue && now - startTime >= maxWaitTime)
            return null;

         await SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(maxWaitTime, startTime, now, ct);
      }
   }

   private async Task SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(TimeSpan? maxWaitTime, DateTime startTime, DateTime now, CancellationToken ct)
   {
      var minPerformAt = await _db.Dapper.QueryFirstOrDefaultAsync<DateTime?>(
         """
         SELECT MIN(perform_at)
         FROM mvdmio.jobs
         WHERE started_at IS NULL
         """
      );
         
      TimeSpan? delta = minPerformAt.HasValue ? minPerformAt.Value - now : null;
         
      // Remaining time until maxWaitTime expires
      TimeSpan? remaining = maxWaitTime.HasValue ? maxWaitTime.Value - (_clock.UtcNow - startTime) : null;

      // Effective waitDuration: min(delta, remaining), or one of them, or null (indefinite)
      TimeSpan? waitDuration;
      if (delta.HasValue && remaining.HasValue)
         waitDuration = delta < remaining ? delta : remaining;
      else if (delta.HasValue)
         waitDuration = delta.Value;
      else if (remaining.HasValue)
         waitDuration = remaining.Value;
      else
         waitDuration = null;

      if (waitDuration.HasValue && waitDuration.Value <= TimeSpan.Zero)
         return;
         
      await _db.Dapper.ExecuteAsync("LISTEN jobs_updated");

      if (waitDuration.HasValue)
      {
         await Task.WhenAny(
            Task.Delay(waitDuration.Value, ct),
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
      await EnsureMigrationsRunAsync(ct);
      
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
      await EnsureMigrationsRunAsync(ct);
      
      var jobData = await _db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, job_name, job_group, perform_at, started_at
         FROM mvdmio.jobs
         WHERE started_at IS NULL
         ORDER BY perform_at ASC, created_at ASC
         """
      );
      
      return jobData.Select(x => x.ToJobStoreItem());
   }

   public async Task<IEnumerable<JobStoreItem>> GetInProgressJobsAsync(CancellationToken ct = default)
   {
      await EnsureMigrationsRunAsync(ct);
      
      var jobData = await _db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, job_name, job_group, perform_at, started_at
         FROM mvdmio.jobs
         WHERE started_at IS NOT NULL
         ORDER BY perform_at ASC, created_at ASC
         """
      );
      
      return jobData.Select(x => x.ToJobStoreItem());
   }
   
   private async Task EnsureMigrationsRunAsync(CancellationToken ct = default)
   {
      if(_migrationsRun)
         return;

      await _migrationsLock.WaitAsync(ct);

      if(_migrationsRun)
         return;
      
      try
      {
         var migrationRunner = new DatabaseMigrator(_db, GetType().Assembly);
         await migrationRunner.MigrateDatabaseToLatestAsync(ct);
         _migrationsRun = true;
      }
      finally
      {
         _migrationsLock.Release();
      }
   }
}