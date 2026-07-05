using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public class JobDataTests
{
   private const string UnresolvableType = "NonExistent.Type, NonExistent.Assembly";

   [Theory]
   [InlineData(UnresolvableType, "object")]
   [InlineData("object", UnresolvableType)]
   public void ToJobStoreItem_ShouldReturnNull_WhenTypeCannotBeResolved(string jobType, string parametersType)
   {
      // Arrange
      var jobData = new JobData {
         Id = Guid.NewGuid(),
         JobType = Resolve(jobType, typeof(object)),
         ParametersType = Resolve(parametersType, typeof(object)),
         ParametersJson = "{}",
         CronExpression = null,
         ApplicationName = "test-app",
         JobName = "test-job",
         JobGroup = null,
         Culture = null,
         UICulture = null,
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
         Culture = "nl-NL",
         UICulture = "de-DE",
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
      result.CultureName.Should().Be("nl-NL");
      result.UICultureName.Should().Be("de-DE");
   }

   [Theory]
   [InlineData("nl-NL", "de-DE")]   // distinct formatting and UI cultures
   [InlineData("", "")]             // invariant culture
   [InlineData(null, null)]         // no captured culture
   public void FromJobStoreItem_ThenToJobStoreItem_PreservesCulture(string? culture, string? uiCulture)
   {
      // Arrange
      var item = new JobStoreItem {
         JobType = typeof(object),
         Parameters = "test-parameter",
         Options = new JobScheduleOptions { JobName = "test-job" },
         PerformAt = DateTime.UtcNow,
         CultureName = culture,
         UICultureName = uiCulture
      };

      // Act
      var roundTripped = JobData.FromJobStoreItem("test-app", item).ToJobStoreItem();

      // Assert
      roundTripped.Should().NotBeNull();
      roundTripped!.CultureName.Should().Be(culture);
      roundTripped.UICultureName.Should().Be(uiCulture);
   }

   private static string Resolve(string token, Type fallback) =>
      token == UnresolvableType ? UnresolvableType : fallback.AssemblyQualifiedName!;
}
