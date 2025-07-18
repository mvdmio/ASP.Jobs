using System.Threading.Tasks;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Migrations;

internal sealed class _202507181100_AddApplicationName : IDbMigration
{
   public long Identifier { get; } = 202507181100;
   public string Name { get; } = "AddApplicationName";
   
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         TRUNCATE mvdmio.jobs, mvdmio.job_instances;
         
         ALTER TABLE mvdmio.job_instances
            ADD COLUMN application_name TEXT NOT NULL;

         ALTER TABLE mvdmio.jobs
            ADD COLUMN application_name TEXT NOT NULL;
            
         DROP INDEX mvdmio.idx_jobs__perform_at__created_at;
         CREATE INDEX idx_jobs__application__perform_at__created_at ON mvdmio.jobs (application_name, perform_at, created_at);
         
         -- Unique index for on-conflict queries so that only one job with the same name can be not-started at the same time.
         DROP INDEX mvdmio.idxu_jobs__job_name__not_started;
         CREATE UNIQUE INDEX idxu_jobs__application__job_name__not_started ON mvdmio.jobs (application_name, job_name) WHERE started_at IS NULL;
         """
      );
   }
}