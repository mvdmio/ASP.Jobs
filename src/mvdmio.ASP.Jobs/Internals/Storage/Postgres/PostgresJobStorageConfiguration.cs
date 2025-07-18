using System;
using JetBrains.Annotations;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    Configuration options for the Postgres job storage.
/// </summary>
[PublicAPI]
public sealed class PostgresJobStorageConfiguration
{
   /// <summary>
   ///   The ID of the current instance running or scheduling jobs. Defaults to the machine name but falls back to a GUID if the machine name is empty.
   /// </summary>
   public string InstanceId { get; internal set; } = string.IsNullOrEmpty(Environment.MachineName) ? Guid.NewGuid().ToString() : Environment.MachineName;
   
   /// <summary>
   ///   The name of the application. Used so that only jobs from the same application are processed by this instance.
   /// </summary>
   public required string ApplicationName { get; set; }
   
   /// <summary>
   ///    The database connection to use for the job storage.
   /// </summary>
   public required DatabaseConnection DatabaseConnection { get; set; }
}