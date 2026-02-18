using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public class JobDataTests
{
   [Fact]
   public void ToJobStoreItem_ShouldReturnNull_WhenJobTypeCannotBeResolved()
   {
      // Arrange
      var jobData = new JobData {
         Id = Guid.NewGuid(),
         JobType = "NonExistent.JobType, NonExistent.Assembly",
         ParametersType = typeof(object).AssemblyQualifiedName!,
         ParametersJson = "{}",
         CronExpression = null,
         ApplicationName = "test-app",
         JobName = "test-job",
         JobGroup = null,
         PerformAt = DateTime.UtcNow
      };

      // Act
      var result = jobData.ToJobStoreItem();

      // Assert
      result.Should().BeNull();
   }

   [Fact]
   public void ToJobStoreItem_ShouldReturnNull_WhenParametersTypeCannotBeResolved()
   {
      // Arrange
      var jobData = new JobData {
         Id = Guid.NewGuid(),
         JobType = typeof(object).AssemblyQualifiedName!,
         ParametersType = "NonExistent.ParametersType, NonExistent.Assembly",
         ParametersJson = "{}",
         CronExpression = null,
         ApplicationName = "test-app",
         JobName = "test-job",
         JobGroup = null,
         PerformAt = DateTime.UtcNow
      };

      // Act
      var result = jobData.ToJobStoreItem();

      // Assert
      result.Should().BeNull();
   }

   [Fact]
   public void ToJobStoreItem_ShouldReturnJobStoreItem_WhenTypesCanBeResolved()
   {
      // Arrange
      var jobId = Guid.NewGuid();
      var performAt = DateTime.UtcNow;
      var jobData = new JobData {
         Id = jobId,
         JobType = typeof(object).AssemblyQualifiedName!,
         ParametersType = typeof(string).AssemblyQualifiedName!,
         ParametersJson = "\"test-parameter\"",
         CronExpression = null,
         ApplicationName = "test-app",
         JobName = "test-job",
         JobGroup = "test-group",
         PerformAt = performAt
      };

      // Act
      var result = jobData.ToJobStoreItem();

      // Assert
      result.Should().NotBeNull();
      result!.JobId.Should().Be(jobId);
      result.JobType.Should().Be(typeof(object));
      result.Parameters.Should().Be("test-parameter");
      result.PerformAt.Should().Be(performAt);
      result.Options.JobName.Should().Be("test-job");
      result.Options.Group.Should().Be("test-group");
   }
}
