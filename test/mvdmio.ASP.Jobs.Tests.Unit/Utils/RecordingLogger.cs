using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Logger that records every entry it receives so tests can assert which lines were logged and how often.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
   private readonly LogLevel _minimumLevel;

   public RecordingLogger(LogLevel minimumLevel = LogLevel.Trace)
   {
      _minimumLevel = minimumLevel;
   }

   public ConcurrentQueue<RecordedLogEntry> Entries { get; } = new();

   public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

   public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

   public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
   {
      if (!IsEnabled(logLevel))
         return;

      var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
         ? pairs.ToDictionary(x => x.Key, x => x.Value)
         : [];

      Entries.Enqueue(new RecordedLogEntry(logLevel, formatter(state, exception), exception, properties));
   }
}

/// <param name="Properties">The named values of the message template, such as <c>Parameters</c> for <c>{@Parameters}</c>.</param>
internal sealed record RecordedLogEntry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Properties);
