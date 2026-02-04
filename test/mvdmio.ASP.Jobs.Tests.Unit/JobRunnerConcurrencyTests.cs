using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

/// <summary>
/// Tests for verifying concurrent job execution behavior in the JobRunnerService.
/// </summary>
public sealed class JobRunnerConcurrencyTests
{
   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   [Fact]
   public async Task ConcurrentJobs_ShouldNotExceedMaxConcurrency()
   {
      // Arrange
      const int maxConcurrentJobs = 3;
      const int totalJobs = 10;
      var tracker = new ConcurrencyTracker();
      
      var (runner, scheduler, jobStorage) = CreateServices(maxConcurrentJobs);
      
      // Schedule multiple jobs that take some time to execute
      for (var i = 0; i < totalJobs; i++)
      {
         var parameters = new ConcurrencyTrackingJob.Parameters {
            Tracker = tracker,
            ExecutionDuration = TimeSpan.FromMilliseconds(50)
         };
         await scheduler.PerformAsapAsync<ConcurrencyTrackingJob, ConcurrencyTrackingJob.Parameters>(parameters, CancellationToken);
      }
      
      // Act
      await runner.StartAsync(CancellationToken);
      await WaitForAllJobsToFinishAsync(jobStorage);
      await runner.StopAsync(CancellationToken);
      
      // Assert
      tracker.TotalJobsStarted.Should().Be(totalJobs);
      tracker.TotalJobsFinished.Should().Be(totalJobs);
      tracker.MaxObservedConcurrency.Should().BeLessThanOrEqualTo(maxConcurrentJobs);
      tracker.CurrentConcurrency.Should().Be(0);
   }

   [Fact]
   public async Task ConcurrentJobs_ShouldExecuteConcurrently_WhenBelowLimit()
   {
      // Arrange
      const int maxConcurrentJobs = 10;
      const int totalJobs = 5;
      var tracker = new ConcurrencyTracker();
      
      var (runner, scheduler, jobStorage) = CreateServices(maxConcurrentJobs);
      
      // Schedule fewer jobs than the max concurrency, with enough delay to overlap
      for (var i = 0; i < totalJobs; i++)
      {
         var parameters = new ConcurrencyTrackingJob.Parameters {
            Tracker = tracker,
            ExecutionDuration = TimeSpan.FromMilliseconds(100)
         };
         await scheduler.PerformAsapAsync<ConcurrencyTrackingJob, ConcurrencyTrackingJob.Parameters>(parameters, CancellationToken);
      }
      
      // Act
      await runner.StartAsync(CancellationToken);
      await WaitForAllJobsToFinishAsync(jobStorage);
      await runner.StopAsync(CancellationToken);
      
      // Assert
      tracker.TotalJobsStarted.Should().Be(totalJobs);
      tracker.TotalJobsFinished.Should().Be(totalJobs);
      // Jobs should have executed concurrently (max observed should be > 1)
      tracker.MaxObservedConcurrency.Should().BeGreaterThan(1, 
         "Jobs should execute concurrently when below the max concurrency limit");
   }

   [Fact]
   public async Task ConcurrentJobs_WithMaxConcurrencyOf1_ShouldExecuteSequentially()
   {
      // Arrange
      const int maxConcurrentJobs = 1;
      const int totalJobs = 5;
      var tracker = new ConcurrencyTracker();
      
      var (runner, scheduler, jobStorage) = CreateServices(maxConcurrentJobs);
      
      for (var i = 0; i < totalJobs; i++)
      {
         var parameters = new ConcurrencyTrackingJob.Parameters {
            Tracker = tracker,
            ExecutionDuration = TimeSpan.FromMilliseconds(20)
         };
         await scheduler.PerformAsapAsync<ConcurrencyTrackingJob, ConcurrencyTrackingJob.Parameters>(parameters, CancellationToken);
      }
      
      // Act
      await runner.StartAsync(CancellationToken);
      await WaitForAllJobsToFinishAsync(jobStorage);
      await runner.StopAsync(CancellationToken);
      
      // Assert
      tracker.TotalJobsStarted.Should().Be(totalJobs);
      tracker.TotalJobsFinished.Should().Be(totalJobs);
      tracker.MaxObservedConcurrency.Should().Be(1, 
         "Only one job should run at a time when MaxConcurrentJobs is 1");
   }

