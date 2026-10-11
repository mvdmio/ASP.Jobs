using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    Removes the CRON rows that a new CRON Schedule of the same Job class replaces.
/// </summary>
internal static class ReplacedCronRows
{
   /// <summary>
   ///    Deletes every row of <paramref name="applicationName"/> that carries a CRON expression, has not been started, is
   ///    a CRON row of the class of <paramref name="jobItem"/> (<see cref="CronJobNames.IsOfClass"/>) and has another name
   ///    than <paramref name="jobItem"/>. Runs on <paramref name="db"/>, so a caller can run it inside its own transaction.
   /// </summary>
   public static async Task DeleteAsync(DatabaseConnection db, string applicationName, JobStoreItem jobItem, CancellationToken ct)
   {
      // The query narrows the rows by name prefix (starts_with, not LIKE: a class name may hold '_' and '%', which LIKE
      // reads as wildcards); CronJobNames.IsOfClass then decides which of them belong to the class.
      var candidateNames = await db.Dapper.QueryAsync<string>(
         """
         SELECT job_name
         FROM mvdmio.jobs
         WHERE application_name = :application_name
           AND started_at IS NULL
           AND cron_expression IS NOT NULL
           AND job_name <> :job_name
           AND starts_with(job_name, :prefix)
         """,
         new Dictionary<string, object?> {
            { "application_name", applicationName },
            { "job_name", jobItem.Options.JobName },
            { "prefix", CronJobNames.PrefixFor(jobItem.JobType) }
         },
         ct: ct
      );

      var replacedNames = candidateNames.Where(x => CronJobNames.IsOfClass(x, jobItem.JobType)).ToArray();

      // The delete repeats the started_at test: a Worker Instance may have claimed a row since the query.
      await db.Dapper.ExecuteAsync(
         """
         DELETE FROM mvdmio.jobs
         WHERE application_name = :application_name
           AND started_at IS NULL
           AND cron_expression IS NOT NULL
           AND job_name = ANY(:job_names)
         """,
         new Dictionary<string, object?> {
            { "application_name", applicationName },
            { "job_names", replacedNames }
         },
         ct: ct
      );
   }
}
