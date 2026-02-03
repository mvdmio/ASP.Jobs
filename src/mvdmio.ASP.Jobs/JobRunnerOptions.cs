namespace mvdmio.ASP.Jobs;

/// <summary>
///    Configuration options for the job runner service.
/// </summary>
public class JobRunnerOptions
{
   /// <summary>
   ///    Gets or sets the maximum number of concurrent jobs that can execute simultaneously.
   ///    This allows for better I/O utilization when jobs await external resources.
   ///    Defaults to 10.
   /// </summary>
   public int MaxConcurrentJobs { get; set; } = 10;

   /// <summary>
   ///    Gets or sets the channel buffer size for pending jobs.
   ///    Jobs are fetched from storage and buffered here before execution.
   ///    Defaults to 50.
   /// </summary>
   public int JobChannelCapacity { get; set; } = 50;
}
