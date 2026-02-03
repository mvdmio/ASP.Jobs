using Microsoft.Extensions.DependencyInjection;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

public class JobTestServices
{
   public ServiceCollection Services { get; set; }
   
   public JobTestServices()
   {
      Services = new ServiceCollection();
      Services.RegisterJob<TestJob>();
      Services.RegisterJob<ConcurrencyTrackingJob>();
   }
}