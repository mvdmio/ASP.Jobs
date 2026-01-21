using System;

namespace mvdmio.ASP.Jobs.Utils;

/// <summary>
///    Interface for abstracting system clock operations, enabling testability.
/// </summary>
internal interface IClock
{
   /// <summary>
   ///    Gets the current UTC date and time.
   /// </summary>
   public DateTime UtcNow { get; }
   
   /// <summary>
   ///    Gets the current local date and time.
   /// </summary>
   public DateTime Now { get; }
}