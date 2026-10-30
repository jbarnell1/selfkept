using SelfKept.Core.DTOs;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret;

namespace SelfKept.Infrastructure.Mcp.Tools;

// The read tools over wearable data, check-ins and insights (docs/connect-design.md, "Tools").
// Every tool calls the same services the app uses, so the share grant is checked in the same
// repository code; a null from a service means "not shared" or "no such thing", never an empty
// answer pretending to be data.

internal static class Window
{
  public const int MaxDays = 90;

  /// <summary>Resolves from and to (inclusive), defaulting to the last <paramref name="defaultDays"/> days and capping the span.</summary>
  public static (DateOnly From, DateOnly To) Resolve(McpArguments args, DateOnly today, int defaultDays)
  {
    var to = args.Date("to", today) ?? today;
    var from = args.Date("from", today) ?? to.AddDays(-(defaultDays - 1));
    if (from > to)
      throw new McpToolException("from must not be after to.");
    if (to.DayNumber - from.DayNumber + 1 > MaxDays)
      throw new McpToolException($"Ask for at most {MaxDays} days at a time.");
    return (from, to);
  }

  public static readonly McpParameter From = new("string", "First day, YYYY-MM-DD (inclusive). Defaults to a window ending on to.");
  public static readonly McpParameter To = new("string", "Last day, YYYY-MM-DD (inclusive). Defaults to today.");
}

internal static class NotShared
{
  public static McpToolException For(McpTarget target, string what) =>
    target.IsSelf
      ? new McpToolException($"No {what} found.")
      : new McpToolException($"{target.DisplayName} has not shared {what} with you.");
}

public sealed class GetTodayTool(ITodayService today) : IMcpTool
{
  public string Name => "get_today";
  public string Description =>
    "Today's summary: one plain sentence about last night's sleep and recovery, where sleep, HRV and resting heart rate sit against the person's own usual range (below, inside, above), up to two insight cards, and the latest check-in. Start here for any question about how someone is doing.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => true;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>();

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var dto = await today.GetAsync(context.Caller.PersonId, context.Target.PersonId, ct) ?? throw NotShared.For(context.Target, "insights");
    return new McpToolOutput(new
    {
      person = context.Target.DisplayName,
      day = dto.Day,
      sentence = dto.Sentence,
      sleepMinutes = dto.SleepMinutes,
      readiness = dto.Readiness,
      learning = dto.Learning,
      cards = dto.Cards.Select(c => new { c.Kind, c.Severity, c.Day, c.Text, c.Metrics }),
      lastCheckin = dto.LastCheckin == null ? null : new { dto.LastCheckin.At, mood = dto.LastCheckin.Mood, moodScale = McpFormat.MoodScale, note = dto.LastCheckin.Note },
    }, 1 + dto.Cards.Count);
  }
}

public sealed class ListMetricsTool(IDataService data, IMetricCatalogue catalogue) : IMcpTool
{
  public string Name => "list_metrics";
  public string Description =>
    "Which metrics have data for the person: the metric key to use with other tools, its display name, unit, share category, how many readings, the first and last reading time and the latest value. Lab markers start with lab. and are better read with get_lab_markers.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => true;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>();

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var overview = await data.GetOverviewAsync(context.Caller.PersonId, context.Target.PersonId, ct) ?? throw NotShared.For(context.Target, "any data");
    var rows = overview.Metrics
      .Select(m =>
      {
        var metric = catalogue.Find(m.Metric);
        return new
        {
          metric = m.Metric,
          name = MetricNames.For(m.Metric),
          unit = m.Unit,
          category = metric == null ? null : McpFormat.CategoryWord(metric.Category),
          readings = m.Count,
          firstAt = m.FirstAt,
          lastAt = m.LastAt,
          latest = McpFormat.Round(m.LastValue),
        };
      })
      .OrderBy(r => r.metric, StringComparer.Ordinal)
      .ToList();
    return new McpToolOutput(new { person = context.Target.DisplayName, metrics = rows }, rows.Count);
  }
}

