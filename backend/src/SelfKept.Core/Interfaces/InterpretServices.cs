using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;

namespace SelfKept.Core.Interfaces;

/// <summary>What one rule saw, pre-loaded once per person per day so rules stay pure.</summary>
public sealed class RuleContext
{
  public RuleContext(Person person, DateOnly day, IReadOnlyDictionary<string, Baseline> baselines, IReadOnlyDictionary<string, IReadOnlyList<DailySummary>> series, LabContext? labs = null)
  {
    Person = person;
    Day = day;
    Baselines = baselines;
    Series = series;
    Labs = labs ?? LabContext.Empty;
    // HRV: rmssd when the person has it, else sdnn (design, "whichever the person has").
    HrvMetricKey = series.TryGetValue(MetricKeys.HrvRmssdMs, out var rmssd) && rmssd.Count > 0 ? MetricKeys.HrvRmssdMs : MetricKeys.HrvSdnnMs;
  }

  public Person Person { get; }

  /// <summary>The day being interpreted (the last day of every 28-day window).</summary>
  public DateOnly Day { get; }

  /// <summary>Baselines computed for <see cref="Day"/>, by metric key.</summary>
  public IReadOnlyDictionary<string, Baseline> Baselines { get; }

  /// <summary>The last 28 days of daily summaries ending on <see cref="Day"/>, by metric key, days ascending.</summary>
  public IReadOnlyDictionary<string, IReadOnlyList<DailySummary>> Series { get; }

  public string HrvMetricKey { get; }

  /// <summary>The person's lab panels and the reference ranges for their sex. Empty when none are stored.</summary>
  public LabContext Labs { get; }

  public Baseline? BaselineFor(string metricKey) => Baselines.TryGetValue(metricKey, out var b) ? b : null;

  public IReadOnlyList<DailySummary> SeriesFor(string metricKey) => Series.TryGetValue(metricKey, out var s) ? s : [];

  /// <summary>The value on one day, or null when that day had no data.</summary>
  public double? ValueOn(string metricKey, DateOnly day)
  {
    foreach (var row in SeriesFor(metricKey))
    {
      if (row.Day == day) return row.Value;
    }
    return null;
  }

  /// <summary>Values for the last <paramref name="days"/> days ending on <see cref="Day"/>, oldest first; null where a day is missing.</summary>
  public double?[] LastValues(string metricKey, int days)
  {
    var result = new double?[days];
    for (var i = 0; i < days; i++)
    {
      result[i] = ValueOn(metricKey, Day.AddDays(-(days - 1 - i)));
    }
    return result;
  }

  /// <summary>True when every metric has at least <see cref="Baseline.MinimumDays"/> days in its window.</summary>
  public bool Established(params string[] metricKeys)
  {
    foreach (var key in metricKeys)
    {
      if (BaselineFor(key) is not { IsEstablished: true }) return false;
    }
    return true;
  }

  /// <summary>True when no baseline metric has reached <see cref="Baseline.MinimumDays"/> days yet.</summary>
  public bool StillLearning => !Baselines.Values.Any(b => b.IsEstablished);
}

/// <summary>One stored panel as the lab rules see it: when it was collected and confirmed, and its results by marker.</summary>
public sealed record LabPanelSnapshot(Guid ReportId, DateOnly CollectedOn, DateOnly ConfirmedOn, IReadOnlyDictionary<string, LabResult> Results)
{
  public LabResult? Result(string metricKey) => Results.TryGetValue(metricKey, out var r) ? r : null;
}

/// <summary>Lab panels newest first, plus the reference row that applies to this person for a marker.</summary>
public sealed class LabContext
{
  public static readonly LabContext Empty = new([], _ => null, null);

  public LabContext(IReadOnlyList<LabPanelSnapshot> panels, Func<string, LabReference?> reference, Func<string, string>? displayName)
  {
    Panels = panels;
    Reference = reference;
    DisplayName = displayName ?? (key => key);
  }

  /// <summary>The catalogue display name for a marker, for the card text.</summary>
  public Func<string, string> DisplayName { get; }

  /// <summary>Newest collected_on first.</summary>
  public IReadOnlyList<LabPanelSnapshot> Panels { get; }

  /// <summary>The population range for the person's sex, or null when none applies.</summary>
  public Func<string, LabReference?> Reference { get; }

  public LabPanelSnapshot? Latest => Panels.Count > 0 ? Panels[0] : null;
}

