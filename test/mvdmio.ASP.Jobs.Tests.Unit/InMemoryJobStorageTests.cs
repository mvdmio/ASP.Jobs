﻿using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public class InMemoryJobStorageTests
{
   private readonly TestClock _clock;
   private readonly InMemoryJobStorage _sut;

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public InMemoryJobStorageTests()
   {
      _clock = new TestClock();
      _sut = new InMemoryJobStorage(_clock);
   }

   [Fact]
   public async Task AddJob_ShouldAddJob_WhenEmpty()
   {
      // Arrange

      // Act
      await _sut.ScheduleJobAsync(
         new JobStoreItem {
            JobType = typeof(TestJob),
            Parameters = null!,
            PerformAt = _clock.UtcNow,
            Options = new JobScheduleOptions()
         },
         CancellationToken
      );

      // Assert
      _sut.ScheduledJobs.Should().HaveCount(1);
   }

   [Fact]
   public async Task AddJob_ShouldNotAddJobWithSameIdTwice()
   {
      // Arrange
      var jobId = Guid.NewGuid().ToString();

      // Act
      _ = await AddNewJobStoreItem(id: jobId);
      var secondJobItem = await AddNewJobStoreItem(id: jobId);

      // Assert
      _sut.ScheduledJobs.Should().HaveCount(1);
      _sut.ScheduledJobs.First().Should().Be(secondJobItem);
   }

   [Fact]
   public async Task AddJobs()
   {
      // Arrange

      // Act
      var jobs = await AddNewJobStoreItems(100);

      // Assert
      _sut.ScheduledJobs.Should().HaveCount(100);
      _sut.ScheduledJobs.Should().BeEquivalentTo(jobs);
   }

   [Fact]
   public async Task RemoveJob_ShouldNotDoAnything_WhenEmpty()
   {
      // Arrange
      var jobId = Guid.NewGuid().ToString();

      // Act
      var remove = async () => await _sut.FinalizeJobAsync(jobId, CancellationToken);

      // Assert
      await remove.Should().NotThrowAsync();
   }

   [Fact]
   public async Task RemoveJob_ShouldRemoveJob_WhenExists()
   {
      // Arrange
      var jobStoreItem = await AddNewJobStoreItem();
      _ = await _sut.StartNextJobAsync(CancellationToken);

      // Act
      await _sut.FinalizeJobAsync(jobStoreItem.Options.JobName, CancellationToken);

      // Assert
      _sut.ScheduledJobs.Should().HaveCount(0);
      _sut.InProgressJobs.Should().HaveCount(0);
   }

   [Fact]
   public async Task StartNextJob_ShouldReturnNull_WhenEmpty()
   {
      // Arrange

      // Act
      var result = await _sut.StartNextJobAsync(CancellationToken);

      // Assert
      result.Should().BeNull();
   }

   [Fact]
   public async Task StartNextJob_ShouldReturnJob_WhenExists()
   {
      // Arrange
      var jobStoreItem = await AddNewJobStoreItem();

      // Act
      var result = await _sut.StartNextJobAsync(CancellationToken);

      // Assert
      result.Should().BeEquivalentTo(jobStoreItem);
   }

   [Fact]
   public async Task StartNextJob_ShouldReturnNull_WhenAllJobsAreInProgress()
   {
      // Arrange
      _ = await AddNewJobStoreItem();

      // Act
      _ = await _sut.StartNextJobAsync(CancellationToken);
      var result = await _sut.StartNextJobAsync(CancellationToken);

      // Assert
      result.Should().BeNull();
   }

   [Fact]
   public async Task StartNextJob_ShouldSkipFutureJobs()
   {
      // Arrange
      _ = await AddNewJobStoreItem(_clock.UtcNow.AddDays(1));
      var executableJobItem = await AddNewJobStoreItem();

      // Act
      var result = await _sut.StartNextJobAsync(CancellationToken);

      // Assert
      result.Should().BeEquivalentTo(executableJobItem);
   }

   [Fact]
   public async Task StartNextJob_ShouldSkipJobsInGroupsThatAreAlreadyInProgress()
   {
      // Arrange
      var firstInGroup = await AddNewJobStoreItem(group: "test");
      _ = await AddNewJobStoreItem(group: "test");

      // Act
      var firstResult = await _sut.StartNextJobAsync(CancellationToken);
      var secondResult = await _sut.StartNextJobAsync(CancellationToken);

      // Assert
      firstResult.Should().BeEquivalentTo(firstInGroup);
      secondResult.Should().BeNull();
   }

   [Fact]
   public async Task StartNextJob_ShouldReturnNextInGroupAfterPreviousJobIsFinished()
   {
      // Arrange
      var firstInGroup = await AddNewJobStoreItem(group: "test");
      var secondInGroup = await AddNewJobStoreItem(group: "test");

      // Act
      _ = await _sut.StartNextJobAsync(CancellationToken);
      await _sut.FinalizeJobAsync(firstInGroup.Options.JobName, CancellationToken);

      var secondResult = await _sut.StartNextJobAsync(CancellationToken);

      // Assert
      secondResult.Should().Be(secondInGroup);
   }

   [Fact] // There was a bug where jobs were sorted on their ID instead of their scheduled time.
   public async Task StartNextJob_ShouldReturnJobsInCorrectOrder()
   {
      // Arrange
      var job1 = await AddNewJobStoreItem(id: "A", performAt: DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(1))); // Should be executed last
      var job2 = await AddNewJobStoreItem(id: "B", performAt: DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(2))); // Should be executed second
      var job3 = await AddNewJobStoreItem(id: "C", performAt: DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(3))); // Should be executed first

      // Act
      var firstJob = await _sut.StartNextJobAsync(CancellationToken);
      var secondJob = await _sut.StartNextJobAsync(CancellationToken);
      var thirdJob = await _sut.StartNextJobAsync(CancellationToken);

      // Assert
      firstJob.Should().Be(job3);
      secondJob.Should().Be(job2);
      thirdJob.Should().Be(job1);
   }

   [Fact]
   public async Task ReschedulingJobThatIsCurrentlyInProgress()
   {
      // Arrange

      // Act
      _ = await AddNewJobStoreItem(id: "TestId");
      _ = await _sut.StartNextJobAsync(CancellationToken);
      var scheduledJob2 = await AddNewJobStoreItem(id: "TestId");
      await _sut.FinalizeJobAsync("TestId", CancellationToken);
      var job2 = await _sut.StartNextJobAsync(CancellationToken);

      // Assert
      job2.Should().Be(scheduledJob2);
   }

   private async Task<JobStoreItem> AddNewJobStoreItem(DateTimeOffset? performAt = null, string? id = null, string? group = null)
   {
      var jobItem = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = null!,
         PerformAt = performAt ?? _clock.UtcNow,
         Options = new JobScheduleOptions {
            JobName = id ?? Guid.NewGuid().ToString(),
            Group = group
         }
      };

      await _sut.ScheduleJobAsync(jobItem, CancellationToken);

      return jobItem;
   }

   private async Task<JobStoreItem[]> AddNewJobStoreItems(int count, DateTime? performAt = null, string? group = null)
   {
      var items = Enumerable.Range(0, count)
         .Select(_ => new JobStoreItem {
               JobType = typeof(TestJob),
               Parameters = null!,
               PerformAt = performAt ?? _clock.UtcNow,
               Options = new JobScheduleOptions {
                  JobName = Guid.NewGuid().ToString(),
                  Group = group
               }
            }
         )
         .ToArray();

      await _sut.ScheduleJobsAsync(items, CancellationToken);
      return items;
   }
}