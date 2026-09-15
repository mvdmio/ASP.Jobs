using System;
using System.Collections.Concurrent;
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
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Migrations;
using Npgsql;
using NpgsqlTypes;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    PostgreSQL implementation of <see cref="IJobStorage"/> for persistent job storage across multiple application instances.
///    Supports distributed job processing with locking and notifications.
/// </summary>
internal sealed class PostgresJobStorage : IJobStorage, IDisposable, IAsyncDisposable
{
   /// <summary>
   ///    The Resolution Grace window: how long a Worker Instance skips a job it just found Unresolvable before
   ///    trying it again. Also how long this instance's skip-list entry for that job lives.
   /// </summary>
   private static readonly TimeSpan ResolutionGrace = TimeSpan.FromMinutes(5);

   private readonly DatabaseConnectionFactory _dbConnectionFactory;
   private readonly IOptions<PostgresJobStorageConfiguration> _configuration;
   private readonly ILoggerFactory _loggerFactory;
   private readonly ILogger<PostgresJobStorage> _logger;
   private readonly IClock _clock;

   // Job ids this instance recently found Unresolvable, mapped to the storage-clock time their skip entry expires.
   // Excluded from this instance's own Claim query so it does not re-Claim the same job in a tight loop; a peer
   // instance is unaffected and can Claim the job immediately. Entries are dropped once they expire, so this
   // cannot grow without bound. Lives and dies with the process - nothing about correctness depends on it
   // surviving a restart.
   private readonly ConcurrentDictionary<Guid, DateTime> _unresolvableJobSkipList = new();

   private readonly SemaphoreSlim _initializationLock = new(1, 1);

   // volatile: written inside _initializationLock during InitializeAsync, but read lock-free by the
   // Initialization Guard (ThrowIfNotInitialized) and the double-checked fast path. volatile gives the
   // reader threads (job runner, request handlers) an acquire fence so they observe the completed
   // initialization rather than a stale 'false' on weak-memory hardware.
   private volatile bool _isInitialized;

   private PostgresJobStorageConfiguration Configuration => _configuration.Value;

   // IMPORTANT: each access returns a NEW DatabaseConnection wrapper. The wrapper holds a single
   // shared NpgsqlConnection field, so reusing the same wrapper across concurrent operations
   // would cause "A command is already in progress" errors when two callers race on the same
   // physical connection. The underlying NpgsqlDataSource is cached by the factory, so the
   // wrappers are cheap and each one acquires/returns its own pooled connector per call.
   private DatabaseConnection Db => _dbConnectionFactory.BuildConnection(Configuration.DatabaseConnectionString);

   public PostgresJobStorage(
      [FromKeyedServices("Jobs")] DatabaseConnectionFactory dbConnectionFactory,
      IOptions<PostgresJobStorageConfiguration> configuration,
      ILoggerFactory loggerFactory,
      IClock clock
   ) {
      _configuration = configuration;
      _loggerFactory = loggerFactory;
      _dbConnectionFactory = dbConnectionFactory;
      _logger = loggerFactory.CreateLogger<PostgresJobStorage>();
      _clock = clock;
   }

