using AwesomeAssertions;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage.Data;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Internals.Storage.Postgres.Data;
using mvdmio.ASP.Jobs.Tests.Integration.Fixtures;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Integration.Postgres;

/// <summary>
///    Tests that a CRON job whose end the database refuses once is still scheduled again, against real PostgreSQL: the
///    runner runs it on a real <see cref="Internals.Storage.Postgres.PostgresJobStorage"/> that refuses the first
///    <see cref="IJobStorage.FinalizeJobAsync"/>. The first wait of the retry loop is a real 1 second.
/// </summary>
public sealed class PostgresJobEndRetryTests : IAsyncLifetime
{
   private const string JobName = "DailyJob";

   private readonly PostgresFixture _fixture;
   private readonly CancellationTokenSource _cts;
   private readonly PostgresStorageHarness _harness;
   private readonly RefusingFirstFinalizeStorage _storage;
   private readonly JobRunnerService _runner;

   private CancellationToken CancellationToken => _cts.Token;

   public PostgresJobEndRetryTests(PostgresFixture fixture)
   {
      _fixture = fixture;
      _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      _cts.CancelAfter(TimeSpan.FromSeconds(15));

      _harness = new PostgresStorageHarness(fixture);
      _storage = new RefusingFirstFinalizeStorage(_harness.Storage);

      var services = new JobTestServices().Services;
      services.AddSingleton<IJobStorage>(_storage);
      _runner = new JobRunnerService(services.BuildServiceProvider(), Options.Create(new JobRunnerOptions()), NullLogger<JobRunnerService>.Instance, _harness.Clock);
   }

   public async ValueTask InitializeAsync()
   {
      await _fixture.ResetAsync();
      await _harness.Storage.InitializeAsync(CancellationToken);
      await _harness.InstanceRepository.RegisterInstance(CancellationToken);
   }

   public ValueTask DisposeAsync() => ValueTask.CompletedTask;

   [Fact]
   public async Task CronJob_WhoseFirstFinalizeIsRefused_LeavesOnlyTheNextOccurrenceInTheDatabase()
   {
      // Arrange: a daily job, so the next occurrence is far enough away for the runner never to Claim it during the test.
      var cron = CronExpression.Parse("0 0 * * *");
      await _harness.Storage.ScheduleJobAsync(JobStoreItemFactory.MakeTestJob(jobName: JobName, performAt: _harness.Clock.UtcNow, cronExpression: cron), CancellationToken);

      // Act: the first Finalize is refused, so the row is stored again after the real 1 s wait.
      var before = DateTime.UtcNow;
      await _runner.StartAsync(CancellationToken);
      await WaitUntilAsync(() => _storage.FinalizeCalls >= 2 && GetJobsFromDatabase() is [{ StartedAt: null }]);
      await _runner.StopAsync(CancellationToken);
      var after = DateTime.UtcNow;

      // Assert: the refused first call was followed by a second, no Claimed row is left and the one row is the job's next occurrence.
      _storage.FinalizeCalls.Should().Be(2);
      var rows = GetJobsFromDatabase();
      var row = rows.Should().ContainSingle(x => x.JobName == JobName).Subject;
      row.StartedAt.Should().BeNull();
      row.StartedBy.Should().BeNull();
      row.PerformAt.Should().BeOneOf(NextMidnightAfter(before), NextMidnightAfter(after));
   }

   private async Task WaitUntilAsync(Func<bool> condition)
   {
      while (!condition())
         await Task.Delay(10, CancellationToken);
   }

   private List<JobData> GetJobsFromDatabase()
   {
      return _fixture.DatabaseConnection.Dapper.Query<JobData>("SELECT * FROM mvdmio.jobs").ToList();
   }

   private static DateTime NextMidnightAfter(DateTime utc)
   {
      return utc.Date.AddDays(1);
   }

   /// <summary>Forwards every call to the real storage, except that the first <see cref="FinalizeJobAsync"/> throws.</summary>
   private sealed class RefusingFirstFinalizeStorage : IJobStorage
   {
      private readonly IJobStorage _inner;
      private int _finalizeCalls;

      public int FinalizeCalls => Volatile.Read(ref _finalizeCalls);

      public RefusingFirstFinalizeStorage(IJobStorage inner)
      {
         _inner = inner;
      }

      public Task FinalizeJobAsync(JobStoreItem job, CancellationToken ct = default)
      {
         return Interlocked.Increment(ref _finalizeCalls) == 1
            ? Task.FromException(new InvalidOperationException("The database refused the write"))
            : _inner.FinalizeJobAsync(job, ct);
      }

      public Task InitializeAsync(CancellationToken ct = default) => _inner.InitializeAsync(ct);

      public Task ScheduleJobAsync(JobStoreItem jobItem, CancellationToken ct = default) => _inner.ScheduleJobAsync(jobItem, ct);

      public Task ScheduleCronJobAsync(JobStoreItem jobItem, CancellationToken ct = default) => _inner.ScheduleCronJobAsync(jobItem, ct);

      public Task ScheduleJobsAsync(IEnumerable<JobStoreItem> items, CancellationToken ct = default) => _inner.ScheduleJobsAsync(items, ct);

      public Task<JobStoreItem?> WaitForNextJobAsync(CancellationToken ct = default) => _inner.WaitForNextJobAsync(ct);

      public Task<bool> TryScheduleRetryAsync(JobStoreItem job, DateTime nextAttemptAtUtc, CancellationToken ct = default) => _inner.TryScheduleRetryAsync(job, nextAttemptAtUtc, ct);

      public Task<IEnumerable<JobStoreItem>> GetScheduledJobsAsync(CancellationToken ct = default) => _inner.GetScheduledJobsAsync(ct);

      public Task<IEnumerable<JobStoreItem>> GetInProgressJobsAsync(CancellationToken ct = default) => _inner.GetInProgressJobsAsync(ct);

      public Task DeleteJobByIdAsync(Guid jobId, CancellationToken ct = default) => _inner.DeleteJobByIdAsync(jobId, ct);
   }
}
