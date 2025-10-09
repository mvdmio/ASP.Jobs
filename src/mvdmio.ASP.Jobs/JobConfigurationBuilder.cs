using System;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Configuration options for the job runner.
/// </summary>
[PublicAPI]
public class JobConfigurationBuilder
{
   private Action<JobRunnerOptions> _jobRunnerOptionsBuilder = _ => {};
   private Action<PostgresJobStorageConfiguration> _postgresConfigurationBuilder = _ => {};
   
   internal Type JobStorageType { get; set; } = typeof(InMemoryJobStorage);
   
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
      JobStorageType = typeof(InMemoryJobStorage);
   }

   /// <summary>
   ///    Use <see cref="PostgresJobStorage" /> as the job storage and use the given connection string to connect to the database.
   /// </summary>
   /// <param name="applicationName">The name of the application. This makes sure that the current instance only picks up jobs from the same application. Useful for scenarios where the same database is used for multiple different applications.</param>
   /// <param name="connectionString">The connection string to the Postgres Database to use for storing jobs</param>
   public void UsePostgresStorage(string applicationName, string connectionString)
   {
      JobStorageType = typeof(PostgresJobStorage);
      _postgresConfigurationBuilder = options => {
         options.ApplicationName = applicationName;
         options.DatabaseConnectionString = connectionString;
      };
   }

   /// <summary>
   ///   Configure the job system.
   /// </summary>
   public void ConfigureJobRunner(Action<JobRunnerOptions> action)
   {
      _jobRunnerOptionsBuilder = action;
   }
   
   internal void SetupServices(IServiceCollection services)
   {
      services.AddSingleton<IClock>(SystemClock.Instance);
      services.Configure(_jobRunnerOptionsBuilder);
      
      services.AddSingleton(typeof(IJobStorage), JobStorageType);
      if (JobStorageType == typeof(PostgresJobStorage))
      {
         services.Configure(_postgresConfigurationBuilder);
         services.AddSingleton<PostgresJobInstanceRepository>();
         services.AddKeyedSingleton<DatabaseConnectionFactory>("Jobs");
         
         services.AddHostedService<PostgresInitializationService>();
         services.AddHostedService<PostgresCleanupService>();
      }
      
      if (IsSchedulerEnabled)
         services.AddSingleton<IJobScheduler, JobScheduler>();

      if (IsRunnerEnabled)
         services.AddHostedService<JobRunnerService>();
   }
}