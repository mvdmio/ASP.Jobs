using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public sealed class JobSchedulerLogLevelTests
{
   private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

   [Fact]
   public async Task WhenDebugIsFilteredOut_SchedulingSucceedsWritesNoDebugLineAndBuildsNoParameterDictionary()
   {
      // Arrange
      var (scheduler, logger) = CreateScheduler(LogLevel.Information);
      var parameters = new ReadCountingJob.Parameters();

      // Act
      await scheduler.PerformAsapAsync<ReadCountingJob, ReadCountingJob.Parameters>(parameters, _ct);
      await scheduler.PerformAtAsync<ReadCountingJob, ReadCountingJob.Parameters>(DateTime.UtcNow.AddHours(1), parameters, _ct);
      await scheduler.PerformAsapAsync<ReadCountingJob, ReadCountingJob.Parameters>([parameters, parameters], _ct);

      // Assert
      logger.Entries.Should().BeEmpty();
      parameters.Reads.Should().Be(0);
      (await scheduler.GetScheduledJobsAsync<ReadCountingJob>(_ct)).Should().HaveCount(4);
   }

   [Fact]
   public async Task WhenDebugIsEnabled_TheParameterDictionaryIsBuiltForEachLine()
   {
      // Arrange
      var (scheduler, logger) = CreateScheduler(LogLevel.Trace);
      var parameters = new ReadCountingJob.Parameters();

      // Act
      await scheduler.PerformAsapAsync<ReadCountingJob, ReadCountingJob.Parameters>([parameters, parameters], _ct);

      // Assert
      logger.Entries.Should().HaveCount(2).And.OnlyContain(x => x.Level == LogLevel.Debug);
      parameters.Reads.Should().Be(2);
   }

   [Fact]
   public async Task WhenDebugIsFilteredOut_AFailureStillWritesItsErrorLine()
   {
      // Arrange
      var harness = new JobRunnerHarness(schedulerMinimumLevel: LogLevel.Information);

      // Act
      var act = () => harness.Scheduler.PerformCronAsync<LoggedParametersJob, LoggedParametersJob.Parameters>("0 0 30 2 *", new LoggedParametersJob.Parameters(), runImmediately: false, _ct);

      // Assert
      await act.Should().ThrowAsync<InvalidOperationException>();
      var line = harness.SchedulerLogger.Entries.Should().ContainSingle().Subject;
      line.Level.Should().Be(LogLevel.Error);
      line.Message.Should().StartWith("Error while scheduling job: LoggedParametersJob");
   }

   private static (JobScheduler Scheduler, RecordingLogger<JobScheduler> Logger) CreateScheduler(LogLevel minimumLevel)
   {
      var clock = new TestClock();
      var services = new ServiceCollection();
      services.RegisterJob<ReadCountingJob>();
      var logger = new RecordingLogger<JobScheduler>(minimumLevel);

      return (new JobScheduler(services.BuildServiceProvider(), new InMemoryJobStorage(clock), clock, logger), logger);
   }
}
