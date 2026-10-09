using System.Threading;
using System.Threading.Tasks;
using mvdmio.Database.PgSQL;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    The PostgreSQL notification channel that wakes waiting claims. A write that may make a job claimable sends a
///    notification on it; a claim with nothing to claim listens on it.
/// </summary>
internal static class JobsUpdatedChannel
{
   /// <summary>
   ///    The channel name used by <c>NOTIFY</c> and <c>LISTEN</c>.
   /// </summary>
   public const string Name = "jobs_updated";

   /// <summary>
   ///    Sends a notification on the channel, waking every claim that is waiting on it.
   /// </summary>
   public static async Task NotifyAsync(DatabaseConnection db, CancellationToken ct)
   {
      await db.Dapper.ExecuteAsync($"NOTIFY {Name}", ct: ct);
   }
}
