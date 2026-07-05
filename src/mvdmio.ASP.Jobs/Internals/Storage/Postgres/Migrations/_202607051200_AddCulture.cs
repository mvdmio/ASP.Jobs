using System.Threading.Tasks;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Migrations;

internal sealed class _202607051200_AddCulture : IDbMigration
{
   public long Identifier { get; } = 202607051200;
   public string Name { get; } = "AddCulture";

   public async Task UpAsync(DatabaseConnection db)
   {
      // Nullable: existing jobs (scheduled before culture capture existed) carry no Captured Culture,
      // which is distinct from a captured invariant culture (stored as the empty string).
      await db.Dapper.ExecuteAsync(
         """
         ALTER TABLE mvdmio.jobs
            ADD COLUMN culture TEXT,
            ADD COLUMN ui_culture TEXT;
         """
      );
   }
}
