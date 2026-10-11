using mvdmio.ASP.Jobs.Tests.Unit.Utils;

namespace mvdmio.ASP.Jobs.Tests.Integration.Jobs;

/// <summary>
///    A job class whose short name starts with <c>TestJob_</c>, so its CRON row name <c>cron_TestJob_Underscored_...</c>
///    starts with the CRON row name prefix of <see cref="TestJob"/>. Used to verify that scheduling
///    <see cref="TestJob"/> on a CRON leaves the CRON Schedule of this other class alone.
/// </summary>
// ReSharper disable once InconsistentNaming
public class TestJob_Underscored : Job<TestJob.Parameters>
{
   public override Task ExecuteAsync(TestJob.Parameters parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }
}
