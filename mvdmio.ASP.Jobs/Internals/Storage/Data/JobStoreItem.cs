using System;
using Cronos;

namespace mvdmio.ASP.Jobs.Internals.Storage.Data;

internal class JobStoreItem
{
   public required Type JobType { get; init; }
   public required object Parameters { get; init; }
   public CronExpression? CronExpression { get; init; }
}