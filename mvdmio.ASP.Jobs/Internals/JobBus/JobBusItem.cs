using System;

namespace mvdmio.ASP.Jobs.Internals.JobBus;

internal class JobBusItem
{
   public required Type JobType { get; init; }
   public required object Parameters { get; init; }
}