using System.Threading.Tasks;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Migrations;

internal sealed class _202609151200_AddUnresolvableSince : IDbMigration
{
   public long Identifier { get; } = 202609151200;
   public string Name { get; } = "AddUnresolvableSince";

   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         ALTER TABLE mvdmio.jobs
            ADD COLUMN unresolvable_since TIMESTAMPTZ NULL;
         """
      );
   }
}
