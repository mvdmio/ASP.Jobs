using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using mvdmio.Database.PgSQL.Migrations;
using NpgsqlTypes;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    PostgreSQL implementation of <see cref="IJobStorage"/> for persistent job storage across multiple application instances.
///    Supports distributed job processing with locking and notifications.
/// </summary>
internal sealed class PostgresJobStorage : IJobStorage, IDisposable, IAsyncDisposable
{
   private readonly DatabaseConnectionFactory _dbConnectionFactory;
   private readonly IOptions<PostgresJobStorageConfiguration> _configuration;
   private readonly ILogger<PostgresJobStorage> _logger;
   private readonly IClock _clock;

   private readonly SemaphoreSlim _initializationLock = new(1, 1);
   private bool _isInitialized;

   // The DatabaseConnectionFactory caches the underlying NpgsqlDataSource per connection string,
   // but each call to BuildConnection allocates a fresh DatabaseConnection wrapper (with its own
   // SemaphoreSlim). Cache the wrapper for the lifetime of the storage instance to avoid
   // unnecessary allocations and to keep the connection-handling state in a single place.
   private readonly DatabaseConnection _db;

   private PostgresJobStorageConfiguration Configuration => _configuration.Value;

   private DatabaseConnection Db => _db;
   
   public PostgresJobStorage(
      [FromKeyedServices("Jobs")] DatabaseConnectionFactory dbConnectionFactory,
      IOptions<PostgresJobStorageConfiguration> configuration,
      ILogger<PostgresJobStorage> logger,
      IClock clock
   ) {
      _configuration = configuration;
      _dbConnectionFactory = dbConnectionFactory;
      _logger = logger;
      _clock = clock;
      _db = _dbConnectionFactory.BuildConnection(Configuration.DatabaseConnectionString);
   }

