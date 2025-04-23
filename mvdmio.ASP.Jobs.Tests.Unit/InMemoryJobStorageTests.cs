using FluentAssertions;
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
      await _sut.AddJobAsync(new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = null!,
         PerformAt = _clock.UtcNow,
         Options = new JobScheduleOptions()
      }, CancellationToken);

      // Assert
      _sut.Jobs.Should().HaveCount(1);
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
      _sut.Jobs.Should().HaveCount(1);
      _sut.Jobs.First().Should().Be(secondJobItem);
   }
   
   [Fact]
   public async Task RemoveJob_ShouldNotDoAnything_WhenEmpty()
   {
      // Arrange
      var jobId = Guid.NewGuid().ToString();
      
      // Act
      var remove = async () => await _sut.RemoveJobAsync(jobId, CancellationToken);
      
      // Assert
      await remove.Should().NotThrowAsync();
   }

   [Fact]
   public async Task RemoveJob_ShouldRemoveJob_WhenExists()
   {
      // Arrange
      var jobStoreItem = await AddNewJobStoreItem();
      
      // Act
      await _sut.RemoveJobAsync(jobStoreItem.Options.JobId, CancellationToken);

      // Assert
      _sut.Jobs.Should().HaveCount(0);
   }
   
   [Fact]
   public async Task GetNextJob_ShouldReturnNull_WhenEmpty()
   {
      // Arrange
      
      // Act
      var result = await _sut.GetNextJobAsync(CancellationToken);

      // Assert
      result.Should().BeNull();
   }

   [Fact]
   public async Task GetNextJob_ShouldReturnJob_WhenExists()
   {
      // Arrange
      var jobStoreItem = await AddNewJobStoreItem();
      
      // Act
      var result = await _sut.GetNextJobAsync(CancellationToken);
      
      // Assert
      result.Should().BeEquivalentTo(jobStoreItem);
   }
   
   [Fact]
   public async Task GetNextJob_ShouldReturnNull_WhenAllJobsAreInProgress()
   {
      // Arrange
      _ = await AddNewJobStoreItem();
      
      // Act
      _ = await _sut.GetNextJobAsync(CancellationToken);
      var result = await _sut.GetNextJobAsync(CancellationToken);
      
      // Assert
      result.Should().BeNull();
   }
   
   [Fact]
   public async Task GetNextJob_ShouldSkipFutureJobs()
   {
      // Arrange
      _ = await AddNewJobStoreItem(_clock.UtcNow.AddDays(1));
      var executableJobItem = await AddNewJobStoreItem();
      
      // Act
      var result = await _sut.GetNextJobAsync(CancellationToken);
      
      // Assert
      result.Should().BeEquivalentTo(executableJobItem);
   }
   
   [Fact]
   public async Task GetNextJob_ShouldSkipJobsInGroupsThatAreAlreadyInProgress()
   {
      // Arrange
      var firstInGroup = await AddNewJobStoreItem(group: "test");
      _ = await AddNewJobStoreItem(group: "test");
      
      // Act
      var firstResult = await _sut.GetNextJobAsync(CancellationToken);
      var secondResult = await _sut.GetNextJobAsync(CancellationToken);
      
      // Assert
      firstResult.Should().BeEquivalentTo(firstInGroup);
      secondResult.Should().BeNull();
   }
   
   [Fact]
   public async Task GetNextJob_ShouldReturnNextInGroupAfterPreviousJobIsFinished()
   {
      // Arrange
      var firstInGroup = await AddNewJobStoreItem(group: "test");
      var secondInGroup = await AddNewJobStoreItem(group: "test");
      
      // Act
      _ = await _sut.GetNextJobAsync(CancellationToken);
      await _sut.RemoveJobAsync(firstInGroup.Options.JobId, CancellationToken);
      
      var secondResult = await _sut.GetNextJobAsync(CancellationToken);
      
      // Assert
      secondResult.Should().Be(secondInGroup);
   }
   
   private async Task<JobStoreItem> AddNewJobStoreItem(DateTime? performAt = null, string? id = null, string? group = null)
   {
      var jobItem = new JobStoreItem {
         JobType = typeof(TestJob),
         Parameters = null!,
         PerformAt = performAt ?? _clock.UtcNow,
         Options = new JobScheduleOptions {
            JobId = id ?? Guid.NewGuid().ToString(),
            Group = group
         }
      };

      await _sut.AddJobAsync(jobItem, CancellationToken);
      
      return jobItem;
   }
}