using System;

namespace mvdmio.ASP.Jobs.Utils;

internal class SystemClock : IClock
{
   private static readonly Lazy<SystemClock> _instance = new(() => new SystemClock());
   
   public static SystemClock Instance => _instance.Value;
   
   public DateTime UtcNow => DateTime.UtcNow;
   public DateTime Now => DateTime.Now;
}