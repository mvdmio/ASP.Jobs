namespace mvdmio.ASP.Jobs;

/// <summary>
///    Configuration options for the job runner service.
/// </summary>
public class JobRunnerOptions
{
   /// <summary>
   ///    Gets or sets the maximum number of concurrent threads that execute jobs. Defaults to 5.
   /// </summary>
   public int JobRunnerThreadsCount { get; set; } = 5;
}