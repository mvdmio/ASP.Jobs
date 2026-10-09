using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    Takes the Claim on the next due job of this application, so two Worker Instances claiming at the same moment
///    never both hold a Claim in one group.
/// </summary>
internal sealed class PostgresJobClaimer
{
   /// <summary>
   ///    SQL condition, on an unaliased <c>mvdmio.jobs</c> row, that holds when the row has no group or no row in the
   ///    same application and group is claimed. A group spans every Worker Instance of the application, and a claim
   ///    held by an instance that has since died still counts until that instance's claims are reset.
   /// </summary>
   internal const string GroupIsFreeCondition =
      """
      (
         job_group IS NULL
         OR NOT EXISTS (
            SELECT 1
            FROM mvdmio.jobs AS running
            WHERE running.application_name = jobs.application_name
              AND running.job_group = jobs.job_group
              AND running.started_at IS NOT NULL
         )
      )
      """;

   /// <summary>
   ///    The two arguments of the advisory lock that serialises claims within one group of one application, bound to
   ///    <c>:application_name</c> and <c>:job_group</c>.
   /// </summary>
   internal const string GroupLockKeyArguments = "hashtext(:application_name), hashtext(:job_group)";

   private readonly Func<DatabaseConnection> _buildConnection;
   private readonly IOptions<PostgresJobStorageConfiguration> _configuration;

   private PostgresJobStorageConfiguration Configuration => _configuration.Value;

   /// <param name="buildConnection">Builds a new connection wrapper; one wrapper carries one claim transaction.</param>
   /// <param name="configuration">The storage configuration that names this application and Worker Instance.</param>
   public PostgresJobClaimer(Func<DatabaseConnection> buildConnection, IOptions<PostgresJobStorageConfiguration> configuration)
   {
      _buildConnection = buildConnection;
      _configuration = configuration;
   }

   /// <summary>
   ///    Claims the next due job in one transaction. The candidate is row-locked with <c>SKIP LOCKED</c>. A grouped
   ///    candidate is claimed only once this transaction holds its group's advisory lock and a new statement still
   ///    finds no started group-mate; otherwise its group is added to <paramref name="contendedGroups"/> and the pick
   ///    runs again without it.
   /// </summary>
   /// <param name="now">The claim time, written to <c>started_at</c>.</param>
   /// <param name="skippedJobIds">This instance's Unresolvable Jobs, which are not claimed.</param>
   /// <param name="contendedGroups">Groups a peer held during this claim pass; read and added to.</param>
   /// <param name="ct">The cancellation token.</param>
   /// <returns>The claimed row, or null when no job is claimable in this pass.</returns>
   public async Task<JobData?> ClaimNextJobAsync(DateTime now, Guid[] skippedJobIds, ISet<string> contendedGroups, CancellationToken ct)
   {
      while (true)
      {
         // One wrapper for the whole transaction: the row lock and the advisory lock belong to its connection.
         var db = _buildConnection();
         var attempt = await db.InTransactionAsync(async () => await TryClaimCandidateAsync(db, now, skippedJobIds, contendedGroups, ct));

         if (attempt.ContendedGroup is null)
            return attempt.Claimed;

         contendedGroups.Add(attempt.ContendedGroup);
      }
   }

   /// <summary>
   ///    One claim attempt, run inside the caller's transaction on <paramref name="db"/>. A refused attempt writes
   ///    nothing, so ending its transaction only releases the locks it took.
   /// </summary>
   private async Task<ClaimAttempt> TryClaimCandidateAsync(DatabaseConnection db, DateTime now, Guid[] skippedJobIds, ISet<string> contendedGroups, CancellationToken ct)
   {
      // A grouped job is a candidate only while no earlier pending job of its group waits. Without that rule, SKIP
      // LOCKED would step past the earliest row while a peer holds it mid-claim, and take the next one in the group.
      var candidate = await db.Dapper.QueryFirstOrDefaultAsync<JobData>(
         $"""
         SELECT {JobData.Columns}
         FROM mvdmio.jobs
         WHERE application_name = :application_name
           AND perform_at <= :now
           AND started_at IS NULL
           AND NOT (id = ANY(:skipped_job_ids))
           AND (job_group IS NULL OR NOT (job_group = ANY(:contended_groups)))
           AND {GroupIsFreeCondition}
           AND (
              job_group IS NULL
              OR NOT EXISTS (
                 SELECT 1
                 FROM mvdmio.jobs AS earlier
                 WHERE earlier.application_name = jobs.application_name
                   AND earlier.job_group = jobs.job_group
                   AND earlier.started_at IS NULL
                   AND NOT (earlier.id = ANY(:skipped_job_ids))
                   AND (earlier.perform_at, earlier.created_at, earlier.id) < (jobs.perform_at, jobs.created_at, jobs.id)
              )
           )
         ORDER BY perform_at, created_at, id
         LIMIT 1
         FOR UPDATE SKIP LOCKED
         """,
         new Dictionary<string, object?> {
            { "now", now },
            { "application_name", Configuration.ApplicationName },
            { "skipped_job_ids", skippedJobIds },
            { "contended_groups", contendedGroups.ToArray() }
         },
         ct: ct
      );

      if (candidate is null)
         return ClaimAttempt.NothingClaimable;

      if (candidate.JobGroup is not null && !await TryLockFreeGroupAsync(db, candidate, ct))
         return ClaimAttempt.Contended(candidate.JobGroup);

      var claimed = await db.Dapper.QueryFirstAsync<JobData>(
         $"""
         UPDATE mvdmio.jobs
         SET started_at = :now,
             started_by = :instance_id
         WHERE id = :id
         RETURNING {JobData.Columns}
         """,
         new Dictionary<string, object?> {
            { "now", now },
            { "instance_id", Configuration.InstanceId },
            { "id", candidate.Id }
         },
         ct: ct
      );

      return ClaimAttempt.Succeeded(claimed);
   }

   /// <summary>
   ///    Takes the candidate's group advisory lock for the rest of the transaction without waiting, then checks in a
   ///    new statement, which sees every claim a peer has committed, that the group is still free.
   /// </summary>
   /// <returns>True when the lock is held and the group is free; false when a peer holds the lock or a claim.</returns>
   private async Task<bool> TryLockFreeGroupAsync(DatabaseConnection db, JobData candidate, CancellationToken ct)
   {
      var locked = await db.Dapper.QueryFirstAsync<bool>(
         $"SELECT pg_try_advisory_xact_lock({GroupLockKeyArguments})",
         new Dictionary<string, object?> {
            { "application_name", Configuration.ApplicationName },
            { "job_group", candidate.JobGroup }
         },
         ct: ct
      );

      if (!locked)
         return false;

      return await db.Dapper.QueryFirstAsync<bool>(
         $"""
         SELECT {GroupIsFreeCondition}
         FROM mvdmio.jobs
         WHERE id = :id
         """,
         new Dictionary<string, object?> {
            { "id", candidate.Id }
         },
         ct: ct
      );
   }

   /// <summary>
   ///    The result of one claim attempt: the claimed row, the group a peer held, or neither when nothing is
   ///    claimable. Built only through its three named outcomes, so at most one of the two is set.
   /// </summary>
   private readonly record struct ClaimAttempt
   {
      public static ClaimAttempt NothingClaimable => default;

      public JobData? Claimed { get; private init; }

      public string? ContendedGroup { get; private init; }

      public static ClaimAttempt Succeeded(JobData claimed) => new() { Claimed = claimed };

      public static ClaimAttempt Contended(string jobGroup) => new() { ContendedGroup = jobGroup };
   }
}
