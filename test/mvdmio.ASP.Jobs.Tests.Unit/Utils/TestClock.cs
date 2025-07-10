using mvdmio.ASP.Jobs.Tests.Unit.Extensions;
using mvdmio.ASP.Jobs.Utils;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class TestClock : IClock
{
   // We use 'Floor' to ensure that the time is always rounded down to the nearest millisecond. This is to prevent issues with time-sensitive assertions that rely on exact microsecond precision.
   // Some tests used to fail because the database saved time with a lower precision than the test expected, leading to false negatives.
   
   public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow.Floor();
   public DateTimeOffset Now { get; set; } = DateTimeOffset.Now.Floor();
}