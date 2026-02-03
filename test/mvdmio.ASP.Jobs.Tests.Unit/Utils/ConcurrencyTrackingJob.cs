namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// A test job that tracks concurrent execution for testing purposes.
/// Uses a shared tracker to monitor how many jobs are running simultaneously.
/// </summary>
public class ConcurrencyTrackingJob : Job<ConcurrencyTrackingJob.Parameters>
{
   public override async Task<Parameters> ExecuteAsync(Parameters properties, CancellationToken cancellationToken)
   {
      // Record that this job started
      properties.Tracker.RecordJobStarted();
      
      try
      {
         // Simulate work with configurable delay
         await Task.Delay(properties.ExecutionDuration, cancellationToken);
         return properties;
      }
      finally
      {
         // Record that this job finished
         properties.Tracker.RecordJobFinished();
      }
   }

   public override Task OnJobExecutedAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters.Executed = true;
      return Task.CompletedTask;
   }

   public override Task OnJobFailedAsync(Parameters parameters, Exception exception, CancellationToken cancellationToken)
   {
      parameters.Crashed = true;
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public required ConcurrencyTracker Tracker { get; init; }
      public TimeSpan ExecutionDuration { get; init; } = TimeSpan.FromMilliseconds(100);
      public bool Executed { get; set; }
      public bool Crashed { get; set; }
   }
}

/// <summary>
/// Thread-safe tracker for monitoring concurrent job execution.
/// </summary>
public class ConcurrencyTracker
{
   private int _currentConcurrency;
   private int _maxObservedConcurrency;
   private int _totalJobsStarted;
   private int _totalJobsFinished;
   private readonly object _lock = new();
   private readonly List<(DateTime Time, int Concurrency, string Event)> _events = [];

   /// <summary>
   /// Gets the maximum number of jobs that were running concurrently at any point.
   /// </summary>
   public int MaxObservedConcurrency
   {
      get
      {
         lock (_lock)
         {
            return _maxObservedConcurrency;
         }
      }
   }

   /// <summary>
   /// Gets the current number of jobs running.
   /// </summary>
   public int CurrentConcurrency
   {
      get
      {
         lock (_lock)
         {
            return _currentConcurrency;
         }
      }
   }

   /// <summary>
   /// Gets the total number of jobs that started.
   /// </summary>
   public int TotalJobsStarted
   {
      get
      {
         lock (_lock)
         {
            return _totalJobsStarted;
         }
      }
   }

   /// <summary>
   /// Gets the total number of jobs that finished.
   /// </summary>
   public int TotalJobsFinished
   {
      get
      {
         lock (_lock)
         {
            return _totalJobsFinished;
         }
      }
   }

   /// <summary>
   /// Gets a copy of all recorded events for debugging.
   /// </summary>
   public IReadOnlyList<(DateTime Time, int Concurrency, string Event)> Events
   {
      get
      {
         lock (_lock)
         {
            return _events.ToList();
         }
      }
   }

   /// <summary>
   /// Records that a job has started executing.
   /// </summary>
   public void RecordJobStarted()
   {
      lock (_lock)
      {
         _totalJobsStarted++;
         _currentConcurrency++;
         
         if (_currentConcurrency > _maxObservedConcurrency)
            _maxObservedConcurrency = _currentConcurrency;
         
         _events.Add((DateTime.UtcNow, _currentConcurrency, "Started"));
      }
   }

   /// <summary>
   /// Records that a job has finished executing.
   /// </summary>
   public void RecordJobFinished()
   {
      lock (_lock)
      {
         _totalJobsFinished++;
         _currentConcurrency--;
         _events.Add((DateTime.UtcNow, _currentConcurrency, "Finished"));
      }
   }

   /// <summary>
   /// Resets all tracking data.
   /// </summary>
   public void Reset()
   {
      lock (_lock)
      {
         _currentConcurrency = 0;
         _maxObservedConcurrency = 0;
         _totalJobsStarted = 0;
         _totalJobsFinished = 0;
         _events.Clear();
      }
   }
}
