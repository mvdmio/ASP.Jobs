using System;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Thrown when job storage is accessed (e.g. a job is scheduled or queried) before Initialization has completed
///    in the current process. This is the fail-fast Initialization Guard: rather than producing a cryptic SQL error
///    against a missing schema, it tells the caller to initialize first.
/// </summary>
[PublicAPI]
public sealed class JobStorageNotInitializedException : InvalidOperationException
{
   /// <summary>
   ///    Initializes a new instance of the <see cref="JobStorageNotInitializedException"/> class.
   /// </summary>
   public JobStorageNotInitializedException()
      : base(
         "Job storage has not been initialized. In ASP.NET hosts Initialization runs automatically at host start; " +
         "if you schedule jobs before the host has started, or you run in a non-ASP.NET host, " +
         "call IServiceProvider.InitializeJobsAsync() before scheduling or querying jobs."
      )
   {
   }
}
