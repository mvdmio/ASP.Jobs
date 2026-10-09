using System.Collections.Concurrent;
using System.Diagnostics;
using AwesomeAssertions;
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

public sealed class JobRunnerStorageErrorBackoffTests
{
   private const string FetchErrorMessage = "Error while fetching next job from storage";
   private const string StorageDownMessage = "storage down";

   private readonly IJobStorage _storage = Substitute.For<IJobStorage>();
   private readonly RecordingLogger<JobRunnerService> _logger = new();
   private readonly ConcurrentQueue<TimeSpan> _fetchTimes = new();
   private readonly Stopwatch _stopwatch = new();
   private readonly JobRunnerService _runner;

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public JobRunnerStorageErrorBackoffTests()
   {
      var services = new JobTestServices().Services;
      services.AddSingleton(_storage);
      var serviceProvider = services.BuildServiceProvider();

      _runner = new JobRunnerService(serviceProvider, Options.Create(new JobRunnerOptions()), _logger, new TestClock());
   }

   [Theory]
   [InlineData(1, 1)]
   [InlineData(2, 2)]
   [InlineData(3, 4)]
   [InlineData(4, 8)]
   [InlineData(5, 16)]
   [InlineData(6, 30)]
   [InlineData(7, 30)]
   [InlineData(64, 30)]
   public void BackoffDoublesFromOneSecondUpToThirtySeconds(int consecutiveErrors, int expectedSeconds)
   {
      // Act
      var wait = JobRunnerService.GetStorageErrorBackoff(consecutiveErrors);

      // Assert
      wait.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
   }

   [Fact]
   public async Task FailingStorageIsRetriedOnlyAHandfulOfTimes()
   {
      // Arrange
      SetupFetchResults(_ => StorageDown());

      // Act
      await StartAsync();
      await Task.Delay(TimeSpan.FromSeconds(3.5), CancellationToken);
      await _runner.StopAsync(CancellationToken);

      // Assert: a handful of attempts (about 3, at 0 s, 1 s and 3 s), each logged once with the existing message and nothing else.
      _fetchTimes.Should().HaveCountGreaterThanOrEqualTo(2).And.HaveCountLessThanOrEqualTo(4);
      WarningOrWorseEntries().Should().HaveCount(_fetchTimes.Count);
      WarningOrWorseEntries().Should().OnlyContain(x => x.Message == FetchErrorMessage && x.Exception!.Message == StorageDownMessage);
   }

   [Fact]
   public async Task SuccessfulFetchResetsTheBackoff()
   {
      // Arrange: throw twice, return null once, then keep throwing.
      SetupFetchResults(call => call == 3 ? Task.FromResult<JobStoreItem?>(null) : StorageDown());

      // Act
      await StartAsync();
      await WaitForFetchCountAsync(5, TimeSpan.FromSeconds(10));
      await _runner.StopAsync(CancellationToken);

      // Assert: the 4th fetch is the first error after the success, so the gap after it is the 1 s wait. Without the
      // reset it would be 4 s; an upper bound of 3 s leaves room for a busy machine and still tells the two apart.
      var times = _fetchTimes.ToArray();
      var gapAfterFirstErrorFollowingSuccess = times[4] - times[3];

      gapAfterFirstErrorFollowingSuccess.Should().BeGreaterThan(TimeSpan.FromSeconds(0.9));
      gapAfterFirstErrorFollowingSuccess.Should().BeLessThan(TimeSpan.FromSeconds(3));
   }

   [Fact]
   public async Task StoppingDuringBackoffReturnsPromptly()
   {
      // Arrange: stop just after the 2nd error, while its 2 s wait has nearly all of its time left.
      SetupFetchResults(_ => StorageDown());

      await StartAsync();
      await WaitForFetchCountAsync(2, TimeSpan.FromSeconds(5));
      await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken);

      // Act
      var stopStartedAt = _stopwatch.Elapsed;
      await _runner.StopAsync(CancellationToken);
      var stopDuration = _stopwatch.Elapsed - stopStartedAt;

      // Assert: the wait was cut short rather than waited out (about 1.9 s), and the cancelled wait logged nothing.
      stopDuration.Should().BeLessThan(TimeSpan.FromSeconds(1));
      _fetchTimes.Should().HaveCount(2);
      WarningOrWorseEntries().Should().HaveCount(2).And.OnlyContain(x => x.Message == FetchErrorMessage);
   }

   private static Task<JobStoreItem?> StorageDown()
   {
      return Task.FromException<JobStoreItem?>(new InvalidOperationException(StorageDownMessage));
   }

   private void SetupFetchResults(Func<int, Task<JobStoreItem?>> resultForCall)
   {
      var calls = 0;

      _storage.WaitForNextJobAsync(Arg.Any<CancellationToken>()).Returns(_ => {
         _fetchTimes.Enqueue(_stopwatch.Elapsed);
         return resultForCall(Interlocked.Increment(ref calls));
      });
   }

   private async Task StartAsync()
   {
      _stopwatch.Start();
      await _runner.StartAsync(CancellationToken);
   }

   private async Task WaitForFetchCountAsync(int count, TimeSpan timeout)
   {
      using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      timeoutSource.CancelAfter(timeout);

      while (_fetchTimes.Count < count)
         await Task.Delay(10, timeoutSource.Token);
   }

   private List<RecordedLogEntry> WarningOrWorseEntries()
   {
      return _logger.Entries.Where(x => x.Level >= LogLevel.Warning).ToList();
   }
}
