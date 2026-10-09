using System.Collections.Concurrent;
using System.Diagnostics;
using AwesomeAssertions;
using Cronos;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

/// <summary>
/// Tests for the job span on the <c>mvdmio.ASP.Jobs</c> activity source, as an <see cref="ActivityListener"/> sees it.
/// A listener hears the whole process, so each test matches only the spans whose <c>job.parameters</c> tag holds its
/// own <see cref="TestJob.Parameters"/> instance.
/// </summary>
public sealed class JobRunnerTracingTests
{
   private static readonly string _testJobType = typeof(TestJob).AssemblyQualifiedName!;

   private readonly JobRunnerHarness _harness = new();
   private readonly CancellationTokenSource _cts;

   private CancellationToken CancellationToken => _cts.Token;

   public JobRunnerTracingTests()
   {
      _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      _cts.CancelAfter(TimeSpan.FromSeconds(10));
   }

   [Fact]
   public async Task SamplingCallback_SeesNameAndJobTags_ForNamedGroupedAsapJob()
   {
      // Arrange
      var parameters = new TestJob.Parameters();
      var offered = new ConcurrentQueue<OfferedSpan>();
      using var listener = ListenToOwnSpans(parameters, offered, ActivitySamplingResult.AllDataAndRecorded);

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(
         parameters,
         new JobScheduleOptions { JobName = "tracing-job", Group = "tracing-group" },
         CancellationToken
      );

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var span = offered.Should().ContainSingle().Subject;
      span.Name.Should().Be("Job: TestJob");
      span.Kind.Should().Be(ActivityKind.Internal);
      span.Tags.Should().Contain("job.type", _testJobType);
      span.Tags.Should().Contain("job.name", "tracing-job");
      span.Tags.Should().Contain("job.group", "tracing-group");
      span.Tags["job.parameters"].Should().BeSameAs(parameters);
      span.Tags.Should().Contain("job.attempt", 0);
      span.Tags.Should().NotContainKey("job.cron");
   }

   [Fact]
   public async Task SamplingCallback_SeesCronTag_ForCronJob()
   {
      // Arrange
      var parameters = new TestJob.Parameters();
      var offered = new ConcurrentQueue<OfferedSpan>();
      using var listener = ListenToOwnSpans(parameters, offered, ActivitySamplingResult.AllDataAndRecorded);

      await _harness.Scheduler.PerformCronAsync<TestJob, TestJob.Parameters>(CronExpression.EverySecond, parameters, runImmediately: true, CancellationToken);

      // Act - drive the runner directly: a CRON chain always leaves its next occurrence scheduled, so a drain never ends.
      await _harness.Runner.StartAsync(CancellationToken);
      await JobRunnerHarness.WaitUntilAsync(() => parameters.Executed, CancellationToken);
      await _harness.Runner.StopAsync(CancellationToken);

      // Assert
      var span = offered.First();
      span.Name.Should().Be("Job: TestJob");
      span.Tags.Should().Contain("job.type", _testJobType);
      span.Tags.Should().Contain("job.cron", CronExpression.EverySecond.ToString());
   }

   [Fact]
   public async Task DroppedSpan_CarriesNameAndJobType_InsideJobBody()
   {
      // Arrange
      var parameters = new TestJob.Parameters();
      using var listener = ListenToOwnSpans(parameters, new ConcurrentQueue<OfferedSpan>(), ActivitySamplingResult.PropagationData);

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var span = parameters.ExecuteActivity;
      span.Should().NotBeNull("a propagation-only span still exists while the job runs");
      span.Recorded.Should().BeFalse();
      span.IsAllDataRequested.Should().BeFalse();
      span.DisplayName.Should().Be("Job: TestJob");
      span.GetTagItem("job.type").Should().Be(_testJobType);
   }

