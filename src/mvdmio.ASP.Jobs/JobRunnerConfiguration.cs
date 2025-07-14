using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Configuration options for the job runner.
/// </summary>
[PublicAPI]
public class JobRunnerConfiguration
{
   internal IJobStorage? JobStorage { get; set; }

   /// <summary>
   ///    Flag to enable or disable the job scheduler. Default to true.
   /// </summary>
   public bool IsSchedulerEnabled { get; set; } = true;

   /// <summary>
   ///    Flag to enable or disable the job runner. Default to true.
   /// </summary>
   public bool IsRunnerEnabled { get; set; } = true;

   /// <summary>
   ///    Use <see cref="InMemoryJobStorage" /> as the job storage.
   /// </summary>
   public void UseInMemoryStorage()
   {
      JobStorage = new InMemoryJobStorage();
   }

   /// <summary>
   ///    Use <see cref="PostgresJobStorage" /> as the job storage and use the given connection string to connect to the database.
   /// </summary>
   public void UsePostgresStorage(string connectionString)
   {
      var postgresConfiguration = new PostgresJobStorageConfiguration {
         DatabaseConnection = new DatabaseConnectionFactory().ForConnectionString(connectionString)
      };
      
      JobStorage = new PostgresJobStorage(postgresConfiguration, SystemClock.Instance);
   }

   internal void SetupServices(IServiceCollection services)
   {
      services.AddSingleton<IClock>(SystemClock.Instance);
      
      if (JobStorage is null or InMemoryJobStorage)
      {
         services.AddSingleton<IJobStorage>(JobStorage ?? new InMemoryJobStorage());   
      }
      else if (JobStorage is PostgresJobStorage postgres)
      {
         services.AddSingleton<IJobStorage>(postgres);
         services.AddSingleton<PostgresJobStorageConfiguration>(postgres.Configuration);
         services.AddHostedService<PostgresMigrationService>();
      }

      if (IsSchedulerEnabled)
         services.AddSingleton<IJobScheduler, JobScheduler>();

      if (IsRunnerEnabled)
         services.AddHostedService<JobRunnerService>();
   }
}