using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace mvdmio.ASP.Jobs.Internals;

/// <summary>
///    Hosted service that triggers job storage Initialization at host start. Implemented as an
///    <see cref="IHostedLifecycleService"/> so that <see cref="StartingAsync"/> runs for all hosted services before any
///    <see cref="IHostedService.StartAsync"/> — guaranteeing Initialization precedes Instance Registration, the job
///    runner's first storage access, and request handling, independent of service registration order.
///    Failures propagate and abort host startup (fail-fast). Registered for all backends (a no-op for InMemory storage).
/// </summary>
internal sealed class JobInitializationHostedService : IHostedLifecycleService
{
   private readonly IJobInitializer _initializer;

   public JobInitializationHostedService(IJobInitializer initializer)
   {
      _initializer = initializer;
   }

   /// <inheritdoc />
   public Task StartingAsync(CancellationToken cancellationToken)
   {
      return _initializer.InitializeAsync(cancellationToken);
   }

   /// <inheritdoc />
   public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

   /// <inheritdoc />
   public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

   /// <inheritdoc />
   public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

   /// <inheritdoc />
   public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

   /// <inheritdoc />
   public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
