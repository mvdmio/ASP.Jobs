using Cronos;

namespace mvdmio.ASP.Jobs.Internals.Storage.Data;

internal class CronJobStoreItem
{
   public required CronExpression CronExpression { get; set; }
   public required JobStoreItem Job { get; set; }
}