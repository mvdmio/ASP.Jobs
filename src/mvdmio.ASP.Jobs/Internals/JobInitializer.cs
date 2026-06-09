using System.Threading;
using System.Threading.Tasks;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;

namespace mvdmio.ASP.Jobs.Internals;

/// <summary>
///    Default <see cref="IJobInitializer"/>. Delegates Initialization to the configured <see cref="IJobStorage"/>.
///    Host- and backend-agnostic; the backend-specific work lives in <see cref="IJobStorage.InitializeAsync"/>.
/// </summary>
internal sealed class JobInitializer : IJobInitializer
{
   private readonly IJobStorage _storage;

   public JobInitializer(IJobStorage storage)
   {
      _storage = storage;
   }

   /// <inheritdoc />
   public Task InitializeAsync(CancellationToken ct = default)
   {
      return _storage.InitializeAsync(ct);
   }
}
