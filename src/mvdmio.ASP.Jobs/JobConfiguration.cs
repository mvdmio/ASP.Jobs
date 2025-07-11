namespace mvdmio.ASP.Jobs;

/// <summary>
///    Configuration for the job system.
/// </summary>
public class JobConfiguration
{
   /// <summary>
   ///    The maximum number of threads that run jobs. Default is 5.
   ///    Note that the number of jobs started can be higher that this value if there is a lot of I/O bound work involved.
   /// </summary>
   public int JobRunnerThreadsCount { get; init; } = 5;
}