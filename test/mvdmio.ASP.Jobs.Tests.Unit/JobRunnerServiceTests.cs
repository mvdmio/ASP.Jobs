using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Internals.Storage.Interfaces;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public sealed class JobRunnerServiceTests
{
   private readonly InMemoryJobStorage _jobStorage;
   private readonly Random _random;
   private readonly JobRunnerService _runner;
   private readonly JobScheduler _scheduler;
   private readonly TestClock _clock;

   private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public JobRunnerServiceTests()
   {
      _random = new Random(1);

      var services = new JobTestServices().Services;
      
      _clock = new TestClock();
      
      _jobStorage = new InMemoryJobStorage();
      var configuration = new JobRunnerOptions {
         MaxConcurrentJobs = 10
      };

      services.AddSingleton<IJobStorage>(_jobStorage);
      
      var loggerFactory = new NullLoggerFactory();
      
      _scheduler = new JobScheduler(services.BuildServiceProvider(), _jobStorage, _clock);
      _runner = new JobRunnerService(services.BuildServiceProvider(), Options.Create(configuration), loggerFactory.CreateLogger<JobRunnerService>());
   }

   [Fact]
   public async Task RunSingleJob()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();

      // Act
      await RunJobs();
      
      // Assert
      _jobStorage.ScheduledJobs.Should().HaveCount(0);
      _jobStorage.InProgressJobs.Should().HaveCount(0);
      
      AssertExecuted(job1);
   }

   [Fact]
   public async Task RunMultipleJobs()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();
      var job2 = await ScheduleTestJobAsync();

      // Act
      await RunJobs();

      // Assert
      _jobStorage.ScheduledJobs.Should().HaveCount(0);
      _jobStorage.InProgressJobs.Should().HaveCount(0);
      
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
      await RunJobs();

      // Assert
      _jobStorage.ScheduledJobs.Should().HaveCount(0);
      _jobStorage.InProgressJobs.Should().HaveCount(0);
      
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
      await RunJobs();
      
      // Assert
      _jobStorage.ScheduledJobs.Should().HaveCount(0);
      _jobStorage.InProgressJobs.Should().HaveCount(0);
      
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

   [Fact]
   public async Task HandleManyJobsInParallel()
   {
      // Arrange
      var jobs = new List<TestJob.Parameters>();
      for (var i = 0; i < 1000; i++)
      {
         jobs.Add(await ScheduleTestJobAsync(TimeSpan.FromMilliseconds(1)));
      }

      // Act
      await RunJobs();
      
      // Assert
      _jobStorage.ScheduledJobs.Should().HaveCount(0);
      _jobStorage.InProgressJobs.Should().HaveCount(0);
      
      jobs.ForEach(AssertExecuted);
   }

   // TODO: Test CRON jobs. Requires improved test harness with more refined control over time and timing.
   // [Fact]
   // public async Task HandleCronJobs()
   // {
   //    // Arrange
   //    var job = new TestJob.Parameters {
   //       Delay = TimeSpan.FromMilliseconds(10)
   //    };
   //    
   //    await _scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.EverySecond, job, null, cancellationToken: CancellationToken);
   //    
   //    // Act
   //    _ = await RunJobs();
   //    
   //    // Assert
   //    AssertExecuted(job);
   //    
   //    _jobStorage.ScheduledJobs.Should().HaveCount(1);
   //    _jobStorage.InProgressJobs.Should().HaveCount(0);
   //    _jobStorage.ScheduledJobs.First().CronExpression.Should().Be(CronExpression.EverySecond);
   // }
   
   private async Task<TestJob.Parameters> ScheduleTestJobAsync(TimeSpan? delay = null)
   {
      delay ??= TimeSpan.FromMilliseconds(_random.Next(1, 10));

      var parameters = new TestJob.Parameters {
         Delay = delay.Value
      };

      await _scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);
      return parameters;
   }

   private async Task RunJobs()
   {
      await _runner.StartAsync(CancellationToken);
      await WaitForAllJobsToFinishAsync();
      await _runner.StopAsync(CancellationToken);
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

   private async Task WaitForAllJobsToFinishAsync()
   {
      do
      {
         var scheduledJobs = await _jobStorage.GetScheduledJobsAsync(CancellationToken);
         var inProgressJobs = await _jobStorage.GetInProgressJobsAsync(CancellationToken);
         
         if (scheduledJobs.Any() || inProgressJobs.Any())
            await Task.Delay(10, CancellationToken);
         else
            break;
      }
      while (true);
   }
}