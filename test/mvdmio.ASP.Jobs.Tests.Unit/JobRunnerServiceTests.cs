using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public sealed class JobRunnerServiceTests : IAsyncLifetime
{
   private readonly InMemoryJobStorage _jobStorage;
   private readonly Random _random;
   private readonly JobRunnerService _runner;
   private readonly JobScheduler _scheduler;

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public JobRunnerServiceTests()
   {
      _random = new Random(1);

      var services = new ServiceCollection();
      services.RegisterJob<TestJob>();

      _jobStorage = new InMemoryJobStorage();
      var configuration = new JobConfiguration();

      _scheduler = new JobScheduler(services.BuildServiceProvider(), _jobStorage);
      _runner = new JobRunnerService(services.BuildServiceProvider(), _jobStorage, Options.Create(configuration));
   }

   public async ValueTask InitializeAsync()
   {
      await _runner.StartAsync(CancellationToken);
   }

   public async ValueTask DisposeAsync()
   {
      await _runner.StopAsync(CancellationToken);
   }

   [Fact]
   public async Task RunSingleJob()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();

      // Act
      await job1.Complete();

      // Assert
      await _jobStorage.WaitForAllJobsFinishedAsync(CancellationToken);

      AssertExecuted(job1);
   }

   [Fact]
   public async Task RunMultipleJobs()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();
      var job2 = await ScheduleTestJobAsync();

      // Act
      await job1.Complete();
      await job2.Complete();

      // Assert
      await _jobStorage.WaitForAllJobsFinishedAsync(CancellationToken);

      AssertExecuted(job1);
      AssertExecuted(job2);
   }

   [Fact]
   public async Task HandleCrash()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();
      var job2 = await ScheduleTestJobAsync();

      // Act
      await job1.Crash(new Exception("Crashed job test"));
      await job2.Complete();

      // Assert
      await _jobStorage.WaitForAllJobsFinishedAsync(CancellationToken);

      AssertCrashed(job1);
      AssertExecuted(job2);
   }

   [Fact]
   public async Task HandleManyJobs()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();
      var job2 = await ScheduleTestJobAsync();
      var job3 = await ScheduleTestJobAsync();
      var job4 = await ScheduleTestJobAsync();
      var job5 = await ScheduleTestJobAsync();
      var job6 = await ScheduleTestJobAsync();
      var job7 = await ScheduleTestJobAsync();
      var job8 = await ScheduleTestJobAsync();
      var job9 = await ScheduleTestJobAsync();
      var job10 = await ScheduleTestJobAsync();

      // Act
      await job1.Complete();
      await job2.Complete();
      await job3.Complete();
      await job4.Complete();
      await job5.Complete();
      await job6.Complete();
      await job7.Complete();
      await job8.Complete();
      await job9.Complete();
      await job10.Complete();

      // Assert
      await _jobStorage.WaitForAllJobsFinishedAsync(CancellationToken);

      AssertExecuted(job1);
      AssertExecuted(job2);
      AssertExecuted(job3);
      AssertExecuted(job4);
      AssertExecuted(job5);
      AssertExecuted(job6);
      AssertExecuted(job7);
      AssertExecuted(job8);
      AssertExecuted(job9);
      AssertExecuted(job10);
   }

   private async Task<TestJob.Parameters> ScheduleTestJobAsync(int? delay = null)
   {
      delay ??= _random.Next(1, 10);

      var parameters = new TestJob.Parameters {
         Delay = delay.Value
      };

      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);
      return parameters;
   }

   private static void AssertExecuted(TestJob.Parameters job)
   {
      job.Should()
         .BeEquivalentTo(
            new {
               Executed = true,
               Crashed = false
            }
         );
   }

   private static void AssertCrashed(TestJob.Parameters job)
   {
      job.Should()
         .BeEquivalentTo(
            new {
               Executed = false,
               Crashed = true
            }
         );
   }
}