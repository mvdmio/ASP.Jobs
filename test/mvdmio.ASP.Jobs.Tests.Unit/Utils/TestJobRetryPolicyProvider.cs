namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Mutable holder for the <see cref="RetryPolicy"/> that <see cref="TestJob"/> instances expose. Registered as a
/// singleton per DI container (one per <see cref="JobRunnerHarness"/>/test) so tests can configure retry behavior
/// without needing a dedicated Job subclass per scenario.
/// </summary>
public class TestJobRetryPolicyProvider
{
   public RetryPolicy Policy { get; set; } = new();
}
