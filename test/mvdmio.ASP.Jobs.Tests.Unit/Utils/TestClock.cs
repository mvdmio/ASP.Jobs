using mvdmio.ASP.Jobs.Tests.Unit.Extensions;
using mvdmio.ASP.Jobs.Utils;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class TestClock : IClock
{
   // We use 'Floor' to ensure that the time is always rounded down to the nearest millisecond. This is to prevent issues with time-sensitive assertions that rely on exact microsecond precision.
   // Some tests used to fail because the database saved time with a lower precision than the test expected, leading to false negatives.

   private DateTime _utcNow = DateTime.UtcNow.Floor();
   private DateTime _now = DateTime.Now.Floor();
   
   public DateTime UtcNow
   {
      get => _utcNow;
      set => _utcNow = value.Floor();
   }

   public DateTime Now
   {
      get => _now;
      set => _now = value.Floor();
   }
}