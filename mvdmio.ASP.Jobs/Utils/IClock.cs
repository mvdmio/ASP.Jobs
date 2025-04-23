using System;

namespace mvdmio.ASP.Jobs.Utils;

internal interface IClock
{
   public DateTime UtcNow { get; }
   public DateTime Now { get; }
}