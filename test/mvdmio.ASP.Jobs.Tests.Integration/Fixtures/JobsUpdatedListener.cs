using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using Npgsql;

namespace mvdmio.ASP.Jobs.Tests.Integration.Fixtures;

/// <summary>
/// Listens on the <c>jobs_updated</c> channel on its own connection and counts the notifications it receives.
/// Start it before the write under test, then call <see cref="CountNotificationsAsync"/> to count what arrives
/// within a short window. Proves how many notifications a write sent, which a single wait cannot.
/// </summary>
internal sealed class JobsUpdatedListener : IAsyncDisposable
{
   private readonly NpgsqlConnection _connection;
   private int _count;

   private JobsUpdatedListener(NpgsqlConnection connection)
   {
      _connection = connection;
      _connection.Notification += (_, _) => Interlocked.Increment(ref _count);
   }

   public static async Task<JobsUpdatedListener> StartAsync(string connectionString, CancellationToken ct)
   {
      var connection = new NpgsqlConnection(connectionString);
      await connection.OpenAsync(ct);

      await using (var command = new NpgsqlCommand($"LISTEN {JobsUpdatedChannel.Name}", connection))
      {
         await command.ExecuteNonQueryAsync(ct);
      }

      return new JobsUpdatedListener(connection);
   }

   /// <summary>
   /// Processes notifications for <paramref name="window"/> and returns how many have arrived since the listener
   /// started. Call it once: ending the window cancels the wait, which leaves the connection unusable.
   /// </summary>
   public async Task<int> CountNotificationsAsync(TimeSpan window, CancellationToken ct)
   {
      // A timed WaitAsync with no notification arriving hung past its timeout here, so the window is enforced by
      // cancellation instead.
      using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      windowCts.CancelAfter(window);

      try
      {
         while (true)
         {
            await _connection.WaitAsync(windowCts.Token);
         }
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
         // The window has ended.
      }

      return Volatile.Read(ref _count);
   }

   public async ValueTask DisposeAsync()
   {
      await _connection.DisposeAsync();
   }
}
