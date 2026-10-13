using System.Collections.Concurrent;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Installs a recording sink on Serilog's static <see cref="Log.Logger"/> for as long as it lives, and restores the previous
/// logger on dispose. The static logger is process-wide, so tests that use this belong in the <see cref="Name"/> xunit collection
/// and match events by their own unique marked id.
/// </summary>
internal sealed class SerilogCapture : IDisposable
{
   /// <summary>The xunit collection name of every test class that installs a <see cref="SerilogCapture"/>.</summary>
   public const string Name = "Serilog static logger";

   private readonly ILogger _previous;
   private readonly Sink _sink = new();

   public IReadOnlyCollection<LogEvent> Events => _sink.Events;

   public SerilogCapture()
   {
      _previous = Log.Logger;
      Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
   }

   public void Dispose()
   {
      Log.Logger = _previous;
   }

   /// <summary>The rendered message and every property value of <paramref name="logEvent"/>, as one string to search.</summary>
   public static string Text(LogEvent logEvent)
   {
      return logEvent.RenderMessage() + " " + string.Join(" ", logEvent.Properties.Select(x => $"{x.Key}={x.Value}"));
   }

   private sealed class Sink : ILogEventSink
   {
      private readonly ConcurrentQueue<LogEvent> _events = new();

      public IReadOnlyCollection<LogEvent> Events => _events;

      public void Emit(LogEvent logEvent)
      {
         _events.Enqueue(logEvent);
      }
   }
}
