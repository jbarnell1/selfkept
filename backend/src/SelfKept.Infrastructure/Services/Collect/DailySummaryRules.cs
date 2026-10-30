using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Collect;

public enum SummaryRule
{
  /// <summary>value = sum of sample values on the day (by start).</summary>
  Sum,

  /// <summary>value = mean, min and max of sample values on the day (by start).</summary>
  Average,

  /// <summary>value = minutes asleep for the night that ended on the day (awake and in_bed excluded).</summary>
  SleepMinutes,
}

/// <summary>
/// Which metrics get a daily_summary row and how it is derived. Pure functions so the rules are
/// tested without a database; IDailySummaryService supplies the samples.
/// </summary>
public static class DailySummaryRules
{
  private static readonly Dictionary<string, SummaryRule> Rules = new(StringComparer.Ordinal)
  {
    [MetricKeys.Steps] = SummaryRule.Sum,
    [MetricKeys.DistanceM] = SummaryRule.Sum,
    [MetricKeys.ActiveEnergyKcal] = SummaryRule.Sum,
    [MetricKeys.ExerciseMin] = SummaryRule.Sum,
    [MetricKeys.Flights] = SummaryRule.Sum,
    [MetricKeys.HeartRateBpm] = SummaryRule.Average,
    [MetricKeys.HrvSdnnMs] = SummaryRule.Average,
    [MetricKeys.HrvRmssdMs] = SummaryRule.Average,
    [MetricKeys.Spo2Pct] = SummaryRule.Average,
    [MetricKeys.RespiratoryRateBpm] = SummaryRule.Average,
    [MetricKeys.WeightKg] = SummaryRule.Average,
    [MetricKeys.RestingHrBpm] = SummaryRule.Average,
    [MetricKeys.SleepStage] = SummaryRule.SleepMinutes,
    [MetricKeys.CheckinMood] = SummaryRule.Average,
    // Daily-kind metrics a device reports once a day; averaged so a resent value stays one row.
    [MetricKeys.WristTempDeltaC] = SummaryRule.Average,
    [MetricKeys.OuraReadiness] = SummaryRule.Average,
    [MetricKeys.OuraSleepScore] = SummaryRule.Average,
  };

  public static SummaryRule? RuleFor(string metricKey) => Rules.TryGetValue(metricKey, out var rule) ? rule : null;

  public static bool HasRule(string metricKey) => Rules.ContainsKey(metricKey);

  /// <summary>Whether the day a sample belongs to is taken from its end (sleep) or its start.</summary>
  public static bool AttributesByEnd(string metricKey) => RuleFor(metricKey) == SummaryRule.SleepMinutes;

  /// <summary>The local day a sample counts towards, or null when the metric has no daily rule.</summary>
  public static DateOnly? DayFor(Sample sample, TimeZoneInfo zone)
  {
    if (RuleFor(sample.MetricKey) is not { } rule) return null;
    var instant = rule == SummaryRule.SleepMinutes ? sample.EndAt ?? sample.StartAt : sample.StartAt;
    return DayBoundaries.LocalDay(instant, zone);
  }

  /// <summary>
  /// Derives the row for one (metric, day) from the samples that fall on it. Null means no row
  /// (nothing on the day, or nothing that counts), so the caller deletes any stale row.
  /// </summary>
  public static DailySummary? Derive(Guid personId, string metricKey, DateOnly day, IReadOnlyList<Sample> samples, DateTime computedAtUtc)
  {
    if (RuleFor(metricKey) is not { } rule || samples.Count == 0) return null;

    switch (rule)
    {
      case SummaryRule.Sum:
      {
        // One reading per origin: the iPhone and the Watch both count the same walk, and a ring
        // that writes into Apple Health repeats what the cloud feed sent. Total each origin and
        // keep the largest, never the sum across origins.
        var best = samples.GroupBy(OriginOf).Select(g => g.ToList()).MaxBy(g => g.Sum(s => s.Value))!;
        return new DailySummary
        {
          PersonId = personId, MetricKey = metricKey, Day = day,
          Value = best.Sum(s => s.Value), Count = best.Count, ComputedAt = computedAtUtc,
        };
      }

      case SummaryRule.Average:
        return new DailySummary
        {
          PersonId = personId, MetricKey = metricKey, Day = day,
          Value = samples.Average(s => s.Value), Min = samples.Min(s => s.Value), Max = samples.Max(s => s.Value),
          Count = samples.Count, ComputedAt = computedAtUtc,
        };

      case SummaryRule.SleepMinutes:
      {
        var asleep = samples.Where(s => s.EndAt != null && SleepStages.IsAsleep(s.Value)).ToList();
        if (asleep.Count == 0) return null;
        // Same rule as Sum, per origin, with overlapping segments of one origin merged first
        // (an "asleep" span and the stages inside it describe the same minutes).
        var best = asleep.GroupBy(OriginOf).Select(g => (Samples: g.ToList(), Minutes: MergedMinutes(g))).MaxBy(x => x.Minutes);
        return new DailySummary
        {
          PersonId = personId, MetricKey = metricKey, Day = day,
          Value = Math.Round(best.Minutes, 1),
          Count = best.Samples.Count, ComputedAt = computedAtUtc,
        };
      }

      default:
        return null;
    }
  }

  /// <summary>
  /// Which app or device a sample came from: the collector's sourceBundle (HealthKit),
  /// sourcePackage (Health Connect) or origin (server-side feeds such as Oura), else its source.
  /// </summary>
  public static string OriginOf(Sample sample)
  {
    if (!string.IsNullOrEmpty(sample.Meta))
    {
      try
      {
        using var doc = System.Text.Json.JsonDocument.Parse(sample.Meta);
        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
          foreach (var key in OriginKeys)
          {
            if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String && v.GetString() is { Length: > 0 } origin)
              return origin;
          }
        }
      }
      catch (System.Text.Json.JsonException)
      {
        // Unreadable meta: fall back to the source.
      }
    }
    return sample.SourceId.ToString();
  }

  private static readonly string[] OriginKeys = ["sourceBundle", "sourcePackage", "origin"];

  /// <summary>Total minutes covered by the samples' intervals, counting overlaps once.</summary>
  private static double MergedMinutes(IEnumerable<Sample> samples)
  {
    var total = 0.0;
    DateTime? start = null, end = null;
    foreach (var s in samples.OrderBy(s => s.StartAt))
    {
      var sEnd = s.EndAt!.Value;
      if (start == null || s.StartAt > end)
      {
        if (start != null) total += (end!.Value - start.Value).TotalMinutes;
        start = s.StartAt;
        end = sEnd;
      }
      else if (sEnd > end)
      {
        end = sEnd;
      }
    }
    if (start != null) total += (end!.Value - start.Value).TotalMinutes;
    return total;
  }
}
