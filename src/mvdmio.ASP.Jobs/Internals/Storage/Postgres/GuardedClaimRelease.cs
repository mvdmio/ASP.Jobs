using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Exceptions;
using Npgsql;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    The one way a Worker Instance gives up a Claim on a job row without deleting it - used both by the retry
///    reschedule and by the deferral of an Unresolvable Job, which need the same guard for the same reason.
/// </summary>
internal static class GuardedClaimRelease
{
   /// <summary>
   ///    Runs a guarded <c>UPDATE</c> against one Claimed row: <paramref name="setClause"/> is applied only when no
   ///    other pending row already holds the same application name and job name. The partial unique index on those
   ///    two columns for unstarted rows forbids two, so a release that would leave a second pending row under one
   ///    name has to be refused. When it is refused, that other row is the newer schedule and carries the same work,
   ///    so the caller supersedes the Claimed row rather than releasing it.
   ///    <para>
   ///    A unique violation arriving concurrently - an insert landing between the <c>NOT EXISTS</c> check and the
   ///    commit - is caught and reported as the same supersession. Db.Dapper wraps the driver exception in a
   ///    <see cref="QueryException"/>, so the <see cref="PostgresException"/> is unwrapped from its inner exception.
   ///    </para>
   /// </summary>
   /// <param name="db">The connection to run the statement on.</param>
   /// <param name="setClause">
   ///    The body of the <c>SET</c> list, without the <c>SET</c> keyword. Always a literal written by the calling
   ///    storage - never a value from a job, a caller, or the database - so it is safe to interpolate.
   /// </param>
   /// <param name="jobId">The Claimed row to update. Bound as <c>:id</c>.</param>
   /// <param name="applicationName">The application that owns the row. Bound as <c>:application_name</c>.</param>
   /// <param name="jobName">The job name the guard looks for. Bound as <c>:job_name</c>.</param>
   /// <param name="extraParameters">Any further parameters <paramref name="setClause"/> references.</param>
   /// <param name="ct">The cancellation token.</param>
   public static async Task<GuardedClaimReleaseResult> TryReleaseAsync(
      DatabaseConnection db,
      string setClause,
      Guid jobId,
      string applicationName,
      string jobName,
      Dictionary<string, object?> extraParameters,
      CancellationToken ct
   ) {
      var parameters = new Dictionary<string, object?>(extraParameters) {
         { "id", jobId },
         { "application_name", applicationName },
         { "job_name", jobName }
      };

      try
      {
         var updatedId = await db.Dapper.QueryFirstOrDefaultAsync<Guid?>(
            $"""
             UPDATE mvdmio.jobs
             SET {setClause}
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
            parameters,
            ct: ct
         );

         return new GuardedClaimReleaseResult(updatedId, "a newer pending job of the same name", null);
      }
      catch (QueryException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
      {
         return new GuardedClaimReleaseResult(null, "a concurrently-scheduled job of the same name", pg);
      }
   }
}

/// <summary>
///    The outcome of <see cref="GuardedClaimRelease.TryReleaseAsync"/>: either the Claimed row was updated, or
///    another pending row under the same job name superseded it.
/// </summary>
/// <param name="UpdatedId">The id of the updated row, or null when the release was refused.</param>
/// <param name="SupersededByDescription">Names which shape of supersession refused the release, for the log line.</param>
/// <param name="Conflict">The unique violation, when the conflicting row landed concurrently; null otherwise.</param>
internal readonly record struct GuardedClaimReleaseResult(Guid? UpdatedId, string SupersededByDescription, PostgresException? Conflict)
{
   /// <summary>
   ///    Gets a value indicating whether the release was refused because another pending row holds the job name.
   /// </summary>
   public bool Superseded => UpdatedId is null;
}
