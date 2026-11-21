namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

// Names the xunit collection of the tests that depend on whether the job activity source has a listener. An
// ActivityListener hears the whole process, so these tests run one after another.
public static class SpanListenerCollection
{
   public const string NAME = "Job span listeners";
}
