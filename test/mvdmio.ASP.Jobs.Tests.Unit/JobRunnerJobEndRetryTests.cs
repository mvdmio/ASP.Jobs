using System.Collections.Concurrent;
using AwesomeAssertions;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using NSubstitute;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

/// <summary>
///    Tests that the runner keeps trying to store the end of an Execution Chain, and the retry that continues one, until
///    the storage accepts the write. A substituted <see cref="IJobStorage"/> hands the runner its jobs and refuses writes;
///    the tests assert what storage received and what was logged. The first wait of the loop is a real 1 second.
/// </summary>
public sealed class JobRunnerJobEndRetryTests
{
   private static readonly CronExpression _everyMinute = CronExpression.Parse("* * * * *");

   private readonly IJobStorage _storage = Substitute.For<IJobStorage>();
   private readonly RecordingLogger<JobRunnerService> _logger = new();
   private readonly TestClock _clock = new();
   private readonly CancellationTokenSource _cts;
   private readonly ConcurrentQueue<JobStoreItem> _jobsToHandOut = new();
   private readonly TestJobRetryPolicyProvider _retryPolicyProvider;
   private readonly JobRunnerService _runner;

   private CancellationToken CancellationToken => _cts.Token;

   public JobRunnerJobEndRetryTests()
   {
      _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      _cts.CancelAfter(TimeSpan.FromSeconds(15));

      var services = new JobTestServices().Services;
      services.RegisterJob<LoggedParametersJob>();
      services.AddSingleton(_storage);
      var serviceProvider = services.BuildServiceProvider();
      _retryPolicyProvider = serviceProvider.GetRequiredService<TestJobRetryPolicyProvider>();

      // Hands out each queued job once, then waits for shutdown like the real storage waits for the next job.
      _storage.WaitForNextJobAsync(Arg.Any<CancellationToken>()).Returns(call => NextJobAsync(call.Arg<CancellationToken>()));

      _runner = new JobRunnerService(serviceProvider, Options.Create(new JobRunnerOptions { MaxConcurrentJobs = 1 }), _logger, _clock);
   }

   [Fact]
   public async Task CronJob_WhoseFinalizeThrowsOnce_FinalizesAgainAndSchedulesTheNextOccurrence()
   {
      // Arrange
      var job = JobStoreItemFactory.MakeTestJob(jobName: "EveryMinute", cronExpression: _everyMinute);
      _jobsToHandOut.Enqueue(job);
      _storage.FinalizeJobAsync(job, Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("storage down")), _ => Task.CompletedTask);

      // Act
      var before = DateTime.UtcNow;
      await RunUntilAsync(() => CallCount(nameof(IJobStorage.ScheduleJobAsync)) >= 1);
      var after = DateTime.UtcNow;

