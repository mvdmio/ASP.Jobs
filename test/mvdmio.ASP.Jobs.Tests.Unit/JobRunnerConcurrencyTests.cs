using AwesomeAssertions;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

/// <summary>
/// Tests for verifying concurrent job execution behavior in the JobRunnerService.
/// </summary>
public sealed class JobRunnerConcurrencyTests
{
   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public static TheoryData<int, int, int, int?> ConcurrencyCases =>
      new()
      {
         // maxConcurrent, totalJobs, durationMs, randomSeed (null = fixed duration)
         { 3, 10, 50, null },     // Should not exceed max concurrency
         { 1, 5, 20, null },      // Sequential execution
         { 5, 100, 5, null },     // Even with high load, all jobs should complete
         { 4, 20, 0, 42 }         // Variable durations 10-100ms (seeded)
      };

   [Theory]
   [MemberData(nameof(ConcurrencyCases))]
   public async Task ConcurrentJobs_ShouldRespectMaxConcurrencyLimit(int maxConcurrentJobs, int totalJobs, int durationMs, int? randomSeed)
   {
      // Arrange
      var tracker = new ConcurrencyTracker();
      var harness = new JobRunnerHarness(maxConcurrentJobs);
      var random = randomSeed.HasValue ? new Random(randomSeed.Value) : null;

      for (var i = 0; i < totalJobs; i++)
      {
         var duration = random is null
            ? TimeSpan.FromMilliseconds(durationMs)
            : TimeSpan.FromMilliseconds(random.Next(10, 100));

         await harness.Scheduler.PerformAsapAsync<ConcurrencyTrackingJob, ConcurrencyTrackingJob.Parameters>(
            new ConcurrencyTrackingJob.Parameters {
               Tracker = tracker,
               ExecutionDuration = duration
            },
            CancellationToken);
      }

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert
      tracker.TotalJobsStarted.Should().Be(totalJobs);
      tracker.TotalJobsFinished.Should().Be(totalJobs);
      tracker.MaxObservedConcurrency.Should().BeLessThanOrEqualTo(maxConcurrentJobs);
      tracker.CurrentConcurrency.Should().Be(0);
      harness.Storage.ScheduledJobs.Should().BeEmpty();
      harness.Storage.InProgressJobs.Should().BeEmpty();
   }

   [Fact]
   public async Task ConcurrentJobs_ShouldExecuteConcurrently_WhenBelowLimit()
   {
      // Arrange - this case has a unique assertion (concurrency > 1) that doesn't fit the parameterized template.
      const int maxConcurrentJobs = 10;
      const int totalJobs = 5;
      var tracker = new ConcurrencyTracker();
      var harness = new JobRunnerHarness(maxConcurrentJobs);

      for (var i = 0; i < totalJobs; i++)
      {
         await harness.Scheduler.PerformAsapAsync<ConcurrencyTrackingJob, ConcurrencyTrackingJob.Parameters>(
            new ConcurrencyTrackingJob.Parameters {
               Tracker = tracker,
               ExecutionDuration = TimeSpan.FromMilliseconds(100)
            },
            CancellationToken);
      }

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert
      tracker.TotalJobsStarted.Should().Be(totalJobs);
      tracker.TotalJobsFinished.Should().Be(totalJobs);
      tracker.MaxObservedConcurrency.Should().BeGreaterThan(1, "Jobs should execute concurrently when below the max concurrency limit");
   }

   [Fact]
   public async Task ConcurrentJobs_ScheduledInOneBatchWithTheSameGroup_RunOneAtATime()
   {
      // Arrange - ten job slots are free, but the group allows one job at a time.
      var tracker = new ConcurrencyTracker();
      var harness = new JobRunnerHarness(maxConcurrentJobs: 10);
      var jobs = Enumerable.Range(1, 3)
         .Select(i => (
            Parameters: new ConcurrencyTrackingJob.Parameters { Tracker = tracker, ExecutionDuration = TimeSpan.FromMilliseconds(50) },
            Options: new JobScheduleOptions { JobName = $"grouped-{i}", Group = "batch-group" }
         ))
         .ToArray();

      await harness.Scheduler.PerformAsapAsync<ConcurrencyTrackingJob, ConcurrencyTrackingJob.Parameters>(jobs, CancellationToken);

      // Act
      await harness.RunAndDrainAsync(CancellationToken);

      // Assert
      tracker.TotalJobsFinished.Should().Be(3);
      tracker.MaxObservedConcurrency.Should().Be(1, "jobs of one group run one at a time");
   }
}
