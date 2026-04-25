using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Repository;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using NSubstitute;

namespace mvdmio.ASP.Jobs.Tests.Integration.Fixtures;

/// <summary>
/// Encapsulates the Postgres storage trio (<see cref="PostgresJobStorageConfiguration"/>,
/// <see cref="PostgresJobInstanceRepository"/>, <see cref="PostgresJobStorage"/>) plus a <see cref="TestClock"/>
/// for re-use across the Postgres integration tests.
/// </summary>
internal sealed class PostgresStorageHarness
{
   public TestClock Clock { get; }
   public PostgresJobStorageConfiguration Configuration { get; }
   public PostgresJobInstanceRepository InstanceRepository { get; }
   public PostgresJobStorage Storage { get; }

   public PostgresStorageHarness(PostgresFixture fixture, string instanceId = "test-instance", string applicationName = "test-application")
   {
      Clock = new TestClock();
      Configuration = new PostgresJobStorageConfiguration {
         InstanceId = instanceId,
         ApplicationName = applicationName,
         DatabaseConnectionString = fixture.ConnectionString
      };

      var optionsWrapper = Options.Create(Configuration);
      InstanceRepository = new PostgresJobInstanceRepository(fixture.DatabaseConnectionFactory, optionsWrapper, Clock);
      Storage = new PostgresJobStorage(fixture.DatabaseConnectionFactory, optionsWrapper, Substitute.For<ILogger<PostgresJobStorage>>(), Clock);
   }
}