   public Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default)
   {
      return ScheduleJobsAsync([jobItem], ct);
   }

   public async Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default)
   {
      await EnsureInitializedAsync(ct);
      
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
            }, 
            ct: ct
         );
      }
      
      await Db.Dapper.ExecuteAsync("NOTIFY jobs_updated", ct: ct);
   }

   public async Task<JobStoreItem?> WaitForNextJobAsync(CancellationToken ct = default)
   {
      await EnsureInitializedAsync(ct);
      
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
               },
               ct: ct
            );

            if (selectedJob is not null)
            {
               var jobStoreItem = selectedJob.ToJobStoreItem();
               if (jobStoreItem is null)
               {
                  // Job type could not be resolved - delete the job and log a warning
                  await DeleteJobByIdAsync(selectedJob.Id, ct);
                  
                  _logger.LogWarning(
                     "Job '{JobName}' (ID: {JobId}) with type '{JobType}' could not be loaded because the type no longer exists. The job has been deleted.",
                     selectedJob.JobName,
                     selectedJob.Id,
                     selectedJob.JobType
                  );

                  continue;
               }
               
               return jobStoreItem;
            }

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
      await EnsureInitializedAsync(ct);
      
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
      await EnsureInitializedAsync(ct);
      
      var jobData = await Db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, started_at, started_by
         FROM mvdmio.jobs
         WHERE started_at IS NULL
         ORDER BY perform_at, created_at
         """,
         ct: ct
      );
      
      return FilterResolvableJobs(jobData);
   }

   public async Task<IEnumerable<JobStoreItem>> GetInProgressJobsAsync(CancellationToken ct = default)
   {
      await EnsureInitializedAsync(ct);
      
      var jobData = await Db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, started_at, started_by
         FROM mvdmio.jobs
         WHERE started_at IS NOT NULL
         ORDER BY perform_at, created_at
         """,
         ct: ct
      );
      
      return FilterResolvableJobs(jobData);
   }

   public async Task DeleteJobByIdAsync(Guid jobId, CancellationToken ct = default)
   {
      await EnsureInitializedAsync(ct);
      
      await Db.Dapper.ExecuteAsync(
         """
         DELETE FROM mvdmio.jobs
         WHERE id = :id
         """,
         new Dictionary<string, object?> {
            { "id", jobId }
         },
         ct: ct
      );
   }

   private IEnumerable<JobStoreItem> FilterResolvableJobs(IEnumerable<JobData> jobData)
   {
      foreach (var job in jobData)
      {
         var jobStoreItem = job.ToJobStoreItem();
         if (jobStoreItem is not null)
         {
            yield return jobStoreItem;
         }
         else
         {
            _logger.LogWarning(
               "Job '{JobName}' (ID: {JobId}) with type '{JobType}' could not be loaded because the type no longer exists.",
               job.JobName,
               job.Id,
               job.JobType
            );
         }
      }
   }

   private async Task SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(DateTime now, CancellationToken ct)
   {
      var minPerformAt = await Db.Dapper.QueryFirstOrDefaultAsync<DateTime?>(
         """
         SELECT MIN(perform_at)
         FROM mvdmio.jobs
         WHERE started_at IS NULL
         """,
         ct: ct
      );
         
      TimeSpan? timeUntilNextPerformAt = minPerformAt.HasValue ? minPerformAt.Value - now : null;

      if (timeUntilNextPerformAt.HasValue && timeUntilNextPerformAt.Value <= TimeSpan.Zero)
         return;

      if (timeUntilNextPerformAt.HasValue)
      {
         // Use a linked cancellation token so that whichever branch loses the race in Task.WhenAny
         // is cancelled and releases its resources. Without this, Db.WaitAsync keeps a dedicated
         // LISTEN connection open until the outer cancellation token fires, leaking one connection
         // per polling iteration whenever the delay branch wins (eventually exhausting Postgres'
         // max_connections with error 53300).
         using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

         var delayTask = Task.Delay(timeUntilNextPerformAt.Value, waitCts.Token);
         var listenTask = Db.WaitAsync("jobs_updated", waitCts.Token);

         await Task.WhenAny(delayTask, listenTask);

         // Cancel both branches so the loser releases its resources (in particular the
         // dedicated LISTEN connection used by Db.WaitAsync) before we return. We then
         // await both tasks to ensure their cleanup (NpgsqlConnection.CloseAsync /
         // DisposeAsync inside WaitAsync) has actually run.
         await CancelAndDrainAsync(waitCts, delayTask, listenTask);
      }
      else
      {
         try
         {
            await Db.WaitAsync("jobs_updated", ct);
         }
         catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
         {
            // Expected when the outer token fires.
         }
      }
   }

   private static async Task CancelAndDrainAsync(CancellationTokenSource cts, params Task[] tasks)
   {
      try
      {
         await cts.CancelAsync();
      }
      catch (ObjectDisposedException)
      {
         // The token source was already disposed - nothing to do.
      }

      foreach (var task in tasks)
      {
         try
         {
            await task;
         }
         catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
         {
            // Expected for the loser of the race.
         }
      }
   }

   private bool _disposed;

   public void Dispose()
   {
      if (_disposed)
         return;

      _disposed = true;
      _db.Dispose();
      _dbConnectionFactory.Dispose();
      _initializationLock.Dispose();
   }

   public async ValueTask DisposeAsync()
   {
      if (_disposed)
         return;

      _disposed = true;
      await _db.DisposeAsync();
      await _dbConnectionFactory.DisposeAsync();
      _initializationLock.Dispose();
   }

   private async Task EnsureInitializedAsync(CancellationToken ct = default)
   {
      if(_isInitialized)
         return;

      await _initializationLock.WaitAsync(ct);

      if(_isInitialized)
         return;
      
      try
      {
         await RunDbMigrations(ct);
      }
      finally
      {
         _isInitialized = true;
         _initializationLock.Release();
      }
   }
   
   private async Task RunDbMigrations(CancellationToken ct = default)
   {
      var migrationRunner = new DatabaseMigrator(Db, GetType().Assembly);
      await migrationRunner.MigrateDatabaseToLatestAsync(ct);
   }
}