   [Fact]
   public async Task ConcurrentJobs_ShouldCompleteAllJobs_EvenWithHighLoad()
   {
      // Arrange
      const int maxConcurrentJobs = 5;
      const int totalJobs = 100;
      var tracker = new ConcurrencyTracker();
      
      var (runner, scheduler, jobStorage) = CreateServices(maxConcurrentJobs);
      
      for (var i = 0; i < totalJobs; i++)
      {
         var parameters = new ConcurrencyTrackingJob.Parameters {
            Tracker = tracker,
            ExecutionDuration = TimeSpan.FromMilliseconds(5)
         };
         await scheduler.PerformAsapAsync<ConcurrencyTrackingJob, ConcurrencyTrackingJob.Parameters>(parameters, CancellationToken);
      }
      
      // Act
      await runner.StartAsync(CancellationToken);
      await WaitForAllJobsToFinishAsync(jobStorage);
      await runner.StopAsync(CancellationToken);
      
      // Assert
      tracker.TotalJobsStarted.Should().Be(totalJobs);
      tracker.TotalJobsFinished.Should().Be(totalJobs);
      tracker.MaxObservedConcurrency.Should().BeLessThanOrEqualTo(maxConcurrentJobs);
      tracker.CurrentConcurrency.Should().Be(0);
      
      jobStorage.ScheduledJobs.Should().HaveCount(0);
      jobStorage.InProgressJobs.Should().HaveCount(0);
   }

   [Fact]
   public async Task ConcurrentJobs_ShouldRespectConcurrencyLimit_WithVariableJobDurations()
   {
      // Arrange
      const int maxConcurrentJobs = 4;
      const int totalJobs = 20;
      var tracker = new ConcurrencyTracker();
      var random = new Random(42); // Fixed seed for reproducibility
      
      var (runner, scheduler, jobStorage) = CreateServices(maxConcurrentJobs);
      
      // Schedule jobs with varying execution durations
      for (var i = 0; i < totalJobs; i++)
      {
         var parameters = new ConcurrencyTrackingJob.Parameters {
            Tracker = tracker,
            ExecutionDuration = TimeSpan.FromMilliseconds(random.Next(10, 100))
         };
         await scheduler.PerformAsapAsync<ConcurrencyTrackingJob, ConcurrencyTrackingJob.Parameters>(parameters, CancellationToken);
      }
      
      // Act
      await runner.StartAsync(CancellationToken);
      await WaitForAllJobsToFinishAsync(jobStorage);
      await runner.StopAsync(CancellationToken);
      
      // Assert
      tracker.TotalJobsStarted.Should().Be(totalJobs);
      tracker.TotalJobsFinished.Should().Be(totalJobs);
      tracker.MaxObservedConcurrency.Should().BeLessThanOrEqualTo(maxConcurrentJobs);
   }

   private static (JobRunnerService Runner, JobScheduler Scheduler, InMemoryJobStorage Storage) CreateServices(int maxConcurrentJobs)
   {
      var services = new JobTestServices().Services;
      var clock = new TestClock();
      var jobStorage = new InMemoryJobStorage();
      
      var configuration = new JobRunnerOptions {
         MaxConcurrentJobs = maxConcurrentJobs,
         JobChannelCapacity = 50
      };

      services.AddSingleton<IJobStorage>(jobStorage);
      
      var serviceProvider = services.BuildServiceProvider();
      
      var scheduler = new JobScheduler(serviceProvider, jobStorage, clock);
      var runner = new JobRunnerService(serviceProvider, Options.Create(configuration), NullLogger<JobRunnerService>.Instance);
      
      return (runner, scheduler, jobStorage);
   }

   private async Task WaitForAllJobsToFinishAsync(InMemoryJobStorage jobStorage)
   {
      do
      {
         var scheduledJobs = await jobStorage.GetScheduledJobsAsync(CancellationToken);
         var inProgressJobs = await jobStorage.GetInProgressJobsAsync(CancellationToken);
         
         if (scheduledJobs.Any() || inProgressJobs.Any())
            await Task.Delay(10, CancellationToken);
         else
            break;
      }
      while (true);
   }
}
