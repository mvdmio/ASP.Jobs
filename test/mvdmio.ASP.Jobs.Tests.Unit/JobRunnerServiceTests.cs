using System.Diagnostics;
using AwesomeAssertions;
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
      await RunJobs(async () => {
            await job1.Complete();
         }
      );
      
      // Assert
      AssertExecuted(job1);
   }

   [Fact]
   public async Task RunMultipleJobs()
   {
      // Arrange
      var job1 = await ScheduleTestJobAsync();
      var job2 = await ScheduleTestJobAsync();

      // Act
      await RunJobs(async () => {
            await job1.Complete();
            await job2.Complete();
         }
      );

      // Assert
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
      await RunJobs(async () => {
            await job1.Crash(new Exception("Crashed job test"));
            await job2.Complete();
         }
      );

      // Assert
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
      await RunJobs(async () => {
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
         }
      );
      
      // Assert
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
         jobs.Add(await ScheduleTestJobAsync(1));
      }

      // Act
      var executionTime = await RunJobs(async () => {
         var tasks = jobs.Select(job => job.Complete()).ToArray();
         await Task.WhenAll(tasks);
      });
      
      // Assert
      jobs.ForEach(AssertExecuted);
      
      var totalDelay = jobs.Sum(job => job.Delay);
      executionTime.Should().BeLessThan(TimeSpan.FromMilliseconds(totalDelay / 4), "all jobs should be executed in parallel within a quarter of the execution time of all jobs");
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

   private async Task<TimeSpan> RunJobs(Func<Task> runFunc)
   {
      var startTime = Stopwatch.GetTimestamp();
      
      await _runner.StartAsync(CancellationToken);
      await runFunc.Invoke();
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