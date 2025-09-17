namespace mvdmio.ASP.Jobs;

/// <summary>
///    Configuration for the job system.
/// </summary>
public class JobRunnerOptions
{
   /// <summary>
   ///    The maximum number of threads that run jobs. Default is 5.
   /// </summary>
   public int JobRunnerThreadsCount { get; set; } = 5;
}