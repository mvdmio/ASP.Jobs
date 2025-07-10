using System;

namespace mvdmio.ASP.Jobs.Utils;

internal class SystemClock : IClock
{
   private static readonly Lazy<SystemClock> _instance = new(() => new SystemClock());

   public static SystemClock Instance => _instance.Value;

   public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
   public DateTimeOffset Now => DateTimeOffset.Now;
}