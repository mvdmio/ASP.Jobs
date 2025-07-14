using System.Threading.Tasks;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Migrations;

internal sealed class _202507142230_RemoveCompletedAt : IDbMigration
{
   public long Identifier { get; } = 202507142230;
   public string Name { get; } = "RemoveCompletedAt";
   
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         ALTER TABLE mvdmio.jobs
            DROP COLUMN IF EXISTS completed_at;
         """
      );
   }
}