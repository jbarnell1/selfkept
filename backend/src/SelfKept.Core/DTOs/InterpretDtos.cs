using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelfKept.Core.DTOs;

// Wire shapes from docs/insights-design.md ("API"). Property names serialize camelCase.

public sealed record InsightDto(
  Guid Id,
  DateOnly Day,
  string Kind,
  string Severity,
  IReadOnlyList<string> Metrics,
  JsonElement Evidence,
  string Text,
  bool Generated,
  DateTime? ShownAt,
  DateTime? DismissedAt,
  string? Reaction,
  /// <summary>Display names for <see cref="Metrics"/>, same order, for chips a person reads.</summary>
  IReadOnlyList<string> MetricNames);

/// <summary>Where last night's value sits against the person's 28-day band: below, inside or above. Null without enough data.</summary>
public sealed record ReadinessDto(string? Hrv, string? RestingHr, string? Sleep);

public sealed record TodayDto(
  DateOnly Day,
  string Sentence,
  bool Generated,
  double? SleepMinutes,
  ReadinessDto Readiness,
  IReadOnlyList<InsightDto> Cards,
  CheckinDto? LastCheckin,
  bool Learning);

public sealed record ReactionRequest([Required] string Reaction);

/// <summary>Both optional: the caller's own person and yesterday in their time zone by default.</summary>
public sealed record RecomputeRequest(Guid? PersonId, DateOnly? Day);

public sealed record RecomputeResultDto(Guid PersonId, DateOnly Day, int Baselines, int Insights, IReadOnlyList<string> Kinds);

public sealed record TrendPointDto(
  DateOnly Day,
  double Value,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Min,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Max);

public sealed record TrendBandDto(double Median28, double P25, double P75, double P10, double P90);

public sealed record TrendSeriesDto(string Metric, string Unit, IReadOnlyList<TrendPointDto> Points, TrendBandDto? Band, int BaselineDays);

/// <summary>
/// One row of the Trends list: a metric with enough history, its latest value and where that sits
/// in the band, plus the last 28 days (day and value) and the band itself for the row's small line.
/// </summary>
public sealed record TrendMetricDto(
  string Metric,
  string DisplayName,
  string Unit,
  DateOnly? LatestDay,
  double? LatestValue,
  string? Band,
  int BaselineDays,
  IReadOnlyList<TrendPointDto> Recent,
  TrendBandDto? Range);

public static class BandPositions
{
  public const string Below = "below";
  public const string Inside = "inside";
  public const string Above = "above";

  /// <summary>Null when the band is not established yet.</summary>
  public static string? Of(double value, double? p25, double? p75)
  {
    if (p25 == null || p75 == null) return null;
    if (value < p25) return Below;
    if (value > p75) return Above;
    return Inside;
  }
}
