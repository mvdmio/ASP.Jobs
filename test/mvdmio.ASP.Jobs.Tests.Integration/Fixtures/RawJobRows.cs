using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;

namespace mvdmio.ASP.Jobs.Tests.Integration.Fixtures;

/// <summary>
///    Writes pending job rows with raw SQL, for rows the scheduling API cannot create: by default a row whose job class and
///    parameters class cannot be loaded (every live type resolves), and with <c>jobType</c> and <c>cronExpression</c> a row
///    as an older build of the assembly stored it, or a row without a CRON expression under a CRON row name.
/// </summary>
internal static class RawJobRows
{
   public const string UnresolvableJobType = "mvdmio.NoSuchNamespace.NoSuchJob, mvdmio.NoSuchAssembly";
   public const string UnresolvableParametersType = "mvdmio.NoSuchNamespace.NoSuchParameters, mvdmio.NoSuchAssembly";

   public static async Task InsertAsync(
      DatabaseConnection db,
      string applicationName,
      string jobName,
      DateTime performAt,
      string? group = null,
      DateTime? unresolvableSince = null,
      string jobType = UnresolvableJobType,
      string? cronExpression = null,
      CancellationToken ct = default
   )
   {
      await db.Dapper.ExecuteAsync(
         """
         INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, unresolvable_since)
         VALUES (:id, :job_type, :parameters_json, :parameters_type, :cron_expression, :application_name, :job_name, :job_group, :perform_at, :unresolvable_since)
         """,
         new Dictionary<string, object?> {
            { "id", Guid.NewGuid() },
            { "job_type", jobType },
            { "parameters_json", new TypedQueryParameter("{}", NpgsqlDbType.Jsonb) },
            { "parameters_type", UnresolvableParametersType },
            { "cron_expression", cronExpression },
            { "application_name", applicationName },
            { "job_name", jobName },
            { "job_group", group },
            { "perform_at", performAt },
            { "unresolvable_since", unresolvableSince }
         },
         ct: ct
      );
   }
}
