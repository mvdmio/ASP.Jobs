using System;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;
using mvdmio.ASP.Jobs.Utils;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Builder class for configuring the job system, including storage options and runner settings.
/// </summary>
[PublicAPI]
public class JobConfigurationBuilder
{
   private Action<JobRunnerOptions> _jobRunnerOptionsBuilder = _ => {};
   private Action<PostgresJobStorageConfiguration> _postgresConfigurationBuilder = _ => {};
   
   internal Type JobStorageType { get; set; } = typeof(InMemoryJobStorage);
   
   /// <summary>
   ///    Gets or sets a value indicating whether the job scheduler is enabled. Defaults to true.
   /// </summary>
   public bool IsSchedulerEnabled { get; set; } = true;

   /// <summary>
   ///    Gets or sets a value indicating whether the job runner is enabled. Defaults to true.
   /// </summary>
   public bool IsRunnerEnabled { get; set; } = true;
   
   /// <summary>
   ///    Configures the job system to use <see cref="InMemoryJobStorage" /> as the job storage.
   ///    This is the default storage option and is suitable for testing or single-instance deployments.
   /// </summary>
   public void UseInMemoryStorage()
   {
      JobStorageType = typeof(InMemoryJobStorage);
   }

   /// <summary>
   ///    Configures the job system to use <see cref="PostgresJobStorage" /> as the job storage.
   ///    The job storage's connection pool is named after the entry assembly plus <c>.Jobs</c>, or <paramref name="applicationName" /> plus <c>.Jobs</c> when there is no entry assembly.
   /// </summary>
   /// <param name="applicationName">The name of the application. Ensures that the current instance only picks up jobs from the same application. Useful for scenarios where the same database is used for multiple different applications.</param>
   /// <param name="connectionString">The connection string to the PostgreSQL database to use for storing jobs.</param>
   public void UsePostgresStorage(string applicationName, string connectionString)
   {
      JobStorageType = typeof(PostgresJobStorage);
      _postgresConfigurationBuilder = options => {
         options.ApplicationName = applicationName;
         options.DatabaseConnectionString = connectionString;
      };
   }

   /// <summary>
   ///    Configures the job runner with custom options.
   /// </summary>
   /// <param name="action">An action to configure the <see cref="JobRunnerOptions"/>.</param>
   public void ConfigureJobRunner(Action<JobRunnerOptions> action)
   {
      _jobRunnerOptionsBuilder = action;
   }
   
   internal void SetupServices(IServiceCollection services)
   {
      services.AddSingleton<IClock>(SystemClock.Instance);
      services.Configure(_jobRunnerOptionsBuilder);
      
      services.AddSingleton(typeof(IJobStorage), JobStorageType);

      // Initialization is backend-agnostic: a single canonical trigger (IJobInitializer) plus a hosted service
      // that runs it in StartingAsync (before any StartAsync). Registered for all backends; a no-op for InMemory.
      services.AddSingleton<IJobInitializer, JobInitializer>();
      services.AddHostedService<JobInitializationHostedService>();

      if (JobStorageType == typeof(PostgresJobStorage))
      {
         services.Configure(_postgresConfigurationBuilder);
         services.AddSingleton<PostgresJobInstanceRepository>();
         services.AddKeyedSingleton<DatabaseConnectionFactory>("Jobs", (serviceProvider, _) => {
            var configuration = serviceProvider.GetRequiredService<IOptions<PostgresJobStorageConfiguration>>().Value;
            var poolName = JobsPoolName.Create(Assembly.GetEntryAssembly()?.GetName().Name, configuration.ApplicationName);

            // No MaxPoolSize: the connection string's Maximum Pool Size applies, or else the Database.PgSQL default.
            return new DatabaseConnectionFactory(new DatabaseConnectionFactorySettings { ApplicationName = poolName });
         });

         services.AddHostedService<PostgresInstanceRegistrationService>();
         services.AddHostedService<PostgresCleanupService>();
      }
      
      if (IsSchedulerEnabled)
         services.AddSingleton<IJobScheduler, JobScheduler>();

      if (IsRunnerEnabled)
         services.AddHostedService<JobRunnerService>();
   }
}