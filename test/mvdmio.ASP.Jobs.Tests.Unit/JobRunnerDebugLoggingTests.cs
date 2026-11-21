using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

[Collection(SpanListenerCollection.NAME)]
public sealed class JobRunnerDebugLoggingTests
{
   private readonly CancellationTokenSource _cts;

   private CancellationToken CancellationToken => _cts.Token;

   public JobRunnerDebugLoggingTests()
   {
      _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      _cts.CancelAfter(TimeSpan.FromSeconds(10));
   }

   [Fact]
   public async Task SuccessfulRun_WithDebugOff_WritesNoDebugEntry_AndStillRuns()
   {
      // Arrange
      var logger = new RecordingLogger<JobRunnerService>(LogLevel.Information);
      var harness = CreateHarness(logger);
      var parameters = new TestJob.Parameters();
      await harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert
      parameters.Executed.Should().BeTrue();
      (await harness.Storage.GetScheduledJobsAsync(CancellationToken)).Should().BeEmpty();
      logger.Entries.Should().NotContain(x => x.Level == LogLevel.Debug);
   }

   [Fact]
   public async Task SuccessfulRun_WithDebugOn_WritesRunningAndFinishedLinesAtDebug()
   {
      // Arrange
      var logger = new RecordingLogger<JobRunnerService>();
      var harness = CreateHarness(logger);
      var parameters = new TestJob.Parameters();
      await harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var running = logger.Entries.Should().ContainSingle(x => x.Message.StartsWith("Running job")).Subject;
      running.Level.Should().Be(LogLevel.Debug);
      running.Properties["JobType"].Should().Be("TestJob");
      var finished = logger.Entries.Should().ContainSingle(x => x.Message.StartsWith("Finished job")).Subject;
      finished.Level.Should().Be(LogLevel.Debug);
      finished.Properties["Duration"].Should().BeOfType<TimeSpan>();
   }

   [Fact]
   public async Task SuccessfulRun_WithDebugOffAndNoSpanListener_ReadsNoLoggedParameter()
   {
      // Arrange
      var harness = CreateHarness(new RecordingLogger<JobRunnerService>(LogLevel.Information));
      var parameters = new ReadCountingJob.Parameters();
      await harness.Scheduler.PerformAsapAsync<ReadCountingJob, ReadCountingJob.Parameters>(parameters, CancellationToken);

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert
      parameters.Reads.Should().Be(0);
   }

   [Fact]
   public async Task SuccessfulRun_WithDebugOnAndNoSpanListener_ReadsEachLoggedParameterOnce()
   {
      // Arrange
      var logger = new RecordingLogger<JobRunnerService>();
      var harness = CreateHarness(logger);
      var parameters = new ReadCountingJob.Parameters();
      await harness.Scheduler.PerformAsapAsync<ReadCountingJob, ReadCountingJob.Parameters>(parameters, CancellationToken);

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert: the Running and the Finished line share one dictionary.
      parameters.Reads.Should().Be(1);
      logger.Entries.Where(x => x.Properties.ContainsKey("@Parameters")).Should().HaveCount(2);
   }

   [Theory]
   [InlineData(LogLevel.Information)]
   [InlineData(LogLevel.Trace)]
   public async Task SuccessfulRun_WithSpanListener_ReadsEachLoggedParameterOnce(LogLevel runnerMinimumLevel)
   {
      // Arrange
      var harness = CreateHarness(new RecordingLogger<JobRunnerService>(runnerMinimumLevel));
      var parameters = new ReadCountingJob.Parameters();
      await harness.Scheduler.PerformAsapAsync<ReadCountingJob, ReadCountingJob.Parameters>(parameters, CancellationToken);
      using var listener = ListenToJobSource();

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert: the span tags and the Debug lines, when they are on, share one dictionary.
      parameters.Reads.Should().Be(1);
   }

   [Fact]
   public async Task FailingRun_WithDebugOff_StillWritesErrorLine_WithMarkedPropertiesOnly()
   {
      // Arrange
      var logger = new RecordingLogger<JobRunnerService>(LogLevel.Information);
      var harness = CreateHarness(logger);
      var parameters = new LoggedParametersJob.Parameters { ThrowOnExecute = new InvalidOperationException("failure with debug off") };
      await harness.Scheduler.PerformAsapAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(parameters, CancellationToken);

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var failure = logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error).Subject;
      failure.Message.Should().StartWith("Error while running job LoggedParametersJob").And.Contain(parameters.Id.ToString());
      failure.Message.Should().NotContain(parameters.Secret).And.NotContain(parameters.Order.Token);
      failure.Exception.Should().BeSameAs(parameters.ThrowOnExecute);
      var logged = failure.Properties["@Parameters"].Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
      logged.Keys.Should().BeEquivalentTo("Id", "Order");
   }

   // The scheduler's own Debug line reads the Logged Parameters too, so keep it off: the reads counted are the runner's.
   private static JobRunnerHarness CreateHarness(RecordingLogger<JobRunnerService> runnerLogger)
   {
      return new JobRunnerHarness(runnerLogger: runnerLogger, schedulerMinimumLevel: LogLevel.Information);
   }

   // A listener that samples nothing still makes the job span's tags worth building.
   private static ActivityListener ListenToJobSource()
   {
      var listener = new ActivityListener {
         ShouldListenTo = source => source.Name == JobRunnerService.ActivitySourceName,
         Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None
      };

      ActivitySource.AddActivityListener(listener);
      return listener;
   }
}
