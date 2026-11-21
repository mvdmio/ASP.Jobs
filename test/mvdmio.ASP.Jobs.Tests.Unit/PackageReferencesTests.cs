using AwesomeAssertions;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public sealed class PackageReferencesTests
{
   [Fact]
   public void LibraryAssembly_ReferencesNoSerilogAssembly()
   {
      // Act
      var referenced = typeof(IJobScheduler).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToList();

      // Assert
      referenced.Should().Contain("Microsoft.Extensions.Logging.Abstractions");
      referenced.Should().NotContain(x => x != null && x.StartsWith("Serilog", StringComparison.OrdinalIgnoreCase));
   }
}
