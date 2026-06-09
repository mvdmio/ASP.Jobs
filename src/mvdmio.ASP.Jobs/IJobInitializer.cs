using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    The single canonical trigger for job storage Initialization (running the database migrations that ready the schema).
///    In ASP.NET hosts Initialization runs automatically at host start; non-ASP.NET hosts (or code that schedules before
///    the host has started) call <c>IServiceProvider.InitializeJobsAsync()</c>, which funnels through this component.
/// </summary>
[PublicAPI]
public interface IJobInitializer
{
   /// <summary>
   ///    Initializes the configured job storage (e.g. runs database migrations for PostgreSQL storage).
   ///    Idempotent: migrations run exactly once per process regardless of how many times this is called.
   /// </summary>
   /// <param name="ct">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   Task InitializeAsync(CancellationToken ct = default);
}