public sealed class GetDailySummaryTool(IDataService data, IMetricCatalogue catalogue) : IMcpTool
{
  public string Name => "get_daily_summary";
  public string Description =>
    "One value per day for one metric over a window of up to 90 days: totals for steps, distance, energy and exercise minutes; the day's average with its low and high for heart rate, HRV, SpO2, weight and mood; minutes asleep for sleep_stage (attributed to the morning the sleep ended). Use list_metrics for the keys.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => true;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>
  {
    ["metric"] = new("string", "Metric key, for example steps, sleep_stage, hrv_sdnn_ms, resting_hr_bpm, weight_kg, checkin_mood.", Required: true),
    ["from"] = Window.From,
    ["to"] = Window.To,
  };

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var key = McpFormat.NormaliseMetricKey(arguments.RequiredString("metric"));
    var metric = catalogue.Find(key) ?? throw new McpToolException($"Unknown metric {key}. Use list_metrics for the keys.");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var (from, to) = Window.Resolve(arguments, today, 28);
    var series = await data.GetDailyAsync(context.Caller.PersonId, context.Target.PersonId, metric.Key, from, to, ct)
      ?? throw NotShared.For(context.Target, McpFormat.CategoryWord(metric.Category) + " data");
    var days = series.Days.Select(d => new { d.Day, value = McpFormat.Round(d.Value), min = McpFormat.Round(d.Min), max = McpFormat.Round(d.Max) }).ToList();
    return new McpToolOutput(new
    {
      person = context.Target.DisplayName,
      metric = metric.Key,
      name = MetricNames.For(metric.Key),
      unit = series.Unit,
      from,
      to,
      days,
      note = days.Count == 0 ? "No days with data in this window." : null,
    }, days.Count);
  }
}

public sealed class GetTrendTool(ITrendService trends, IMetricCatalogue catalogue) : IMcpTool
{
  public string Name => "get_trend";
  public string Description =>
    "A metric's last 28 or 90 days with the person's own usual range: the 28-day median and the 25th to 75th percentile band (p10 and p90 too). Compare with the person's own band first and with population ranges second.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => true;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>
  {
    ["metric"] = new("string", "Metric key, for example hrv_sdnn_ms, resting_hr_bpm, sleep_stage, steps.", Required: true),
    ["days"] = new("integer", "28 or 90. Defaults to 28.", Enum: ["28", "90"]),
  };

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var key = McpFormat.NormaliseMetricKey(arguments.RequiredString("metric"));
    var metric = catalogue.Find(key) ?? throw new McpToolException($"Unknown metric {key}. Use list_metrics for the keys.");
    var days = arguments.Int("days") ?? 28;
    if (days is not (28 or 90))
      throw new McpToolException("days must be 28 or 90.");
    var series = await trends.GetSeriesAsync(context.Caller.PersonId, context.Target.PersonId, metric.Key, days, ct)
      ?? throw NotShared.For(context.Target, McpFormat.CategoryWord(metric.Category) + " data");
    return new McpToolOutput(new
    {
      person = context.Target.DisplayName,
      metric = metric.Key,
      name = MetricNames.For(metric.Key),
      unit = series.Unit,
      band = series.Band,
      baselineDays = series.BaselineDays,
      points = series.Points.Select(p => new { p.Day, value = McpFormat.Round(p.Value) }),
      note = series.Band == null ? "Still learning this person's normal for this metric: fewer than 7 days of data." : null,
    }, series.Points.Count);
  }
}

public sealed class ComparePeriodsTool(IDataService data, IMetricCatalogue catalogue) : IMcpTool
{
  public string Name => "compare_periods";
  public string Description =>
    "Compares one metric across two windows (each up to 90 days): the median, lowest and highest daily value and how many days had data in each, and the change in the median. Use it for questions like 'since my last blood test' (get the panel date from get_lab_markers) or 'this month versus last month'.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => true;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>
  {
    ["metric"] = new("string", "Metric key, for example sleep_stage, hrv_sdnn_ms, resting_hr_bpm, steps.", Required: true),
    ["from_a"] = new("string", "First window start, YYYY-MM-DD.", Required: true),
    ["to_a"] = new("string", "First window end, YYYY-MM-DD.", Required: true),
    ["from_b"] = new("string", "Second window start, YYYY-MM-DD.", Required: true),
    ["to_b"] = new("string", "Second window end, YYYY-MM-DD.", Required: true),
  };

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var key = McpFormat.NormaliseMetricKey(arguments.RequiredString("metric"));
    var metric = catalogue.Find(key) ?? throw new McpToolException($"Unknown metric {key}. Use list_metrics for the keys.");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var a = await WindowAsync(context, metric.Key, arguments.RequiredDate("from_a", today), arguments.RequiredDate("to_a", today), ct);
    var b = await WindowAsync(context, metric.Key, arguments.RequiredDate("from_b", today), arguments.RequiredDate("to_b", today), ct);
    if (a == null || b == null)
      throw NotShared.For(context.Target, McpFormat.CategoryWord(metric.Category) + " data");

