using System;
using System.Collections.Concurrent;
using System.Linq;

namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    The job ids one Worker Instance recently found Unresolvable, each entry expiring at the close of that job's
///    Resolution Grace window.
///    <para>
///    Its only purpose is to stop the instance Claiming a job it just failed to load, over and over in a tight loop:
///    the ids here are excluded from that instance's own Claim query. A peer instance is unaffected and can Claim the
///    job immediately. The entry expiring is what causes the eventual deletion - once it lapses the instance Claims
///    the job again, confirms it still cannot load the type, finds the stamp old enough, and deletes the row.
///    </para>
///    <para>
///    Entries are dropped once they expire and when their job is deleted, so the set cannot grow without bound. It
///    lives and dies with the process; nothing about correctness depends on it surviving a restart, because a
///    restarted instance simply Claims the job again and decides from the row's stamp alone.
///    </para>
/// </summary>
internal sealed class UnresolvableJobSkipList
{
   private readonly ConcurrentDictionary<Guid, DateTime> _expiryByJobId = new();

   /// <summary>
   ///    Gets the storage-clock time at which the earliest entry lapses, or null when the list is empty.
   /// </summary>
   public DateTime? EarliestExpiry => _expiryByJobId.IsEmpty ? null : _expiryByJobId.Values.Min();

   /// <summary>
   ///    Gets the job ids currently skipped, as a snapshot safe to pass to a query.
   /// </summary>
   public Guid[] JobIds => _expiryByJobId.Keys.ToArray();

   /// <summary>
   ///    Skips <paramref name="jobId"/> until <paramref name="expiresAt"/> on the storage clock.
   /// </summary>
   public void Add(Guid jobId, DateTime expiresAt)
   {
      _expiryByJobId[jobId] = expiresAt;
   }

   /// <summary>
   ///    Stops skipping <paramref name="jobId"/>, if it was skipped at all.
   /// </summary>
   public void Remove(Guid jobId)
   {
      _expiryByJobId.TryRemove(jobId, out _);
   }

   /// <summary>
   ///    Drops every entry whose Resolution Grace window has lapsed by <paramref name="now"/>, so the set cannot grow
   ///    without bound and a lapsed job becomes eligible for this instance's own Claim query again.
   /// </summary>
   public void PurgeExpired(DateTime now)
   {
      foreach (var (jobId, expiresAt) in _expiryByJobId)
      {
         if (expiresAt <= now)
            _expiryByJobId.TryRemove(jobId, out _);
      }
   }
}
