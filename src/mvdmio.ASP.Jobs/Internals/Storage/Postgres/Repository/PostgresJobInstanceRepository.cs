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
   private DatabaseConnection Db => _dbConnectionFactory.ForConnectionString(Configuration.DatabaseConnectionString);
   
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
         }
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
         }
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
               }
            );

            if (expiredInstances.Any())
            {
               await Db.Dapper.ExecuteAsync(
                  """
                  UPDATE mvdmio.jobs
                  SET started_at = NULL, 
                      started_by = NULL
                  WHERE started_by = ANY(:expiredInstances);

                  DELETE FROM mvdmio.job_instances
                  WHERE instance_id = ANY(:expiredInstances);
                  """,
                  new Dictionary<string, object?> {
                     { "expiredInstances", expiredInstances }
                  }
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
      await Db.Dapper.ExecuteAsync(
         """
         UPDATE mvdmio.jobs
            SET started_at = NULL,
                started_by = NULL
         WHERE started_at IS NOT NULL
           AND started_by = :instanceId
         """,
         new Dictionary<string, object?> {
            { "instanceId", InstanceId }
         }
      );
   }

   /// <summary>
   ///    Retrieves all registered job processing instances.
   /// </summary>
   /// <returns>A collection of all registered instances.</returns>
   public async Task<IEnumerable<InstanceData>> GetInstances()
   {
      return await Db.Dapper.QueryAsync<InstanceData>("SELECT instance_id, last_seen_at FROM mvdmio.job_instances");
   }
}