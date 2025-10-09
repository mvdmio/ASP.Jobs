using System;
using Cronos;

namespace mvdmio.ASP.Jobs.Internals.Storage.Data;

internal sealed class JobStoreItem
{
   public Guid JobId { get; init; } = NewGuid();
   
   public required Type JobType { get; init; }
   public required object Parameters { get; init; }
   public required JobScheduleOptions Options { get; init; }
   public required DateTime PerformAt { get; init; }
   public CronExpression? CronExpression { get; init; }

   private static Guid NewGuid()
   {
#if NET9_0_OR_GREATER
      return Guid.CreateVersion7();
#else
      return Guid.NewGuid();
#endif      
   }
}