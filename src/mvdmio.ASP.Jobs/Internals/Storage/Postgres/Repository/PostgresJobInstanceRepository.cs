using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;

/// <summary>
///    Repository for managing job processing instances in PostgreSQL.
///    Handles instance registration, heartbeat updates, and cleanup of stale instances.
/// </summary>
internal sealed class PostgresJobInstanceRepository
{
   private readonly DatabaseConnectionFactory _dbConnectionFactory;
   private readonly IOptions<PostgresJobStorageConfiguration> _configuration;
   private readonly IClock _clock;

   private PostgresJobStorageConfiguration Configuration => _configuration.Value;
   
   // Using this pattern so that InstanceId can be updated from the tests.
   private string InstanceId => Configuration.InstanceId;
   private DatabaseConnection Db => _dbConnectionFactory.BuildConnection(Configuration.DatabaseConnectionString);
   
   /// <summary>
   ///    Initializes a new instance of the <see cref="PostgresJobInstanceRepository"/> class.
   /// </summary>
   /// <param name="dbConnectionFactory">The database connection factory.</param>
   /// <param name="configuration">The PostgreSQL storage configuration.</param>
   /// <param name="clock">The clock for time operations.</param>
   public PostgresJobInstanceRepository(
      [FromKeyedServices("Jobs")] DatabaseConnectionFactory dbConnectionFactory,
      IOptions<PostgresJobStorageConfiguration> configuration,
      IClock clock
   ) {
      _dbConnectionFactory = dbConnectionFactory;
      _configuration = configuration;
      _clock = clock;
   }

   /// <summary>
   ///    Registers the current instance in the database or updates its last seen timestamp.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public async Task RegisterInstance(CancellationToken ct = default)
   {
      await Db.Dapper.ExecuteAsync(
         """
         INSERT INTO mvdmio.job_instances (instance_id, application_name, last_seen_at)
         VALUES (:instanceId, :applicationName, :lastSeenAt)
         ON CONFLICT (instance_id) DO UPDATE
         SET last_seen_at = EXCLUDED.last_seen_at
         """,
         new Dictionary<string, object?> {
            { "instanceId", InstanceId },
            { "applicationName", Configuration.ApplicationName },
            { "lastSeenAt", _clock.UtcNow }
         },
         ct: ct
      );
   }

   /// <summary>
   ///    Removes the current instance registration from the database.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public async Task UnregisterInstance(CancellationToken ct = default)
   {
      await Db.Dapper.ExecuteAsync(
         """
         DELETE FROM mvdmio.job_instances
         WHERE instance_id = :instanceId
         """,
         new Dictionary<string, object?> {
            { "instanceId", InstanceId }
         },
         ct: ct
      );
   }

   /// <summary>
   ///    Updates the last seen timestamp for the current instance.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public Task UpdateLastSeenAt(CancellationToken ct = default)
   {
      // Just re-register the instance. This updates the last seen timestamp if the instance is already registered.
      return RegisterInstance(ct);
   }

