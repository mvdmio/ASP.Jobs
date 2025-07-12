using System.Diagnostics;
using AwesomeAssertions;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Internals.Storage;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public sealed class JobRunnerServiceTests
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
      var executionTime = await RunJobs();
      
      // Assert
      _jobStorage.ScheduledJobs.Should().HaveCount(0);
      _jobStorage.InProgressJobs.Should().HaveCount(0);
      
      jobs.ForEach(AssertExecuted);
      
      // Try to assert parallelism. Approximately, the execution time should be less than the sum of all delays.
      var totalDelay = jobs.Sum(job => job.Delay?.TotalMilliseconds ?? 0);
      executionTime.Should().BeLessThan(TimeSpan.FromMilliseconds(totalDelay));
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

   private async Task<TimeSpan> RunJobs()
   {
      var startTime = Stopwatch.GetTimestamp();
      
      await _runner.StartAsync(CancellationToken);
      await _jobStorage.WaitForAllJobsFinishedAsync(CancellationToken);
      await _runner.StopAsync(CancellationToken);
      
      return Stopwatch.GetElapsedTime(startTime);
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