using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using NSubstitute;
using Serilog.Events;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

/// <summary>
/// Tests that the job scheduler's log lines show a job's Logged Parameters and never the rest of its Job Parameters.
/// The scheduler logs through Serilog's static logger, so these tests share one collection and match events by their own marked id.
/// </summary>
[Collection(SerilogCapture.Name)]
public sealed class JobSchedulerLoggedParametersTests : IDisposable
{
   private readonly SerilogCapture _capture = new();
   private readonly JobRunnerHarness _harness = new();
   private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

   public void Dispose()
   {
      _capture.Dispose();
   }

   [Fact]
   public async Task Asap_LogsJobTypeAndMarkedIds_AndNeverTheSecret()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters();

      // Act
      await _harness.Scheduler.PerformAsapAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(parameters, _ct);

      // Assert
      var line = LinesFor(parameters).Should().ContainSingle().Subject;
      line.Level.Should().Be(LogEventLevel.Information);
      Message(line).Should().StartWith("Scheduled job: LoggedParametersJob");
      AssertShowsMarkedIdsOnly(line, parameters);
   }

   [Fact]
   public async Task At_LogsJobTypeMarkedIdsAndTime_AndNeverTheSecret()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters();
      var performAtUtc = DateTime.UtcNow.AddHours(1);

      // Act
      await _harness.Scheduler.PerformAtAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(performAtUtc, parameters, _ct);

      // Assert
      var line = LinesFor(parameters).Should().ContainSingle().Subject;
      Message(line).Should().StartWith("Scheduled Job: LoggedParametersJob").And.Contain("to run at");
      line.Properties.Should().ContainKey("Time");
      AssertShowsMarkedIdsOnly(line, parameters);
   }

   [Fact]
   public async Task BatchAsapAndAt_LogEachJobWithMarkedIds_AndNeverTheSecrets()
   {
      // Arrange
      var asap = new[] { new LoggedParametersJob.Parameters(), new LoggedParametersJob.Parameters() };
      var at = new[] { new LoggedParametersJob.Parameters(), new LoggedParametersJob.Parameters() };

      // Act
      await _harness.Scheduler.PerformAsapAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(asap, _ct);
      await _harness.Scheduler.PerformAtAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(DateTime.UtcNow.AddHours(1), at, _ct);

      // Assert
      foreach (var parameters in asap)
      {
         var line = LinesFor(parameters).Should().ContainSingle().Subject;
         Message(line).Should().StartWith("Scheduled job: LoggedParametersJob");
         AssertShowsMarkedIdsOnly(line, parameters);
      }

      foreach (var parameters in at)
      {
         var line = LinesFor(parameters).Should().ContainSingle().Subject;
         Message(line).Should().StartWith("Scheduled Job: LoggedParametersJob").And.Contain("to run at");
         AssertShowsMarkedIdsOnly(line, parameters);
      }
   }

   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public async Task Cron_LogsJobTypeMarkedIdsAndExpression_AndNeverTheSecret(bool runImmediately)
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters();

      // Act
      await _harness.Scheduler.PerformCronAsync<LoggedParametersJob, LoggedParametersJob.Parameters>("0 0 * * *", parameters, runImmediately, _ct);

      // Assert
      var line = LinesFor(parameters).Should().ContainSingle().Subject;
      Message(line).Should().StartWith("Scheduled Job: LoggedParametersJob").And.Contain("to run on schedule");
      line.Properties.Should().ContainKey("CronExpression");
      AssertShowsMarkedIdsOnly(line, parameters);
   }

   [Fact]
   public async Task PerformNow_WhenTheJobFails_LogsJobTypeMarkedIdsAndException_AndNeverTheSecret()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters { ThrowOnExecute = new InvalidOperationException("now failure") };

      // Act
      var act = () => _harness.Scheduler.PerformNowAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(parameters, _ct);

      // Assert
      await act.Should().ThrowAsync<InvalidOperationException>();
      var line = LinesFor(parameters).Should().ContainSingle().Subject;
      line.Level.Should().Be(LogEventLevel.Error);
      line.Exception.Should().BeSameAs(parameters.ThrowOnExecute);
      Message(line).Should().StartWith("Error while scheduling job: LoggedParametersJob");
      AssertShowsMarkedIdsOnly(line, parameters);
   }

   [Fact]
   public async Task Cron_WhenTheExpressionHasNoNextOccurrence_LogsJobTypeAndException_AndNeverTheSecret()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters();

      // Act
      var act = () => _harness.Scheduler.PerformCronAsync<LoggedParametersJob, LoggedParametersJob.Parameters>("0 0 30 2 *", parameters, runImmediately: false, _ct);

      // Assert
      await act.Should().ThrowAsync<InvalidOperationException>();
      var line = LinesFor(parameters).Should().ContainSingle().Subject;
      line.Level.Should().Be(LogEventLevel.Error);
      line.Exception.Should().BeOfType<InvalidOperationException>();
      Message(line).Should().StartWith("Error while scheduling job: LoggedParametersJob");
      AssertShowsMarkedIdsOnly(line, parameters);
   }

   [Fact]
   public async Task Asap_WhenStorageFails_LogsJobTypeAndException_AndNeverTheSecret()
   {
      await AssertStorageFailureLoggedAsync((scheduler, parameters) =>
         scheduler.PerformAsapAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(parameters, _ct)
      );
   }

   [Fact]
   public async Task At_WhenStorageFails_LogsJobTypeAndException_AndNeverTheSecret()
   {
      await AssertStorageFailureLoggedAsync((scheduler, parameters) =>
         scheduler.PerformAtAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(DateTime.UtcNow.AddHours(1), parameters, _ct)
      );
   }

   [Fact]
   public async Task Cron_WhenStorageFails_LogsJobTypeAndException_AndNeverTheSecret()
   {
      await AssertStorageFailureLoggedAsync((scheduler, parameters) =>
         scheduler.PerformCronAsync<LoggedParametersJob, LoggedParametersJob.Parameters>("0 0 * * *", parameters, runImmediately: false, _ct)
      );
   }

   [Fact]
   public async Task JobWithoutLoggedParameters_LogsNoParameterValuesOnAnySchedulingPath()
   {
      // Arrange
      var parameters = new UnmarkedParametersJob.Parameters();
      var batch = new[] { new UnmarkedParametersJob.Parameters(), new UnmarkedParametersJob.Parameters() };
      var failing = new UnmarkedParametersJob.Parameters { ThrowOnExecute = new InvalidOperationException("unmarked failure") };

      // Act
      await _harness.Scheduler.PerformAsapAsync<UnmarkedParametersJob, UnmarkedParametersJob.Parameters>(parameters, _ct);
      await _harness.Scheduler.PerformAtAsync<UnmarkedParametersJob, UnmarkedParametersJob.Parameters>(DateTime.UtcNow.AddHours(1), parameters, _ct);
      await _harness.Scheduler.PerformAsapAsync<UnmarkedParametersJob, UnmarkedParametersJob.Parameters>(batch, _ct);
      await _harness.Scheduler.PerformCronAsync<UnmarkedParametersJob, UnmarkedParametersJob.Parameters>("0 0 * * *", parameters, runImmediately: false, _ct);
      var act = () => _harness.Scheduler.PerformNowAsync<UnmarkedParametersJob, UnmarkedParametersJob.Parameters>(failing, _ct);
      await act.Should().ThrowAsync<InvalidOperationException>();
      var invalidCron = () => _harness.Scheduler.PerformCronAsync<UnmarkedParametersJob, UnmarkedParametersJob.Parameters>("0 0 30 2 *", parameters, runImmediately: false, _ct);
      await invalidCron.Should().ThrowAsync<InvalidOperationException>();

      // Assert
      // Other tests may log through the static logger at the same time, so assert on every unmarked-job line alike.
      var lines = _capture.Events.Where(x => x.RenderMessage().Contains("UnmarkedParametersJob")).ToList();
      lines.Should().HaveCountGreaterThanOrEqualTo(7);
      lines.Should().OnlyContain(x => ((DictionaryValue)x.Properties["Parameters"]).Elements.Count == 0);
      lines.Select(SerilogCapture.Text).Should().NotContain(x =>
         x.Contains(parameters.Secret) || x.Contains(parameters.Id.ToString()) || x.Contains(failing.Secret) || x.Contains(failing.Id.ToString())
      );
   }

   [Fact]
   public async Task GetScheduledJobsAsync_StillReturnsEveryParameterProperty_SecretIncluded()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters();
      await _harness.Scheduler.PerformAtAsync<LoggedParametersJob, LoggedParametersJob.Parameters>(DateTime.UtcNow.AddHours(1), parameters, _ct);

      // Act
      var jobs = await _harness.Scheduler.GetScheduledJobsAsync<LoggedParametersJob>(_ct);

      // Assert
      var job = jobs.Should().ContainSingle(x => ((LoggedParametersJob.Parameters)x.Parameters).Id == parameters.Id).Subject;
      var returned = job.Parameters.Should().BeOfType<LoggedParametersJob.Parameters>().Subject;
      returned.Secret.Should().Be(parameters.Secret);
      returned.Order.Id.Should().Be(parameters.Order.Id);
      returned.Order.Token.Should().Be(parameters.Order.Token);
   }

   private async Task AssertStorageFailureLoggedAsync(Func<JobScheduler, LoggedParametersJob.Parameters, Task> schedule)
   {
      // Arrange
      var failure = new InvalidOperationException("storage failure");
      var storage = Substitute.For<IJobStorage>();
      storage.ScheduleJobAsync(Arg.Any<JobStoreItem>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
      storage.ScheduleCronJobAsync(Arg.Any<JobStoreItem>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));

      var services = new ServiceCollection();
      services.RegisterJob<LoggedParametersJob>();
      var scheduler = new JobScheduler(services.BuildServiceProvider(), storage, new TestClock());
      var parameters = new LoggedParametersJob.Parameters();

      // Act
      var act = () => schedule(scheduler, parameters);

      // Assert
      await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("storage failure");
      var line = LinesFor(parameters).Should().ContainSingle().Subject;
      line.Level.Should().Be(LogEventLevel.Error);
      line.Exception.Should().BeSameAs(failure);
      Message(line).Should().StartWith("Error while scheduling job: LoggedParametersJob");
      AssertShowsMarkedIdsOnly(line, parameters);
   }

   private List<LogEvent> LinesFor(LoggedParametersJob.Parameters parameters)
   {
      return _capture.Events.Where(x => SerilogCapture.Text(x).Contains(parameters.Id.ToString())).ToList();
   }

   /// <summary>The rendered message without Serilog's quotes around string values, so a prefix reads like the template.</summary>
   private static string Message(LogEvent line)
   {
      return line.RenderMessage().Replace("\"", string.Empty);
   }

   private static void AssertShowsMarkedIdsOnly(LogEvent line, LoggedParametersJob.Parameters parameters)
   {
      var text = SerilogCapture.Text(line);
      text.Should().Contain(parameters.Id.ToString()).And.Contain(parameters.Order.Id.ToString());
      text.Should().NotContain(parameters.Secret).And.NotContain(parameters.Order.Token);
      line.Properties.Should().ContainKey("Parameters");
   }
}
