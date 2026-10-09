using System.Threading.Tasks;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Migrations;

internal sealed class _202610091200_AddJobGroupStartedIndex : IDbMigration
{
   public long Identifier { get; } = 202610091200;
   public string Name { get; } = "AddJobGroupStartedIndex";

   public async Task UpAsync(DatabaseConnection db)
   {
      // Not unique: tables written before groups were enforced can already hold several started rows in one group.
      await db.Dapper.ExecuteAsync(
         """
         CREATE INDEX idx_jobs__application__job_group__started ON mvdmio.jobs (application_name, job_group) WHERE started_at IS NOT NULL AND job_group IS NOT NULL;
         """
      );
   }
}
