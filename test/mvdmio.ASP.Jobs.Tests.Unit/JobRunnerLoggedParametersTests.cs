using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

/// <summary>
/// Tests that the job runner's log lines show a job's Logged Parameters and never the rest of its Job Parameters.
/// </summary>
public sealed class JobRunnerLoggedParametersTests
{
   private readonly RecordingLogger<JobRunnerService> _logger = new();
   private readonly JobRunnerHarness _harness;
   private readonly CancellationTokenSource _cts;

   private CancellationToken CancellationToken => _cts.Token;

   public JobRunnerLoggedParametersTests()
   {
      _harness = new JobRunnerHarness(runnerLogger: _logger);
      _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      _cts.CancelAfter(TimeSpan.FromSeconds(10));
   }

   [Fact]
   public async Task RunAndFinishLines_ShowLoggedId_AndNeverTheSecret()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters();
      await _harness.Scheduler.PerformAsapAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var lines = JobLines(parameters.Id);
      lines.Should().HaveCount(2);
      lines.Select(x => x.Message).Should().Contain(x => x.StartsWith("Running job: LoggedParametersJob"));
      lines.Select(x => x.Message).Should().Contain(x => x.StartsWith("Finished job LoggedParametersJob"));
      lines.Should().OnlyContain(x => x.Message.Contains(parameters.Id.ToString()));
      AssertNeverShown(lines, parameters.Secret, parameters.Order.Token);
   }

   [Fact]
   public async Task FailureLine_ShowsJobTypeIdAndException_AndNeverTheSecret()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters { ThrowOnExecute = new InvalidOperationException("logged failure") };
      await _harness.Scheduler.PerformAsapAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var lines = JobLines(parameters.Id);
      var failure = lines.Should().ContainSingle(x => x.Level == LogLevel.Error).Subject;
      failure.Message.Should().StartWith("Error while running job LoggedParametersJob").And.Contain(parameters.Id.ToString());
      failure.Exception.Should().BeSameAs(parameters.ThrowOnExecute);
      AssertNeverShown(lines, parameters.Secret, parameters.Order.Token);
   }

   [Fact]
   public async Task NestedLoggedObject_ShowsItsLoggedId_AndNotItsToken()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters();
      await _harness.Scheduler.PerformAsapAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var line = JobLines(parameters.Id).First();
      var logged = line.Properties["@Parameters"].Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
      logged.Keys.Should().BeEquivalentTo("Id", "Order");
      logged["Id"].Should().Be(parameters.Id);
      var order = logged["Order"].Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
      order.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Id"] = parameters.Order.Id });
   }

   [Fact]
   public async Task JobWithoutLoggedParameters_LogsNoParameterValues()
   {
      // Arrange
      var parameters = new UnmarkedParametersJob.Parameters { ThrowOnExecute = new InvalidOperationException("unmarked failure") };
      var succeeding = new UnmarkedParametersJob.Parameters();
      await _harness.Scheduler.PerformAsapAsync<UnmarkedParametersJob, UnmarkedParametersJob.Parameters>(succeeding, CancellationToken);
      await _harness.Scheduler.PerformAsapAsync<UnmarkedParametersJob, UnmarkedParametersJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var lines = _logger.Entries.Where(x => x.Message.Contains("UnmarkedParametersJob")).ToList();
      lines.Should().HaveCount(4);
      lines.Should().OnlyContain(x => ((IDictionary<string, object?>)x.Properties["@Parameters"]!).Count == 0);
      AssertNeverShown(lines, succeeding.Secret, parameters.Secret, succeeding.Id.ToString(), parameters.Id.ToString());
      lines.Should().ContainSingle(x => x.Level == LogLevel.Error).Which.Exception.Should().BeSameAs(parameters.ThrowOnExecute);
   }

   private List<RecordedLogEntry> JobLines(Guid id)
   {
      return _logger.Entries.Where(x => x.Message.Contains(id.ToString())).ToList();
   }

   private static void AssertNeverShown(List<RecordedLogEntry> lines, params string[] secrets)
   {
      lines.Should().NotBeEmpty();

      foreach (var secret in secrets)
      {
         lines.Should().NotContain(x => x.Message.Contains(secret));
         lines.Should().NotContain(x => x.Properties.Values.Any(v => ShowsValue(v, secret)));
      }
   }

   // The Parameters property holds a dictionary, possibly nested; check every value the way a log sink would print it.
   private static bool ShowsValue(object? value, string secret)
   {
      return value switch {
         null => false,
         IDictionary<string, object?> dictionary => dictionary.Values.Any(x => ShowsValue(x, secret)),
         _ => value.ToString()!.Contains(secret)
      };
   }
}
