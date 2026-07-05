namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// During execution, schedules a <see cref="CultureRecordingJob"/> using the default (no-culture) overload.
/// The child should therefore capture the ambient culture — which, while this job runs, is the parent's
/// reapplied Captured Culture — letting tests assert that culture propagates to child jobs automatically.
/// </summary>
public class CultureChildSchedulingJob : Job<CultureChildSchedulingJob.Parameters>
{
   private readonly IJobScheduler _scheduler;

   public CultureChildSchedulingJob(IJobScheduler scheduler)
   {
      _scheduler = scheduler;
   }

   public override async Task ExecuteAsync(Parameters properties, CancellationToken cancellationToken)
   {
      await _scheduler.PerformAsapAsync<CultureRecordingJob, CultureRecordingJob.Parameters>(properties.Child, cancellationToken);
   }

   public class Parameters
   {
      public CultureRecordingJob.Parameters Child { get; set; } = new();
   }
}
