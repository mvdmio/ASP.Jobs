using System.Threading.Tasks;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Migrations;

internal sealed class _202507171100_AddStartedBy : IDbMigration
{
   public long Identifier { get; } = 202507171100;
   public string Name { get; } = "AddStartedBy";
   
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE TABLE mvdmio.job_instances (
             instance_id  TEXT        NOT NULL PRIMARY KEY,
             last_seen_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
         );

         ALTER TABLE mvdmio.jobs
            ADD COLUMN started_by TEXT NULL REFERENCES mvdmio.job_instances(instance_id);
         """
      );

      await db.Dapper.ExecuteAsync(
         """
         DELETE FROM mvdmio.jobs
         WHERE started_by IS NULL;
         """
      );
   }
}