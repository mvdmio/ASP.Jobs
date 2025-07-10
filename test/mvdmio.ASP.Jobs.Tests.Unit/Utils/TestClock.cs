using mvdmio.ASP.Jobs.Utils;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class TestClock : IClock
{
   public DateTime UtcNow { get; set; } = DateTime.UtcNow;
   public DateTime Now { get; set; } = DateTime.Now;
}