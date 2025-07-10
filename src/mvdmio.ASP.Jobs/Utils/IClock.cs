using System;

namespace mvdmio.ASP.Jobs.Utils;

internal interface IClock
{
   public DateTimeOffset UtcNow { get; }
   public DateTimeOffset Now { get; }
}