    double? change = a.Median is { } ma && b.Median is { } mb ? McpFormat.Round(mb - ma) : null;
    double? percent = a.Median is { } pa && b.Median is { } pb && pa != 0 ? Math.Round((pb - pa) / Math.Abs(pa) * 100, 0) : null;
    return new McpToolOutput(new
    {
      person = context.Target.DisplayName,
      metric = metric.Key,
      name = MetricNames.For(metric.Key),
      unit = a.Unit,
      a = new { a.From, a.To, median = McpFormat.Round(a.Median), low = McpFormat.Round(a.Low), high = McpFormat.Round(a.High), daysWithData = a.Days },
      b = new { b.From, b.To, median = McpFormat.Round(b.Median), low = McpFormat.Round(b.Low), high = McpFormat.Round(b.High), daysWithData = b.Days },
      medianChange = change,
      medianChangePercent = percent,
    }, a.Days + b.Days);
  }

  private sealed record WindowSummary(DateOnly From, DateOnly To, string Unit, double? Median, double? Low, double? High, int Days);

  private async Task<WindowSummary?> WindowAsync(McpToolContext context, string key, DateOnly from, DateOnly to, CancellationToken ct)
  {
    if (from > to)
      throw new McpToolException("Each window's start must not be after its end.");
    if (to.DayNumber - from.DayNumber + 1 > Window.MaxDays)
      throw new McpToolException($"Each window may be at most {Window.MaxDays} days.");
    var series = await data.GetDailyAsync(context.Caller.PersonId, context.Target.PersonId, key, from, to, ct);
    if (series == null)
      return null;
    var values = series.Days.Select(d => d.Value).OrderBy(v => v).ToList();
    if (values.Count == 0)
      return new WindowSummary(from, to, series.Unit, null, null, null, 0);
    var mid = values.Count / 2;
    var median = values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    return new WindowSummary(from, to, series.Unit, median, values[0], values[^1], values.Count);
  }
}

public sealed class GetInsightsTool(IInsightService insights) : IMcpTool
{
  public string Name => "get_insights";
  public string Description =>
    "Insight cards the app has raised: what was notable on which day, at what severity (notice, watch, or clinician meaning worth raising with a clinician), the metrics involved and the person's reaction if any. Newest first, up to 90 days.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => true;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>
  {
    ["from"] = Window.From,
    ["to"] = Window.To,
    ["severity"] = new("string", "Only this severity.", Enum: ["notice", "watch", "clinician"]),
  };

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var (from, to) = Window.Resolve(arguments, today, 30);
    var severity = arguments.String("severity")?.ToLowerInvariant();
    if (severity is not (null or "notice" or "watch" or "clinician"))
      throw new McpToolException("severity must be notice, watch or clinician.");
    var list = await insights.ListAsync(context.Caller.PersonId, context.Target.PersonId, from, to, includeDismissed: true, ct)
      ?? throw NotShared.For(context.Target, "insights");
    var rows = list
      .Where(i => severity == null || i.Severity == severity)
      .Select(i => new { i.Day, i.Kind, i.Severity, i.Text, i.Metrics, dismissed = i.DismissedAt != null, i.Reaction })
      .ToList();
    return new McpToolOutput(new { person = context.Target.DisplayName, from, to, insights = rows }, rows.Count);
  }
}

public sealed class GetCheckinsTool(ICheckinService checkins) : IMcpTool
{
  public string Name => "get_checkins";
  public string Description =>
    $"The person's mood check-ins ({McpFormat.MoodScale}) with their free-text notes, newest first, up to 90 days. query filters notes by a word or phrase, which answers questions like 'when did I last mention headaches'.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => true;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>
  {
    ["from"] = Window.From,
    ["to"] = Window.To,
    ["query"] = new("string", "Only check-ins whose note contains this text (case-insensitive)."),
  };

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var (from, to) = Window.Resolve(arguments, today, 30);
    var query = arguments.String("query");
    var list = await checkins.ListAsync(
      context.Caller.PersonId,
      context.Target.PersonId,
      from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
      to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
      ct) ?? throw NotShared.For(context.Target, "check-ins");
    var rows = list
      .Where(c => query == null || (c.Note?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
      .Select(c => new { c.At, c.Mood, c.Note, c.Tags })
      .ToList();
    return new McpToolOutput(new { person = context.Target.DisplayName, moodScale = McpFormat.MoodScale, from, to, checkins = rows }, rows.Count);
  }
}

public sealed class GetSourcesTool(ISourceService sources) : IMcpTool
{
  public string Name => "get_sources";
  public string Description =>
    "The caller's own data sources (Apple Health on an iPhone, Health Connect on an Android phone, an Oura ring, lab results, check-ins) with when each last synced. Use it to explain a gap in the data.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => false;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>();

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var list = await sources.ListAsync(context.Caller.PersonId, ct);
    var rows = list.Select(s => new { s.Kind, s.DeviceName, s.Platform, s.Enabled, s.LastSyncAt, s.LastSampleAt, readings = s.SampleCount }).ToList();
    return new McpToolOutput(new { sources = rows }, rows.Count);
  }
}
