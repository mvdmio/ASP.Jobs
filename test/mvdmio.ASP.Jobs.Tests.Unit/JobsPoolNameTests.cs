using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public class JobsPoolNameTests
{
   [Fact]
   public void Create_UsesEntryAssemblyName_WhenPresent()
   {
      JobsPoolName.Create("Foo.Web", "bar").Should().Be("Foo.Web.Jobs");
   }

   [Theory]
   [InlineData(null)]
   [InlineData("")]
   public void Create_FallsBackToApplicationName_WhenEntryAssemblyNameIsMissing(string? entryAssemblyName)
   {
      JobsPoolName.Create(entryAssemblyName, "bar").Should().Be("bar.Jobs");
   }
}
