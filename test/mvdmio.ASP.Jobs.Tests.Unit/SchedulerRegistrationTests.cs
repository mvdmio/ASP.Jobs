using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.ASP.Jobs.Internals;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public sealed class SchedulerRegistrationTests
{
   [Fact]
   public void AddJobs_WithOnlyTheScheduler_ResolvesTheSchedulerWithoutLoggingSetUp()
   {
      // Arrange
      var services = new ServiceCollection();
      services.AddJobs(options => options.IsRunnerEnabled = false);

      // Act
      using var provider = services.BuildServiceProvider();
      var scheduler = provider.GetRequiredService<IJobScheduler>();

      // Assert
      scheduler.Should().BeOfType<JobScheduler>();
   }
}
