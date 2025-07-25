﻿using System.Threading.Tasks;
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
            created_at          TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_updated_at     TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
            id                  UUID        NOT NULL,
            job_type            TEXT        NOT NULL,
            parameters_type     TEXT        NOT NULL,
            parameters_json     JSONB       NOT NULL,
            cron_expression     TEXT        NULL,
            job_name            TEXT        NOT NULL,
            job_group           TEXT        NULL,
            perform_at          TIMESTAMPTZ NOT NULL,
            started_at          TIMESTAMPTZ NULL,
            completed_at        TIMESTAMPTZ NULL,
            PRIMARY KEY (id),
            CHECK ((started_at IS NULL AND completed_at IS NULL) OR (started_at IS NOT NULL AND completed_at IS NULL) OR (completed_at >= started_at))
         );
         
         CREATE INDEX idx_jobs__perform_at__created_at ON mvdmio.jobs (perform_at, created_at);
         
         -- Unique index for on-conflict queries so that only one job with the same name can be not-started at the same time.
         CREATE UNIQUE INDEX idxu_jobs__job_name__not_started ON mvdmio.jobs (job_name) WHERE started_at IS NULL;
         
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