/// <summary>What a rule produces when it fires.</summary>
public sealed record InsightCandidate(
  string Kind,
  string Severity,
  IReadOnlyList<string> Metrics,
  IReadOnlyDictionary<string, object?> Evidence,
  string Templated,
  /// <summary>How far outside the band, as a fraction of the interquartile range. Ranks ties within a severity.</summary>
  double BandDistance)
{
  public int Rank => InsightSeverities.Rank(Severity);
}

public interface IInsightRule
{
  string Kind { get; }

  /// <summary>Metrics the rule reads; every one must have an established baseline before the rule may fire.</summary>
  IReadOnlyList<string> MetricsUsed(RuleContext context);

  InsightCandidate? Evaluate(RuleContext context);
}

/// <summary>Runs the rule set over a context: minimum-data gate, learning fallback, ranking.</summary>
public interface IRuleEngine
{
  /// <summary>Candidates ranked best first: severity, then distance outside the band.</summary>
  IReadOnlyList<InsightCandidate> Evaluate(RuleContext context);
}

/// <summary>Computes the baseline rows for one person and day (pure maths over daily_summary).</summary>
public interface IBaselineService
{
  Task<IReadOnlyList<Baseline>> ComputeAsync(Guid personId, DateOnly day, CancellationToken ct = default);
}

/// <summary>Baselines, rules and cards for one person and one day. Idempotent per (person, day).</summary>
public interface IInterpretRunner
{
  /// <summary>Null when the person does not exist.</summary>
  Task<RecomputeResultDto?> RunForPersonAsync(Guid personId, DateOnly day, CancellationToken ct = default);

  /// <summary>Whether the day was already interpreted (a baseline or an insight row exists).</summary>
  Task<bool> HasRunAsync(Guid personId, DateOnly day, CancellationToken ct = default);
}

public interface ITodayService
{
  /// <summary>Null when the viewer may not see the target's insights.</summary>
  Task<TodayDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);
}

public interface IInsightService
{
  /// <summary>Newest first. Null when the viewer may not see the target's insights.</summary>
  Task<IReadOnlyList<InsightDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, bool includeDismissed, CancellationToken ct = default);

  /// <summary>Own cards only. Null when the card is not the caller's. Throws DomainValidationException on an unknown reaction.</summary>
  Task<InsightDto?> ReactAsync(Guid userId, Guid personId, Guid? householdId, Guid insightId, string reaction, RequestInfo info, CancellationToken ct = default);

  /// <summary>Own cards only. Null when the card is not the caller's.</summary>
  Task<InsightDto?> DismissAsync(Guid userId, Guid personId, Guid? householdId, Guid insightId, RequestInfo info, CancellationToken ct = default);

  /// <summary>
  /// Recomputes a person and day on demand. The caller may always recompute themselves; an admin
  /// may recompute a member of their household. Null when the target is not reachable that way.
  /// </summary>
  Task<RecomputeResultDto?> RecomputeAsync(Guid userId, Guid callerPersonId, Guid? householdId, bool isAdmin, RecomputeRequest request, RequestInfo info, CancellationToken ct = default);
}

public interface ITrendService
{
  /// <summary>Null when the metric is unknown or the viewer may not see its category.</summary>
  Task<TrendSeriesDto?> GetSeriesAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, int? days, CancellationToken ct = default);

  /// <summary>Metrics with n28 at or above <see cref="Baseline.TrendDays"/>. Null when nothing is granted.</summary>
  Task<IReadOnlyList<TrendMetricDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);
}

public sealed record ChatOptions(double Temperature = 0.3, int MaxTokens = 300);

public sealed record ChatCompletion(string Content, int? PromptTokens, int? CompletionTokens);

/// <summary>
/// The one seam to a language model. Local only: the implementation talks to the OpenAI-compatible
/// endpoint the user configured (Ai:BaseUrl), nothing else. Disabled unless Ai:Enabled is true.
/// </summary>
public interface IChatModel
{
  bool Enabled { get; }
  string ModelName { get; }
  Task<ChatCompletion> CompleteAsync(string system, string user, ChatOptions options, CancellationToken ct = default);
}

/// <summary>Asks the model to reword templated facts, validates the answer, falls back to the template.</summary>
public interface IInsightPhraser
{
  /// <summary>Null when the model is disabled, failed, or produced text that did not validate.</summary>
  Task<string?> PhraseAsync(Guid personId, string purpose, string templated, CancellationToken ct = default);
}
