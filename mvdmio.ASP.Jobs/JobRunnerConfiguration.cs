using JetBrains.Annotations;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

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
   ///    Use <see cref="PostgresJobStorageConfiguration" /> as the job storage.
   /// </summary>
   public void UsePostgresStorage(PostgresJobStorageConfiguration configuration)
   {
      JobStorage = new PostgresJobStorage(configuration);
   }
}