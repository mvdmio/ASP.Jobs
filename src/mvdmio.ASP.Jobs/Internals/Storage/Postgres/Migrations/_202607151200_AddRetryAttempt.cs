using System.Threading.Tasks;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Migrations;

internal sealed class _202607151200_AddRetryAttempt : IDbMigration
{
   public long Identifier { get; } = 202607151200;
   public string Name { get; } = "AddRetryAttempt";

   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         ALTER TABLE mvdmio.jobs
            ADD COLUMN attempt INT NOT NULL DEFAULT 0;
         """
      );
   }
}
