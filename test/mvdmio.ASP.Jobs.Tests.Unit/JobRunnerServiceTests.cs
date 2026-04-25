using AwesomeAssertions;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public sealed class JobRunnerServiceTests
{
   private readonly JobRunnerHarness _harness = new();
   private readonly Random _random = new(1);

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   [Fact]
   public async Task RunSingleJob()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      AssertStorageDrained();
      AssertExecuted(job1);
   }

   [Fact]
   public async Task RunMultipleJobs()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();
      var job2 = await ScheduleTestJobAsync();

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      AssertStorageDrained();
      AssertExecuted(job1);
      AssertExecuted(job2);
   }

   [Fact]
   public async Task HandleCrash()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();
      job1.ThrowOnExecute = new Exception("Crashed job test");

      var job2 = await ScheduleTestJobAsync();

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      AssertStorageDrained();
      AssertCrashed(job1);
      AssertExecuted(job2);
   }

   [Theory]
   [InlineData(10, null)]      // Many jobs with random delays
   [InlineData(1000, 1)]       // Many jobs in parallel with fixed 1ms delay
   public async Task HandleManyJobs(int count, int? fixedDelayMs)
   {
      // Arrange
      var jobs = new List<TestJob.Parameters>(count);
      var delay = fixedDelayMs.HasValue ? TimeSpan.FromMilliseconds(fixedDelayMs.Value) : (TimeSpan?)null;

      for (var i = 0; i < count; i++)
         jobs.Add(await ScheduleTestJobAsync(delay));

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      AssertStorageDrained();
      jobs.ForEach(AssertExecuted);
   }

   // TODO: Test CRON jobs. Requires improved test harness with more refined control over time and timing.

   private async Task<TestJob.Parameters> ScheduleTestJobAsync(TimeSpan? delay = null)
   {
      delay ??= TimeSpan.FromMilliseconds(_random.Next(1, 10));

      var parameters = new TestJob.Parameters {
         Delay = delay.Value
      };

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);
      return parameters;
   }

   private void AssertStorageDrained()
   {
      _harness.Storage.ScheduledJobs.Should().BeEmpty();
      _harness.Storage.InProgressJobs.Should().BeEmpty();
   }

   private static void AssertExecuted(TestJob.Parameters job) =>
      job.Should().BeEquivalentTo(new { Executed = true, Crashed = false });

   private static void AssertCrashed(TestJob.Parameters job) =>
      job.Should().BeEquivalentTo(new { Executed = false, Crashed = true });
}
