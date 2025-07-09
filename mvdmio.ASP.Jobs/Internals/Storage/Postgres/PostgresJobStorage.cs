using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    Configuration options for the Postgres job storage.
/// </summary>
[PublicAPI]
public sealed class PostgresJobStorageConfiguration
{
   /// <summary>
   ///    The connection string to the Postgres database.
   /// </summary>
   public required string ConnectionString { get; set; }
}

internal sealed class PostgresJobStorage : IJobStorage
{
   private DatabaseConnection _db;
   
   public PostgresJobStorageConfiguration Configuration { get; }

   public PostgresJobStorage(PostgresJobStorageConfiguration configuration)
   {
      Configuration = configuration;
      
      _db = new DatabaseConnection(configuration.ConnectionString);
   }

   public Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default)
   {
      return ScheduleJobsAsync([jobItem], ct);
   }

   public Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default)
   {
      throw new NotImplementedException();
   }

   public Task FinalizeJobAsync(string jobId, CancellationToken ct = default)
   {
      throw new NotImplementedException();
   }

   public Task<JobStoreItem?> StartNextJobAsync(CancellationToken ct)
   {
      throw new NotImplementedException();
   }
}