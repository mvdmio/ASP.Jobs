using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    Hosted service that handles PostgreSQL job storage initialization and cleanup on application start/stop.
///    Registers the current instance and releases any claimed jobs on shutdown.
/// </summary>
internal sealed class PostgresInitializationService : IHostedService
{
   private readonly DatabaseConnection _db;
   private readonly PostgresJobInstanceRepository _repository;

   /// <summary>
   ///    Initializes a new instance of the <see cref="PostgresInitializationService"/> class.
   /// </summary>
   /// <param name="dbConnectionFactory">The database connection factory.</param>
   /// <param name="configuration">The PostgreSQL storage configuration.</param>
   /// <param name="repository">The job instance repository.</param>
   public PostgresInitializationService(
      [FromKeyedServices("Jobs")] DatabaseConnectionFactory dbConnectionFactory,
      IOptions<PostgresJobStorageConfiguration> configuration,
      PostgresJobInstanceRepository repository
   ) {
      _db = dbConnectionFactory.ForConnectionString(configuration.Value.DatabaseConnectionString);
      _repository = repository;
   }
   
   /// <inheritdoc />
   public async Task StartAsync(CancellationToken cancellationToken)
   {
      await _repository.RegisterInstance(cancellationToken);
   }

   /// <inheritdoc />
   public async Task StopAsync(CancellationToken cancellationToken)
   {
      await _repository.ReleaseStartedJobs(cancellationToken);
      await _repository.UnregisterInstance(cancellationToken);
   }
}