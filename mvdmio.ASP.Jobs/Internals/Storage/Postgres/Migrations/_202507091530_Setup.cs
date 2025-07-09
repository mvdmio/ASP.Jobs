using System.Threading.Tasks;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres.Migrations;

internal sealed class _202507091530_Setup : IDbMigration
{
   public long Identifier { get; } = 202507091530;
   public string Name { get; } = "Setup";
   
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE SCHEMA IF NOT EXISTS mvdmio;
         
         CREATE TABLE IF NOT EXISTS mvdmio.jobs (
            created_at      TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
            id              BIGINT      NOT NULL GENERATED ALWAYS AS IDENTITY,
            job_type        TEXT        NOT NULL,
            parameters_json JSONB       NOT NULL,
            parameters_type TEXT        NOT NULL,
            cron_expression TEXT        NULL,
            job_name        TEXT        NOT NULL,
            job_group       TEXT        NULL,
            perform_at      TIMESTAMPTZ NOT NULL,
            started_at      TIMESTAMPTZ NULL,
            completed_at    TIMESTAMPTZ NULL,
            PRIMARY KEY (id)
         );
         
         CREATE OR REPLACE FUNCTION mvdmio.trigger_update_last_updated_at() 
         RETURNS TRIGGER LANGUAGE plpgsql AS
         $$
         BEGIN
            new.last_updated_at = CLOCK_TIMESTAMP();
            RETURN new;
         END;
         $$;
         
         CREATE TRIGGER update_last_updated_at
         BEFORE UPDATE ON mvdmio.jobs
         FOR EACH ROW EXECUTE
            PROCEDURE mvdmio.trigger_update_last_updated_at();
         """
      );
   }
}