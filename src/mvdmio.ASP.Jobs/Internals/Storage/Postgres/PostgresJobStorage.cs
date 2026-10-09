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

   /// <summary>
   ///    The longest a claim with nothing to claim sleeps before it looks again, even when no job is coming due.
   /// </summary>
   private static readonly TimeSpan MaxWaitTime = TimeSpan.FromSeconds(30);

   /// <summary>
   ///    How long a claim waits before it looks again when a due job could not be claimed because a peer holds the
   ///    job's row or its group's advisory lock in the middle of its own claim.
   /// </summary>
   private static readonly TimeSpan ContendedClaimRetryDelay = TimeSpan.FromMilliseconds(100);

   private readonly DatabaseConnectionFactory _dbConnectionFactory;
   private readonly IOptions<PostgresJobStorageConfiguration> _configuration;
   private readonly ILoggerFactory _loggerFactory;
   private readonly ILogger<PostgresJobStorage> _logger;
   private readonly IClock _clock;

   private readonly UnresolvableJobSkipList _unresolvableJobSkipList = new();
   private readonly PostgresJobClaimer _claimer;

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
      _claimer = new PostgresJobClaimer(() => Db, configuration);
   }

   public Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default)
   {
      return ScheduleJobsAsync([jobItem], ct);
   }

   public async Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default)
   {
      ThrowIfNotInitialized();

      // Convert every item before touching the database, so a conversion failure stores nothing.
      var jobData = items.Select(x => JobData.FromJobStoreItem(Configuration.ApplicationName, x)).ToList();
      if (jobData.Count == 0)
         return;

      // One statement cannot upsert the same row twice ("ON CONFLICT DO UPDATE command cannot affect row a second
      // time"), so keep only the last item per job name - the same outcome InMemoryJobStorage gets by keying on it.
      var rows = jobData
         .GroupBy(x => x.JobName)
         .Select(x => x.Last())
         .ToList();

      // One array parameter per column, expanded with unnest, keeps the bind-parameter count fixed whatever the
      // batch size. One statement is atomic, so the batch is stored completely or not at all. The parameters
      // array is typed jsonb[] explicitly: a string[] alone would bind as text[].
      await Db.Dapper.ExecuteAsync(
         """
         INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, culture, ui_culture, perform_at)
         SELECT id, job_type, parameters_json, parameters_type, cron_expression, :application_name, job_name, job_group, culture, ui_culture, perform_at
         FROM unnest(:ids, :job_types, :parameters_json, :parameters_types, :cron_expressions, :job_names, :job_groups, :cultures, :ui_cultures, :perform_ats)
            AS batch (id, job_type, parameters_json, parameters_type, cron_expression, job_name, job_group, culture, ui_culture, perform_at)
         ON CONFLICT (application_name, job_name) WHERE started_at IS NULL
         DO UPDATE SET
             id = EXCLUDED.id,
             job_type = EXCLUDED.job_type,
             parameters_json = EXCLUDED.parameters_json,
             parameters_type = EXCLUDED.parameters_type,
             cron_expression = EXCLUDED.cron_expression,
             job_group = EXCLUDED.job_group,
             culture = EXCLUDED.culture,
             ui_culture = EXCLUDED.ui_culture,
             perform_at = EXCLUDED.perform_at,
             attempt = 0,
             unresolvable_since = NULL
         """,
         new Dictionary<string, object?> {
            { "application_name", Configuration.ApplicationName },
            { "ids", rows.Select(x => x.Id).ToArray() },
            { "job_types", rows.Select(x => x.JobType).ToArray() },
            { "parameters_json", new TypedQueryParameter(rows.Select(x => x.ParametersJson).ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Jsonb) },
            { "parameters_types", rows.Select(x => x.ParametersType).ToArray() },
            { "cron_expressions", rows.Select(x => x.CronExpression).ToArray() },
            { "job_names", rows.Select(x => x.JobName).ToArray() },
            { "job_groups", rows.Select(x => x.JobGroup).ToArray() },
            { "cultures", rows.Select(x => x.Culture).ToArray() },
            { "ui_cultures", rows.Select(x => x.UICulture).ToArray() },
            { "perform_ats", rows.Select(x => x.PerformAt).ToArray() }
         },
         ct: ct
      );

      await JobsUpdatedChannel.NotifyAsync(Db, ct);
   }

   public async Task<JobStoreItem?> WaitForNextJobAsync(CancellationToken ct = default)
   {
      ThrowIfNotInitialized();

      // Groups whose claim a peer held during this pass. Cleared before each sleep, so a group skipped only because
      // a peer was mid-claim is tried again on the next pass.
      var contendedGroups = new HashSet<string>();

      try
      {
         while (!ct.IsCancellationRequested)
         {
            var now = _clock.UtcNow;

            _unresolvableJobSkipList.PurgeExpired(now);
            var skippedJobIds = _unresolvableJobSkipList.JobIds;

            var selectedJob = await _claimer.ClaimNextJobAsync(now, skippedJobIds, contendedGroups, ct);

            if (selectedJob is not null)
            {
               var jobStoreItem = selectedJob.ToJobStoreItem();
               if (jobStoreItem is null)
               {
                  if (selectedJob.UnresolvableSince is not null && now - selectedJob.UnresolvableSince.Value >= ResolutionGrace)
                  {
                     await DeleteExpiredUnresolvableJobAsync(selectedJob, now, ct);
                  }
                  else
                  {
                     await DeferUnresolvableJobAsync(selectedJob, now, ct);
                  }

                  continue;
               }

               if (selectedJob.UnresolvableSince is not null)
               {
                  await ClearUnresolvableStampAsync(selectedJob.Id, ct);
               }

               return jobStoreItem;
            }

            contendedGroups.Clear();
            await SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(now, ct);
         }

         return null;
      }
      catch (Exception ex) when (ex is OperationCanceledException || (ct.IsCancellationRequested && ex is QueryException { InnerException: OperationCanceledException }))
      {
         // Ignore cancellation exceptions; they are expected when the service is stopped. Db.Dapper wraps a
         // cancellation that lands mid-query in a QueryException.
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

      // Finishing a grouped job frees its group: wake waiting claims so the next job in the group runs straight away.
      if (job.Options.Group is not null)
         await JobsUpdatedChannel.NotifyAsync(Db, ct);
   }

   public async Task<bool> TryScheduleRetryAsync(JobStoreItem job, DateTime nextAttemptAtUtc, CancellationToken ct = default)
   {
      ThrowIfNotInitialized();

      var release = await GuardedClaimRelease.TryReleaseAsync(
         Db,
         """
         perform_at = :perform_at,
         attempt = attempt + 1,
         started_at = NULL,
         started_by = NULL,
         unresolvable_since = NULL
         """,
         job.JobId,
         Configuration.ApplicationName,
         job.Options.JobName,
         new Dictionary<string, object?> {
            { "perform_at", nextAttemptAtUtc }
         },
         ct
      );

      if (release.Superseded)
      {
         await SupersedeRetryAsync(job, release.SupersededByDescription, release.Conflict, ct);
         return false;
      }

      await JobsUpdatedChannel.NotifyAsync(Db, ct);
      return true;
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

      await DeleteClaimedJobAsync(job.JobId, job.Options.Group, ct);
   }

   public async Task<IEnumerable<JobStoreItem>> GetScheduledJobsAsync(CancellationToken ct = default)
   {
      ThrowIfNotInitialized();

      var jobData = await Db.Dapper.QueryAsync<JobData>(
         $"""
         SELECT {JobData.Columns}
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
         $"""
         SELECT {JobData.Columns}
         FROM mvdmio.jobs
         WHERE started_at IS NOT NULL
         ORDER BY perform_at, created_at
         """,
         ct: ct
      );

      return FilterResolvableJobs(jobData);
   }

   // Sends no jobs_updated. Delete a Claimed row through DeleteClaimedJobAsync, which wakes the claims waiting on its group.
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

   /// <summary>
   ///    Deletes a Claimed job's row. Deleting a grouped row frees its group, so waiting claims are woken to run the
   ///    next job in that group straight away.
   /// </summary>
   private async Task DeleteClaimedJobAsync(Guid jobId, string? group, CancellationToken ct)
   {
      await DeleteJobByIdAsync(jobId, ct);

      if (group is not null)
         await JobsUpdatedChannel.NotifyAsync(Db, ct);
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
   ///    <para>
   ///    The release goes through <see cref="GuardedClaimRelease"/>, so it carries the same guard the retry
   ///    reschedule uses: if another pending row already holds the same application name and job name - the
   ///    rolling-deploy shape, where a new instance re-enqueues at boot while an old instance still holds the Claim -
   ///    releasing would put two pending rows under one name, which the partial unique index forbids. In that case
   ///    the Claimed row is superseded (deleted) instead of released, leaving the newer pending row untouched.
   ///    </para>
   /// </summary>
   private async Task DeferUnresolvableJobAsync(JobData job, DateTime now, CancellationToken ct)
   {
      var release = await GuardedClaimRelease.TryReleaseAsync(
         Db,
         """
         started_at = NULL,
         started_by = NULL,
         unresolvable_since = COALESCE(unresolvable_since, :now)
         """,
         job.Id,
         Configuration.ApplicationName,
         job.JobName,
         new Dictionary<string, object?> {
            { "now", now }
         },
         ct
      );

      if (release.Superseded)
      {
         await SupersedeUnresolvableJobAsync(job, release.SupersededByDescription, release.Conflict, ct);
         return;
      }

      // Record the skip before announcing the release. The row is already unclaimed and stamped at this point, so
      // an instance that failed to record the skip - because the NOTIFY threw - would re-Claim and re-defer the
      // same job on its very next pass, which is the tight loop the skip list exists to prevent.
      _unresolvableJobSkipList.Add(job.Id, now.Add(ResolutionGrace));

      await JobsUpdatedChannel.NotifyAsync(Db, ct);

      _logger.LogDebug(
         "Job '{JobName}' (ID: {JobId}) with type '{JobType}' could not be loaded in this process. Deferring for the Resolution Grace window.",
         job.JobName,
         job.Id,
         job.JobType
      );
   }

   /// <summary>
   ///    Deletes a Claimed job whose Resolution Grace window has closed: the instance has just re-confirmed it
   ///    cannot load the job's job type or parameters type, and the row's <c>unresolvable_since</c> stamp is
   ///    already <see cref="ResolutionGrace"/> old or older. This is the only path in the library that deletes an
   ///    Unresolvable Job, and it only ever runs at a Claim where the load has just failed again - a stamp on its
   ///    own never causes a deletion. Logs the single warning this design produces.
   /// </summary>
   private async Task DeleteExpiredUnresolvableJobAsync(JobData job, DateTime now, CancellationToken ct)
   {
      await DeleteUnresolvableJobAsync(job, ct);

      _logger.LogWarning(
         "Job '{JobName}' (ID: {JobId}) with type '{JobType}' could not be loaded in this process for {UnloadableDuration}, past the Resolution Grace window. The row was deleted.",
         job.JobName,
         job.Id,
         job.JobType,
         now - job.UnresolvableSince!.Value
      );
   }

   /// <summary>
   ///    Clears a stale <c>unresolvable_since</c> stamp on a Claimed row whose job class and parameters class both
   ///    loaded successfully, before the job is handed back for execution. A stamp is never trusted on its own - it
   ///    only ever causes a deletion at a Claim where the load has just failed again - but a stamp left over from an
   ///    earlier Unresolvable episode must not survive on a row that has since proven runnable. Without this, a job
   ///    stamped by an old instance, then run and put back onto a long retry backoff by a new one, would carry a
   ///    stamp far older than the Resolution Grace window, and the next Claim by an old instance would delete it on
   ///    the spot with no grace at all.
   /// </summary>
   private async Task ClearUnresolvableStampAsync(Guid jobId, CancellationToken ct)
   {
      await Db.Dapper.ExecuteAsync(
         """
         UPDATE mvdmio.jobs
         SET unresolvable_since = NULL
         WHERE id = :id
           AND application_name = :application_name
         """,
         new Dictionary<string, object?> {
            { "id", jobId },
            { "application_name", Configuration.ApplicationName }
         },
         ct: ct
      );
   }

   /// <summary>
   ///    Drops the Claim on an Unresolvable Job whose release was refused because another pending row already holds
   ///    its application name and job name. That other row is the newer schedule and carries the same work, so the
   ///    Claimed row is deleted rather than released, leaving one pending row per job name and letting the newer
   ///    schedule win. Logs at Information, matching what the retry path already does for the same situation.
   /// </summary>
   private async Task SupersedeUnresolvableJobAsync(JobData job, string supersededByDescription, PostgresException? exception, CancellationToken ct)
   {
      _logger.LogInformation(
         exception,
         "Job '{JobName}' (ID: {JobId}) was superseded by {SupersededByDescription}; the Claim was dropped rather than released.",
         job.JobName,
         job.Id,
         supersededByDescription
      );

      await DeleteUnresolvableJobAsync(job, ct);
   }

   /// <summary>
   ///    Deletes an Unresolvable Job's row and drops its skip-list entry, so the set cannot hold an id whose job no
   ///    longer exists and cannot grow without bound.
   /// </summary>
   private async Task DeleteUnresolvableJobAsync(JobData job, CancellationToken ct)
   {
      await DeleteClaimedJobAsync(job.Id, job.JobGroup, ct);
      _unresolvableJobSkipList.Remove(job.Id);
   }

   private async Task SleepUntilWakeOrMaxWaitTimeOrNextJobPerformAt(DateTime now, CancellationToken ct)
   {
      var minPerformAt = await Db.Dapper.QueryFirstOrDefaultAsync<DateTime?>(
         $"""
         SELECT MIN(perform_at)
         FROM mvdmio.jobs
         WHERE application_name = :application_name
           AND started_at IS NULL
           AND NOT (id = ANY(:skipped_job_ids))
           AND {PostgresJobClaimer.GroupIsFreeCondition}
         """,
         new Dictionary<string, object?> {
            { "application_name", Configuration.ApplicationName },
            { "skipped_job_ids", _unresolvableJobSkipList.JobIds }
         },
         ct: ct
      );

      // Rows in the skip list and group-mates of a running job are still pending and due, but not claimable.
      // Counting them would return a time in the past and spin this loop hot: for the whole Resolution Grace
      // window, or for as long as the group is busy. Instead, bound the wait by whichever comes first: the next
      // non-skipped job coming due, or the earliest skip entry lapsing - at which point its job becomes eligible for
      // this instance's own Claim query again. Every path that frees a group (finishing, a retry reschedule,
      // releasing or deleting a Claim, resetting an instance's Claims) sends jobs_updated, which wakes this wait.
      var earliestSkipListExpiry = _unresolvableJobSkipList.EarliestExpiry;

      var nextWakeAt = minPerformAt;
      if (earliestSkipListExpiry.HasValue && (nextWakeAt is null || earliestSkipListExpiry.Value < nextWakeAt.Value))
         nextWakeAt = earliestSkipListExpiry;

      // A notification sent after the claim query but before this wait starts listening is lost, so the wait never
      // runs longer than MaxWaitTime: a lost wake-up delays a claimable job by at most that long.
      var timeUntilNextWake = nextWakeAt.HasValue && nextWakeAt.Value - now < MaxWaitTime ? nextWakeAt.Value - now : MaxWaitTime;

      // A due job this pass could not claim is held by a peer mid-claim: its row lock, its group's advisory lock, or
      // the earliest row of its group. Querying again at once would spin this loop until the peer's transaction
      // ends, so wait briefly instead. A peer that commits its claim makes the group busy, which leaves it out of the
      // query above; a peer that rolls back is retried after this short wait.
      if (timeUntilNextWake <= TimeSpan.Zero)
         timeUntilNextWake = ContendedClaimRetryDelay;

      // Use a linked cancellation token so that whichever branch loses the race in Task.WhenAny
      // is cancelled and releases its resources. Without this, Db.WaitAsync keeps a dedicated
      // LISTEN connection open until the outer cancellation token fires, leaking one connection
      // per polling iteration whenever the delay branch wins (eventually exhausting Postgres'
      // max_connections with error 53300).
      using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

      var delayTask = Task.Delay(timeUntilNextWake, waitCts.Token);
      var listenTask = Db.WaitAsync(JobsUpdatedChannel.Name, waitCts.Token);

      await Task.WhenAny(delayTask, listenTask);

      // Cancel both branches so the loser releases its resources (in particular the
      // dedicated LISTEN connection used by Db.WaitAsync) before we return. We then
      // await both tasks to ensure their cleanup (NpgsqlConnection.CloseAsync /
      // DisposeAsync inside WaitAsync) has actually run.
      await CancelAndDrainAsync(waitCts, delayTask, listenTask);
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