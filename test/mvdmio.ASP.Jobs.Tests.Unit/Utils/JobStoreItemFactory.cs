using mvdmio.ASP.Jobs.Internals.Storage.Data;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Shared factory for building <see cref="JobStoreItem"/> instances in tests.
/// Centralizes the canonical test job shape so updates to <see cref="JobStoreItem"/> only need to be made in one place.
/// </summary>
internal static class JobStoreItemFactory
{
   /// <summary>
   /// Builds a <see cref="JobStoreItem"/> wrapping <see cref="TestJob"/>.
   /// When <paramref name="jobName"/> is null a new GUID is used (matching the production default).
   /// When <paramref name="useNullParameters"/> is true the <see cref="JobStoreItem.Parameters"/> property is set to null.
   /// </summary>
   public static JobStoreItem MakeTestJob(
      string? jobName = null,
      string? group = null,
      DateTime? performAt = null,
      TestJob.Parameters? parameters = null,
      bool useNullParameters = false)
   {
      var options = new JobScheduleOptions {
         Group = group
      };

      if (jobName is not null)
         options.JobName = jobName;

      return new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = useNullParameters
            ? null!
            : parameters ?? new TestJob.Parameters {
               Delay = TimeSpan.Zero
            },
         Options = options,
         PerformAt = performAt ?? DateTime.UtcNow
      };
   }
}
