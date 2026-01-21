using System;

namespace mvdmio.ASP.Jobs.Utils;

/// <summary>
///    Default implementation of <see cref="IClock"/> that returns the actual system time.
///    Implemented as a singleton.
/// </summary>
internal class SystemClock : IClock
{
   private static readonly Lazy<SystemClock> _instance = new(() => new SystemClock());

   /// <summary>
   ///    Gets the singleton instance of <see cref="SystemClock"/>.
   /// </summary>
   public static SystemClock Instance => _instance.Value;

   private SystemClock()
   {
      // Private constructor to enforce singleton pattern
   }
   
   /// <inheritdoc />
   public DateTime UtcNow => DateTime.UtcNow;
   
   /// <inheritdoc />
   public DateTime Now => DateTime.Now;
}