   public Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default)
   {
      return ScheduleJobsAsync([jobItem], ct);
   }

   public async Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default)
   {
      ThrowIfNotInitialized();

      var jobData = items.Select(x => JobData.FromJobStoreItem(Configuration.ApplicationName, x));

      foreach (var job in jobData)
      {
         await Db.Dapper.ExecuteAsync(
            """
            INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, culture, ui_culture, perform_at)
            VALUES (:id, :job_type, :parameters_json, :parameters_type, :cron_expression, :application_name, :job_name, :job_group, :culture, :ui_culture, :perform_at)
            ON CONFLICT (application_name, job_name) WHERE started_at IS NULL
            DO UPDATE SET
                id = EXCLUDED.id,
                parameters_json = EXCLUDED.parameters_json,
                culture = EXCLUDED.culture,
                ui_culture = EXCLUDED.ui_culture,
                perform_at = EXCLUDED.perform_at,
                attempt = 0
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
               { "culture", job.Culture },
               { "ui_culture", job.UICulture },
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
      ThrowIfNotInitialized();

      try
      {
         while (!ct.IsCancellationRequested)
         {
            var now = _clock.UtcNow;

            PurgeExpiredSkipListEntries(now);
            var skippedJobIds = _unresolvableJobSkipList.Keys.ToArray();

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
                    AND NOT (id = ANY(:skipped_job_ids))
                  ORDER BY perform_at, created_at
                  LIMIT 1
                  FOR UPDATE SKIP LOCKED
               )
               RETURNING id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, culture, ui_culture, perform_at, started_at, started_by, attempt, unresolvable_since
               """,
               new Dictionary<string, object?> {
                  { "now", now },
                  { "instance_id", Configuration.InstanceId },
                  { "application_name", Configuration.ApplicationName },
                  { "skipped_job_ids", skippedJobIds }
               },
               ct: ct
            );

            if (selectedJob is not null)
            {
               var jobStoreItem = selectedJob.ToJobStoreItem();
               if (jobStoreItem is null)
               {
                  await DeferUnresolvableJobAsync(selectedJob, now, ct);
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
      ThrowIfNotInitialized();

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

   public async Task<bool> TryScheduleRetryAsync(JobStoreItem job, DateTime nextAttemptAtUtc, CancellationToken ct = default)
   {
      ThrowIfNotInitialized();

      try
      {
         var updatedId = await Db.Dapper.QueryFirstOrDefaultAsync<Guid?>(
            """
            UPDATE mvdmio.jobs
            SET perform_at = :perform_at,
                attempt = attempt + 1,
                started_at = NULL,
                started_by = NULL
            WHERE id = :id
              AND application_name = :application_name
              AND NOT EXISTS (
                 SELECT 1
                 FROM mvdmio.jobs other
                 WHERE other.application_name = :application_name
                   AND other.job_name = :job_name
                   AND other.started_at IS NULL
                   AND other.id <> :id
              )
            RETURNING id
            """,
            new Dictionary<string, object?> {
               { "id", job.JobId },
               { "perform_at", nextAttemptAtUtc },
               { "application_name", Configuration.ApplicationName },
               { "job_name", job.Options.JobName }
            },
            ct: ct
         );

         if (updatedId is null)
         {
            // A different pending job with the same name already exists - the chain is superseded.
            await SupersedeRetryAsync(job, "a newer pending job of the same name", null, ct);
            return false;
         }

         await Db.Dapper.ExecuteAsync("NOTIFY jobs_updated", ct: ct);
         return true;
      }
      catch (QueryException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
      {
         // A same-name job was inserted concurrently between our NOT EXISTS check and the UPDATE commit - treat as supersession.
         // Db.Dapper wraps the driver exception in a QueryException, so the PostgresException is unwrapped from InnerException.
         await SupersedeRetryAsync(job, "a concurrently-scheduled job of the same name", pg, ct);
         return false;
      }
   }

   private async Task SupersedeRetryAsync(JobStoreItem job, string supersededByDescription, PostgresException? exception, CancellationToken ct)
   {
      _logger.LogInformation(
         exception,
         "Job chain '{JobName}' (ID: {JobId}) was superseded by {SupersededByDescription}; the retry was not written.",
         job.Options.JobName,
         job.JobId,
         supersededByDescription
      );

      await DeleteJobByIdAsync(job.JobId, ct);
   }

   public async Task<IEnumerable<JobStoreItem>> GetScheduledJobsAsync(CancellationToken ct = default)
   {
      ThrowIfNotInitialized();

      var jobData = await Db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, culture, ui_culture, perform_at, started_at, started_by, attempt, unresolvable_since
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
      ThrowIfNotInitialized();

      var jobData = await Db.Dapper.QueryAsync<JobData>(
         """
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, culture, ui_culture, perform_at, started_at, started_by, attempt, unresolvable_since
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
      ThrowIfNotInitialized();

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
               "Job '{JobName}' (ID: {JobId}) with type '{JobType}' could not be loaded in this process.",
               job.JobName,
               job.Id,
               job.JobType
            );
         }
      }
   }

   /// <summary>
   ///    Releases the Claim on a job whose job type or parameters type could not be loaded in this process, opening
   ///    (or preserving) its Resolution Grace window, and remembers the job in this instance's skip list so it is
   ///    not Claimed again by this instance until the window lapses. A peer instance running a build that can load
   ///    the type is unaffected and Claims the job immediately, since <c>perform_at</c> is left untouched.
   /// </summary>
   private async Task DeferUnresolvableJobAsync(JobData job, DateTime now, CancellationToken ct)
   {
      await Db.Dapper.ExecuteAsync(
         """
         UPDATE mvdmio.jobs
         SET started_at = NULL,
             started_by = NULL,
             unresolvable_since = COALESCE(unresolvable_since, :now)
         WHERE id = :id
         """,
         new Dictionary<string, object?> {
            { "id", job.Id },
            { "now", now }
         },
         ct: ct
      );

      await Db.Dapper.ExecuteAsync("NOTIFY jobs_updated", ct: ct);

      _unresolvableJobSkipList[job.Id] = now.Add(ResolutionGrace);

      _logger.LogDebug(
         "Job '{JobName}' (ID: {JobId}) with type '{JobType}' could not be loaded in this process. Deferring for the Resolution Grace window.",
         job.JobName,
         job.Id,
         job.JobType
      );
   }

   /// <summary>
   ///    Drops skip-list entries whose Resolution Grace window has lapsed, so the set cannot grow without bound
   ///    and an expired job becomes eligible for this instance's own Claim query again.
   /// </summary>
   private void PurgeExpiredSkipListEntries(DateTime now)
   {
      foreach (var (jobId, expiresAt) in _unresolvableJobSkipList)
      {
         if (expiresAt <= now)
            _unresolvableJobSkipList.TryRemove(jobId, out _);
      }
   }

   private async Task SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(DateTime now, CancellationToken ct)
   {
      var minPerformAt = await Db.Dapper.QueryFirstOrDefaultAsync<DateTime?>(
         """
         SELECT MIN(perform_at)
         FROM mvdmio.jobs
         WHERE started_at IS NULL
           AND NOT (id = ANY(:skipped_job_ids))
         """,
         new Dictionary<string, object?> {
            { "skipped_job_ids", _unresolvableJobSkipList.Keys.ToArray() }
         },
         ct: ct
      );

      // Rows in the skip list are still pending and due, so excluding them above stops the "next
      // due time" query from returning a time in the past for the whole Resolution Grace window
      // (which would otherwise spin this loop hot). Instead, bound the wait by whichever comes
      // first: the next non-skipped job coming due, or the earliest skip entry lapsing - at which
      // point its job becomes eligible for this instance's own Claim query again.
      var earliestSkipListExpiry = _unresolvableJobSkipList.IsEmpty ? (DateTime?)null : _unresolvableJobSkipList.Values.Min();

      var nextWakeAt = minPerformAt;
      if (earliestSkipListExpiry.HasValue && (nextWakeAt is null || earliestSkipListExpiry.Value < nextWakeAt.Value))
         nextWakeAt = earliestSkipListExpiry;

      TimeSpan? timeUntilNextWake = nextWakeAt.HasValue ? nextWakeAt.Value - now : null;

      if (timeUntilNextWake.HasValue && timeUntilNextWake.Value <= TimeSpan.Zero)
         return;

      if (timeUntilNextWake.HasValue)
      {
         // Use a linked cancellation token so that whichever branch loses the race in Task.WhenAny
         // is cancelled and releases its resources. Without this, Db.WaitAsync keeps a dedicated
         // LISTEN connection open until the outer cancellation token fires, leaking one connection
         // per polling iteration whenever the delay branch wins (eventually exhausting Postgres'
         // max_connections with error 53300).
         using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

         var delayTask = Task.Delay(timeUntilNextWake.Value, waitCts.Token);
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
      _dbConnectionFactory.Dispose();
      _initializationLock.Dispose();
   }

   public async ValueTask DisposeAsync()
   {
      if (_disposed)
         return;

      _disposed = true;
      await _dbConnectionFactory.DisposeAsync();
      _initializationLock.Dispose();
   }

   public async Task InitializeAsync(CancellationToken ct = default)
   {
      if(_isInitialized)
         return;

      await _initializationLock.WaitAsync(ct);

      try
      {
         if(_isInitialized)
            return;

         await RunDbMigrations(ct);
         _isInitialized = true;
      }
      finally
      {
         _initializationLock.Release();
      }
   }

   private void ThrowIfNotInitialized()
   {
      if(!_isInitialized)
         throw new JobStorageNotInitializedException();
   }

   private async Task RunDbMigrations(CancellationToken ct = default)
   {
      var migrationRunner = new DatabaseMigrator(Db, _loggerFactory, GetType().Assembly);
      await migrationRunner.MigrateDatabaseToLatestAsync(ct);
   }
}