   [Fact]
   public async Task ExportedSpan_ForSuccessfulJob_KeepsNameTagsStatusAndEvents()
   {
      // Arrange
      var parameters = new TestJob.Parameters();
      var stopped = new ConcurrentQueue<Activity>();
      using var listener = RecordOwnStoppedSpans(stopped);

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, new JobScheduleOptions { JobName = "exported-job" }, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var span = stopped.Should().ContainSingle(x => ReferenceEquals(x.GetTagItem("job.parameters"), parameters)).Subject;
      span.DisplayName.Should().Be("Job: TestJob");
      span.OperationName.Should().Be("Job: TestJob");
      span.Kind.Should().Be(ActivityKind.Internal);
      span.GetTagItem("job.type").Should().Be(_testJobType);
      span.GetTagItem("job.name").Should().Be("exported-job");
      span.GetTagItem("job.attempt").Should().Be(0);
      span.GetTagItem("job.group").Should().BeNull("a job with no group has no job.group tag");
      span.GetTagItem("job.cron").Should().BeNull("a job with no CRON expression has no job.cron tag");
      span.TagObjects.Select(x => x.Key).Should().BeEquivalentTo("job.type", "job.name", "job.parameters", "job.attempt");
      span.Status.Should().Be(ActivityStatusCode.Ok);
      span.Events.Select(x => x.Name).Should().Equal("Job Started", "Job Completed");
   }

   [Fact]
   public async Task ExportedSpan_ForFailingJob_KeepsNameTagsStatusAndEvents()
   {
      // Arrange
      var parameters = new TestJob.Parameters { ThrowOnExecute = new InvalidOperationException("tracing failure") };
      var stopped = new ConcurrentQueue<Activity>();
      using var listener = RecordOwnStoppedSpans(stopped);

      await _harness.Scheduler.PerformAsapAsync<TestJob, TestJob.Parameters>(parameters, CancellationToken);

      // Act
      await _harness.RunAndDrainAsync(CancellationToken);

      // Assert
      var span = stopped.Should().ContainSingle(x => ReferenceEquals(x.GetTagItem("job.parameters"), parameters)).Subject;
      span.DisplayName.Should().Be("Job: TestJob");
      span.GetTagItem("job.type").Should().Be(_testJobType);
      span.GetTagItem("job.attempt").Should().Be(0);
      span.TagObjects.Select(x => x.Key).Should().BeEquivalentTo("job.type", "job.name", "job.parameters", "job.attempt");
      span.Status.Should().Be(ActivityStatusCode.Error);
      span.StatusDescription.Should().Be("Job failed with exception");
      span.Events.Select(x => x.Name).Should().Equal("Job Started", "exception");
      span.Events.Last().Tags.Should().Contain(new KeyValuePair<string, object?>("exception.message", "tracing failure"));
   }

   private static ActivityListener ListenToOwnSpans(TestJob.Parameters parameters, ConcurrentQueue<OfferedSpan> offered, ActivitySamplingResult ownResult)
   {
      return ListenToJobSource((ref ActivityCreationOptions<ActivityContext> options) => {
         var tags = (options.Tags ?? []).ToDictionary(x => x.Key, x => x.Value);

         if (!tags.TryGetValue("job.parameters", out var tagged) || !ReferenceEquals(tagged, parameters))
            return ActivitySamplingResult.None;

         offered.Enqueue(new OfferedSpan(options.Name, options.Kind, tags));
         return ownResult;
      });
   }

   private static ActivityListener RecordOwnStoppedSpans(ConcurrentQueue<Activity> stopped)
   {
      return ListenToJobSource((ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded, stopped.Enqueue);
   }

   private static ActivityListener ListenToJobSource(SampleActivity<ActivityContext> sample, Action<Activity>? activityStopped = null)
   {
      var listener = new ActivityListener {
         ShouldListenTo = source => source.Name == JobRunnerService.ActivitySourceName,
         Sample = sample,
         ActivityStopped = activityStopped
      };

      ActivitySource.AddActivityListener(listener);
      return listener;
   }

   private sealed record OfferedSpan(string Name, ActivityKind Kind, Dictionary<string, object?> Tags);
}
