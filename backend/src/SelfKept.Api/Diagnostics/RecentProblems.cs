using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace SelfKept.Api.Diagnostics;

/// <summary>One warning or error, as the admin sees it under You, Server.</summary>
public sealed record ProblemEntry(DateTime At, string Level, string Source, string Message, string? Detail);

/// <summary>
/// The last warnings and errors the server logged, kept in memory for the admin's "Recent problems"
/// list, the way Jellyfin and Home Assistant show their logs in the app. Nothing is written to disk
/// or sent anywhere; the full log is still <c>docker compose logs app</c>. A restart starts it empty.
/// </summary>
public sealed class RecentProblems : ILogEventSink
{
  public const int Capacity = 200;
  private const int DetailLimit = 4000;

  /// <summary>Log property a caller sets to attach detail that is not an exception (a phone's stack).</summary>
  public const string DetailProperty = "Detail";

  private readonly ConcurrentQueue<ProblemEntry> _entries = new();

  public void Emit(LogEvent logEvent)
  {
    if (logEvent.Level < LogEventLevel.Warning) return;
    var source = logEvent.Properties.TryGetValue("SourceContext", out var context)
      ? context.ToString().Trim('"').Split('.').Last()
      : "Server";
    var detail = logEvent.Exception?.ToString()
      ?? (logEvent.Properties.TryGetValue(DetailProperty, out var value) ? value.ToString().Trim('"') : null);
    Add(new ProblemEntry(
      logEvent.Timestamp.UtcDateTime,
      logEvent.Level == LogEventLevel.Warning ? "warning" : "error",
      source,
      logEvent.RenderMessage(),
      detail is { Length: > DetailLimit } ? detail[..DetailLimit] : detail));
  }

  public void Add(ProblemEntry entry)
  {
    _entries.Enqueue(entry);
    while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
  }

  /// <summary>Newest first.</summary>
  public IReadOnlyList<ProblemEntry> Snapshot() => _entries.Reverse().ToList();
}
