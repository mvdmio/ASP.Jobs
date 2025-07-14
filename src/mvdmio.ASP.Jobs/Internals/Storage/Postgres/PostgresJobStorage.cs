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
      
      await _db.Bulk.UpsertAsync(
         "mvdmio.jobs",
         new UpsertConfiguration {
            OnConflictColumns = [ "job_name" ],
            OnConflictWhereClause = "started_at IS NULL", 
         },
         jobData,
         new Dictionary<string, Func<JobData, DbValue>> {
            { "id", item => new DbValue(item.Id, NpgsqlDbType.Uuid) },
            { "job_type", item => new DbValue(item.JobType, NpgsqlDbType.Text) },
            { "parameters_json", item => new DbValue(item.ParametersJson, NpgsqlDbType.Jsonb) },
            { "parameters_type", item => new DbValue(item.ParametersType, NpgsqlDbType.Text) },
            { "cron_expression", item => new DbValue(item.CronExpression, NpgsqlDbType.Text) },
            { "job_name", item => new DbValue(item.JobName, NpgsqlDbType.Text) },
            { "job_group", item => new DbValue(item.JobGroup, NpgsqlDbType.Text) },
            { "perform_at", item => new DbValue(item.PerformAt, NpgsqlDbType.TimestampTz) }
         },
         ct: ct
      );
   }

   public async Task<JobStoreItem?> StartNextJobAsync(CancellationToken ct = default)
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
         RETURNING id, job_type, parameters_json, parameters_type, cron_expression, job_name, job_group, perform_at, started_at, completed_at
         """,
         new Dictionary<string, object?> {
            { "now", now }
         }
      );

      if (selectedJob is null)
         return null;

      return selectedJob.ToJobStoreItem();
   }

   public async Task FinalizeJobAsync(JobStoreItem job, CancellationToken ct = default)
   {
      var now = _clock.UtcNow;
      
      await _db.Dapper.ExecuteAsync(
         """
         UPDATE mvdmio.jobs
         SET completed_at = :now
         WHERE id = :id
         """,
         new Dictionary<string, object?> {
            { "now", now },
            { "id", job.JobId }
         }
      );
   }
}