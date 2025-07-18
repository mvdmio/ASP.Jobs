using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;

internal sealed class PostgresJobInstanceRepository
{
   private readonly PostgresJobStorageConfiguration _configuration;
   private readonly IClock _clock;

   // Using this pattern so that InstanceId can be updated from the tests.
   private string InstanceId => _configuration.InstanceId;
   private DatabaseConnection Db => _configuration.DatabaseConnection;
   
   public PostgresJobInstanceRepository(PostgresJobStorageConfiguration configuration, IClock clock)
   {
      _configuration = configuration;
      _clock = clock;
   }

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
            { "applicationName", _configuration.ApplicationName },
            { "lastSeenAt", _clock.UtcNow }
         }
      );
   }

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

   public Task UpdateLastSeenAt(CancellationToken ct = default)
   {
      // Just re-register the instance. This updates the last seen timestamp if the instance is already registered.
      return RegisterInstance(ct);
   }

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

   public async Task<IEnumerable<InstanceData>> GetInstances()
   {
      return await Db.Dapper.QueryAsync<InstanceData>("SELECT instance_id, last_seen_at FROM mvdmio.job_instances");
   }
}