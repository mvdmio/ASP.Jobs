using System;

namespace mvdmio.ASP.Jobs;

/// <summary>
///     Information about a scheduled job.
/// </summary>
public class ScheduledJobInfo
{
    /// <summary>
    ///     The unique identifier of the scheduled job.
    /// </summary>
    public required Guid JobId { get; set; }

    /// <summary>
    ///     The type of the scheduled job.
    /// </summary>
    public required Type JobType { get; set; }

    /// <summary>
    ///     The parameters of the scheduled job.
    /// </summary>
    public required object Parameters { get; set; }

    /// <summary>
    ///     The timestamp at which the job is scheduled to be performed.
    /// </summary>
    public required DateTime PerformAt { get; set; }
}