   /// <summary>
   ///    Cleans up instances that have not reported a heartbeat in over 5 minutes.
   ///    Releases any jobs claimed by those instances so they can be picked up by other workers.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public async Task CleanupOldInstances(CancellationToken ct = default)
   {
      await Db.InTransactionAsync(async () => {
            var expiredInstances = await Db.Dapper.QueryAsync<string>(
               """
               SELECT instance_id
               FROM mvdmio.job_instances
               WHERE last_seen_at < :maxAgeTimestamp
               """,
               new Dictionary<string, object?> {
                  { "maxAgeTimestamp", _clock.UtcNow.Subtract(TimeSpan.FromMinutes(5)) }
               },
               ct: ct
            );

            if (expiredInstances.Any())
            {
               await Db.Dapper.ExecuteAsync(
                  """
                  -- Reduce stale in-progress rows so that at most one row per
                  -- (application_name, job_name) ends up satisfying the partial unique index
                  -- 'idxu_jobs__application__job_name__not_started' once started_at is NULLed.
                  WITH stale AS (
                     SELECT id, application_name, job_name, perform_at
                     FROM mvdmio.jobs
                     WHERE started_by = ANY(:expiredInstances)
                  ),
                  has_unstarted AS (
                     SELECT DISTINCT s.application_name, s.job_name
                     FROM stale s
                     WHERE EXISTS (
                        SELECT 1 FROM mvdmio.jobs j2
                        WHERE j2.application_name = s.application_name
                          AND j2.job_name = s.job_name
                          AND j2.started_at IS NULL
                     )
                  ),
                  ranked AS (
                     SELECT s.id,
                            ROW_NUMBER() OVER (
                               PARTITION BY s.application_name, s.job_name
                               ORDER BY s.perform_at ASC, s.id ASC
                            ) AS rn
                     FROM stale s
                     WHERE NOT EXISTS (
                        SELECT 1 FROM has_unstarted u
                        WHERE u.application_name = s.application_name
                          AND u.job_name = s.job_name
                     )
                  ),
                  to_delete AS (
                     -- Drop every stale row whose group already has an unstarted peer.
                     SELECT s.id
                     FROM stale s
                     JOIN has_unstarted u
                       ON u.application_name = s.application_name
                      AND u.job_name = s.job_name
                     UNION ALL
                     -- For groups with no unstarted peer, keep the earliest-due survivor and drop the rest.
                     SELECT id FROM ranked WHERE rn > 1
                  )
                  DELETE FROM mvdmio.jobs WHERE id IN (SELECT id FROM to_delete);
                  
                  -- Reset remaining stale in-progress jobs (no conflict possible after the reduction above).
                  UPDATE mvdmio.jobs
                  SET started_at = NULL, 
                      started_by = NULL
                  WHERE started_by = ANY(:expiredInstances);

                  DELETE FROM mvdmio.job_instances
                  WHERE instance_id = ANY(:expiredInstances);
                  """,
                  new Dictionary<string, object?> {
                     { "expiredInstances", expiredInstances }
                  },
                  ct: ct
               );
            }
         }
      );
   }

   /// <summary>
   ///    Releases all jobs that were started by the current instance, making them available for other workers.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public async Task ReleaseStartedJobs(CancellationToken ct = default)
   {
      await Db.InTransactionAsync(async () => {
            await Db.Dapper.ExecuteAsync(
               """
               -- Reduce stale in-progress rows owned by this instance so that at most one row
               -- per (application_name, job_name) ends up satisfying the partial unique index
               -- 'idxu_jobs__application__job_name__not_started' once started_at is NULLed.
               WITH stale AS (
                  SELECT id, application_name, job_name, perform_at
                  FROM mvdmio.jobs
                  WHERE started_at IS NOT NULL
                    AND started_by = :instanceId
               ),
               has_unstarted AS (
                  SELECT DISTINCT s.application_name, s.job_name
                  FROM stale s
                  WHERE EXISTS (
                     SELECT 1 FROM mvdmio.jobs j2
                     WHERE j2.application_name = s.application_name
                       AND j2.job_name = s.job_name
                       AND j2.started_at IS NULL
                  )
               ),
               ranked AS (
                  SELECT s.id,
                         ROW_NUMBER() OVER (
                            PARTITION BY s.application_name, s.job_name
                            ORDER BY s.perform_at ASC, s.id ASC
                         ) AS rn
                  FROM stale s
                  WHERE NOT EXISTS (
                     SELECT 1 FROM has_unstarted u
                     WHERE u.application_name = s.application_name
                       AND u.job_name = s.job_name
                  )
               ),
               to_delete AS (
                  -- Drop every stale row whose group already has an unstarted peer.
                  SELECT s.id
                  FROM stale s
                  JOIN has_unstarted u
                    ON u.application_name = s.application_name
                   AND u.job_name = s.job_name
                  UNION ALL
                  -- For groups with no unstarted peer, keep the earliest-due survivor and drop the rest.
                  SELECT id FROM ranked WHERE rn > 1
               )
               DELETE FROM mvdmio.jobs WHERE id IN (SELECT id FROM to_delete);
               
               -- Reset remaining in-progress jobs (no conflict possible after the reduction above).
               UPDATE mvdmio.jobs
                  SET started_at = NULL,
                      started_by = NULL
               WHERE started_at IS NOT NULL
                 AND started_by = :instanceId
               """,
               new Dictionary<string, object?> {
                  { "instanceId", InstanceId }
               },
               ct: ct
            );
         }
      );
   }

   /// <summary>
   ///    Retrieves all registered job processing instances.
   /// </summary>
   /// <returns>A collection of all registered instances.</returns>
   public async Task<IEnumerable<InstanceData>> GetInstances(CancellationToken ct = default)
   {
      return await Db.Dapper.QueryAsync<InstanceData>("SELECT instance_id, last_seen_at FROM mvdmio.job_instances", ct: ct);
   }
}