      // Assert: the next occurrence is the first whole minute after the moment the runner read the time. That moment lies
      // between the two captures, so a minute boundary in between allows the later minute as well.
      await _storage.Received(2).FinalizeJobAsync(job, Arg.Any<CancellationToken>());
      var scheduled = ScheduledItems().Should().ContainSingle().Subject;
      scheduled.Options.JobName.Should().Be("EveryMinute");
      scheduled.PerformAt.Should().BeOneOf(NextWholeMinuteAfter(before), NextWholeMinuteAfter(after));
   }

   [Fact]
   public async Task CronJob_WhoseNextOccurrenceUpsertThrowsOnce_RunsTheWholeStepAgain()
   {
      // Arrange
      var job = JobStoreItemFactory.MakeTestJob(jobName: "EveryMinute", cronExpression: _everyMinute);
      _jobsToHandOut.Enqueue(job);
      _storage.ScheduleJobAsync(Arg.Any<JobStoreItem>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("storage down")), _ => Task.CompletedTask);

      // Act
      await RunUntilAsync(() => CallCount(nameof(IJobStorage.ScheduleJobAsync)) >= 2);

      // Assert
      await _storage.Received(2).FinalizeJobAsync(job, Arg.Any<CancellationToken>());
      ScheduledItems().Should().HaveCount(2).And.OnlyContain(x => x.Options.JobName == "EveryMinute" && x.CronExpression == _everyMinute);
   }

   [Fact]
   public async Task Retry_WhoseWriteThrowsOnce_IsStoredOnTheSecondAttemptWithTheSameRunTimeAndAttempt()
   {
      // Arrange
      _retryPolicyProvider.Policy = new RetryPolicy {
         new RetryBehavior<InvalidOperationException> { MaxRetries = 3, InitialDelay = TimeSpan.FromMinutes(5) }
      };

      var parameters = new TestJob.Parameters { ThrowOnExecute = new InvalidOperationException("job failed") };
      var job = JobStoreItemFactory.MakeTestJob(parameters: parameters);
      _jobsToHandOut.Enqueue(job);
      _storage.TryScheduleRetryAsync(job, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<bool>(new InvalidOperationException("storage down")), _ => Task.FromResult(true));

      // Act
      await RunUntilAsync(() => CallCount(nameof(IJobStorage.TryScheduleRetryAsync)) >= 2);

      // Assert: both attempts wrote the run time computed once, and the retry hook fired once, for the first retry.
      var expectedRunTime = _clock.UtcNow + TimeSpan.FromMinutes(5);
      await _storage.Received(2).TryScheduleRetryAsync(job, expectedRunTime, Arg.Any<CancellationToken>());
      await _storage.DidNotReceive().FinalizeJobAsync(Arg.Any<JobStoreItem>(), Arg.Any<CancellationToken>());
      job.Attempt.Should().Be(0);
      parameters.RetryContexts.Should().ContainSingle().Which.Attempt.Should().Be(1);
      parameters.RetryContexts[0].NextAttemptAtUtc.Should().Be(expectedRunTime);
   }

   [Fact]
   public async Task FailingStep_LogsOneErrorPerFailedAttempt_AndOneInformationLineAfterTheSuccess()
   {
      // Arrange
      var job = JobStoreItemFactory.MakeTestJob();
      _jobsToHandOut.Enqueue(job);
      var failure = new InvalidOperationException("storage down");
      _storage.FinalizeJobAsync(job, Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(failure), _ => Task.FromException(failure), _ => Task.CompletedTask);

      // Act
      await RunUntilAsync(() => CallCount(nameof(IJobStorage.FinalizeJobAsync)) >= 3);

      // Assert: the waits are 1 s and then 2 s, and the success counts as the third attempt.
      var errors = _logger.Entries.Where(x => x.Level == LogLevel.Error).ToList();
      errors.Should().HaveCount(2);
      errors.Should().OnlyContain(x => ReferenceEquals(x.Exception, failure) && (string)x.Properties["JobType"]! == "TestJob");
      errors.Select(x => (int)x.Properties["Attempt"]!).Should().Equal(1, 2);
      errors.Select(x => (TimeSpan)x.Properties["Wait"]!).Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

      var recovered = _logger.Entries.Where(x => x.Properties.ContainsKey("Attempts")).Should().ContainSingle().Subject;
      recovered.Level.Should().Be(LogLevel.Information);
      recovered.Properties["JobType"].Should().Be("TestJob");
      recovered.Properties["Attempts"].Should().Be(3);
   }

   [Fact]
   public async Task StepThatSucceedsAtOnce_LogsNoErrorAndNoRecoveryLine()
   {
      // Arrange
      _jobsToHandOut.Enqueue(JobStoreItemFactory.MakeTestJob());

      // Act
      await RunUntilAsync(() => CallCount(nameof(IJobStorage.FinalizeJobAsync)) >= 1);

      // Assert
      _logger.Entries.Should().NotContain(x => x.Level >= LogLevel.Warning || x.Properties.ContainsKey("Attempts"));
   }

   [Fact]
   public async Task ErrorLine_ShowsTheLoggedParameters_AndNeverTheOtherJobParameters()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters();
      var job = new JobStoreItem {
         JobType = typeof(LoggedParametersJob),
         Parameters = parameters,
         Options = new JobScheduleOptions(),
         PerformAt = DateTime.UtcNow
      };

      _jobsToHandOut.Enqueue(job);
      _storage.FinalizeJobAsync(job, Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("storage down")), _ => Task.CompletedTask);

      // Act
      await RunUntilAsync(() => CallCount(nameof(IJobStorage.FinalizeJobAsync)) >= 2);

      // Assert
      var error = _logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error).Subject;
      var logged = error.Properties["@Parameters"].Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
      logged.Keys.Should().BeEquivalentTo("Id", "Order");
      logged["Id"].Should().Be(parameters.Id);
      error.Message.Should().Contain(parameters.Id.ToString()).And.NotContain(parameters.Secret).And.NotContain(parameters.Order.Token);
   }

   [Fact]
   public async Task StoppingWhileTheStepWaits_EndsTheRunWithoutAnotherAttempt()
   {
      // Arrange
      var job = JobStoreItemFactory.MakeTestJob();
      _jobsToHandOut.Enqueue(job);
      _storage.FinalizeJobAsync(job, Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("storage down")));

      await _runner.StartAsync(CancellationToken);
      await WaitUntilAsync(() => _logger.Entries.Any(x => x.Level == LogLevel.Error));

      // Act: the first wait is 1 s, so the run is still waiting here and stopping must cut that wait short.
      await _runner.StopAsync(CancellationToken);

      // Assert
      await _storage.Received(1).FinalizeJobAsync(job, Arg.Any<CancellationToken>());
      _logger.Entries.Count(x => x.Level >= LogLevel.Warning).Should().Be(1);
   }

   [Fact]
   public async Task WaitingJob_KeepsItsSlot_SoTheNextJobWaitsForIt()
   {
      // Arrange: only one job runs at a time; the first job's end is refused once.
      var first = JobStoreItemFactory.MakeTestJob(jobName: "First");
      var second = JobStoreItemFactory.MakeTestJob(jobName: "Second");
      _jobsToHandOut.Enqueue(first);
      _jobsToHandOut.Enqueue(second);

      var finalizeOrder = new ConcurrentQueue<string>();
      var firstFinalizeCalls = 0;
      _storage.FinalizeJobAsync(Arg.Any<JobStoreItem>(), Arg.Any<CancellationToken>()).Returns(call => {
         var item = call.Arg<JobStoreItem>();
         finalizeOrder.Enqueue(item.Options.JobName);

         return ReferenceEquals(item, first) && Interlocked.Increment(ref firstFinalizeCalls) == 1
            ? Task.FromException(new InvalidOperationException("storage down"))
            : Task.CompletedTask;
      });

      // Act
      await RunUntilAsync(() => finalizeOrder.Count >= 3);

      // Assert: had the first job given up its slot while waiting, the second would have finished before the first's second attempt.
      finalizeOrder.Should().Equal("First", "First", "Second");
   }

   [Fact]
   public async Task ExceptionEscapingTheJobRun_IsLogged_AndTheNextJobStillRuns()
   {
      // Arrange: the first job's type is not registered, so it fails before its body; with one slot, the second job only
      // runs when the first released it.
      _jobsToHandOut.Enqueue(new JobStoreItem {
         JobType = typeof(UnregisteredJob),
         Parameters = new object(),
         Options = new JobScheduleOptions(),
         PerformAt = DateTime.UtcNow
      });

      var next = JobStoreItemFactory.MakeTestJob();
      _jobsToHandOut.Enqueue(next);

      // Act
      await RunUntilAsync(() => CallCount(nameof(IJobStorage.FinalizeJobAsync)) >= 1);

      // Assert
      await _storage.Received(1).FinalizeJobAsync(next, Arg.Any<CancellationToken>());
      var error = _logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error).Subject;
      error.Properties["JobType"].Should().Be("UnregisteredJob");
      error.Exception.Should().BeOfType<InvalidOperationException>();
   }

   private async Task<JobStoreItem?> NextJobAsync(CancellationToken ct)
   {
      if (_jobsToHandOut.TryDequeue(out var job))
         return job;

      await Task.Delay(Timeout.Infinite, ct);
      return null;
   }

   private async Task RunUntilAsync(Func<bool> condition)
   {
      await _runner.StartAsync(CancellationToken);
      await WaitUntilAsync(condition);
      await _runner.StopAsync(CancellationToken);
   }

   private async Task WaitUntilAsync(Func<bool> condition)
   {
      while (!condition())
         await Task.Delay(10, CancellationToken);
   }

   private int CallCount(string method)
   {
      return _storage.ReceivedCalls().Count(x => x.GetMethodInfo().Name == method);
   }

   private List<JobStoreItem> ScheduledItems()
   {
      return _storage.ReceivedCalls()
         .Where(x => x.GetMethodInfo().Name == nameof(IJobStorage.ScheduleJobAsync))
         .Select(x => (JobStoreItem)x.GetArguments()[0]!)
         .ToList();
   }

   private static DateTime NextWholeMinuteAfter(DateTime utc)
   {
      return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc).AddMinutes(1);
   }

   private sealed class UnregisteredJob;
}
