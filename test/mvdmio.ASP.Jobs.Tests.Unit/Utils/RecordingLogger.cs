using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Logger that records every entry it receives, so tests can assert which lines were logged and how often.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
   public ConcurrentQueue<RecordedLogEntry> Entries { get; } = new();

   public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

   public bool IsEnabled(LogLevel logLevel) => true;

   public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
   {
      Entries.Enqueue(new RecordedLogEntry(logLevel, formatter(state, exception), exception));
   }
}

internal sealed record RecordedLogEntry(LogLevel Level, string Message, Exception? Exception);
