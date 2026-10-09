using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;

namespace mvdmio.ASP.Jobs.Tests.Integration.Fixtures;

/// <summary>
///    Writes pending job rows whose job class and parameters class cannot be loaded. The scheduling API cannot create
///    such a row (every live type resolves), so it is written with raw SQL.
/// </summary>
internal static class UnresolvableJobRows
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
      CancellationToken ct = default
   )
   {
      await db.Dapper.ExecuteAsync(
         """
         INSERT INTO mvdmio.jobs (id, job_type, parameters_json, parameters_type, cron_expression, application_name, job_name, job_group, perform_at, unresolvable_since)
         VALUES (:id, :job_type, :parameters_json, :parameters_type, NULL, :application_name, :job_name, :job_group, :perform_at, :unresolvable_since)
         """,
         new Dictionary<string, object?> {
            { "id", Guid.NewGuid() },
            { "job_type", UnresolvableJobType },
            { "parameters_json", new TypedQueryParameter("{}", NpgsqlDbType.Jsonb) },
            { "parameters_type", UnresolvableParametersType },
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
