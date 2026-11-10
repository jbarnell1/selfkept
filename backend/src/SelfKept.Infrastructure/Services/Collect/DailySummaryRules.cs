using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Collect;

public enum SummaryRule
{
  /// <summary>value = sum of sample values on the day (by start).</summary>
  Sum,

  /// <summary>value = mean, min and max of sample values on the day (by start).</summary>
  Average,

  /// <summary>
  /// value = minutes asleep on the sleep day: 18:00 the evening before to 18:00 on the day, by
  /// each segment's end, so the night that ended that morning and any nap that day (awake and
  /// in_bed excluded).
  /// </summary>
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
    // Body readings arrive a few times a week at most; the day's figure is their mean.
    [MetricKeys.BodyFatPct] = SummaryRule.Average,
    [MetricKeys.WaistCm] = SummaryRule.Average,
    [MetricKeys.LeanMassKg] = SummaryRule.Average,
    [MetricKeys.BoneMassKg] = SummaryRule.Average,
    [MetricKeys.BodyWaterKg] = SummaryRule.Average,
    [MetricKeys.Vo2Max] = SummaryRule.Average,
    // What was eaten, as the phone's nutrition records report it, summed per day.
    [MetricKeys.EnergyIntakeKcal] = SummaryRule.Sum,
    [MetricKeys.ProteinG] = SummaryRule.Sum,
    [MetricKeys.CarbsG] = SummaryRule.Sum,
    [MetricKeys.FatG] = SummaryRule.Sum,
  };

  public static SummaryRule? RuleFor(string metricKey) => Rules.TryGetValue(metricKey, out var rule) ? rule : null;

  public static bool HasRule(string metricKey) => Rules.ContainsKey(metricKey);

  /// <summary>
  /// How far a sleep day runs ahead of the calendar day: sleep day D is 18:00 local on D-1 to
  /// 18:00 local on D, so a night that starts in the evening and ends in the morning stays whole
  /// and an afternoon nap counts for its own day. Our own choice, not a published standard; 18:00
  /// is late enough for a nap and early enough for an early bedtime.
  /// </summary>
  public static readonly TimeSpan SleepDayShift = TimeSpan.FromHours(6);

  /// <summary>Where sleep day D starts on D-1, and ends on D, on the local clock (18:00).</summary>
  private static readonly TimeOnly SleepDayStart = TimeOnly.MinValue.Add(-SleepDayShift);

  /// <summary>Whether the day a sample belongs to is taken from its end (sleep) or its start.</summary>
  public static bool AttributesByEnd(string metricKey) => RuleFor(metricKey) == SummaryRule.SleepMinutes;

  /// <summary>
  /// The local day a sample counts towards, or null when the metric has no daily rule. A sample
  /// belongs to day D exactly when the instant it is filed by (its end for sleep, else its start)
  /// lies in <see cref="UtcWindow"/> for D.
  /// </summary>
  public static DateOnly? DayFor(Sample sample, TimeZoneInfo zone)
  {
    if (RuleFor(sample.MetricKey) is not { } rule) return null;
    return rule == SummaryRule.SleepMinutes
      ? SleepDay(sample.EndAt ?? sample.StartAt, zone)
      : DayBoundaries.LocalDay(sample.StartAt, zone);
  }

  /// <summary>
  /// The UTC instants [start, end) whose samples count towards (metric, day): local midnight to
  /// midnight, or for sleep 18:00 the evening before to 18:00 on the day. Every loader uses this.
  /// </summary>
  public static (DateTime StartUtc, DateTime EndUtc) UtcWindow(string metricKey, DateOnly day, TimeZoneInfo zone) =>
    AttributesByEnd(metricKey)
      ? (DayBoundaries.LocalTimeUtc(day.AddDays(-1), SleepDayStart, zone), DayBoundaries.LocalTimeUtc(day, SleepDayStart, zone))
      : DayBoundaries.UtcWindow(day, zone);

  /// <summary>The sleep day an instant (a sleep sample's end) falls in: the local day of the instant plus six hours.</summary>
  private static DateOnly SleepDay(DateTime instant, TimeZoneInfo zone)
  {
    var utc = DayBoundaries.AsUtc(instant);
    var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Add(SleepDayShift));
    // A clock change at 18:00 itself makes the wall-clock arithmetic one day out for up to an
    // hour; the window decides, so DayFor and every loader always agree.
    var (start, end) = UtcWindow(MetricKeys.SleepStage, day, zone);
    if (utc < start) return day.AddDays(-1);
    if (utc >= end) return day.AddDays(1);
    return day;
  }

  /// <summary>
  /// Derives the row for one (metric, day) from the samples that fall on it, and for a dense metric
  /// the minutes retention folded out of them. Null means no row (nothing on the day, or nothing
  /// that counts), so the caller deletes any stale row.
  /// </summary>
  public static DailySummary? Derive(Guid personId, string metricKey, DateOnly day, IReadOnlyList<Sample> samples, DateTime computedAtUtc, IReadOnlyList<SampleMinute>? minutes = null)
  {
    minutes ??= [];
    if (RuleFor(metricKey) is not { } rule || (samples.Count == 0 && minutes.Count == 0)) return null;

    // Only averaged metrics are ever folded (DenseMetrics). A minute is its count of readings at
    // its mean, so the day's mean, min, max and count come out as if every reading were still here.
    if (rule == SummaryRule.Average && minutes.Count > 0)
    {
      var count = samples.Count + minutes.Sum(m => m.Count);
      var total = samples.Sum(s => s.Value) + minutes.Sum(m => m.Avg * m.Count);
      return new DailySummary
      {
        PersonId = personId, MetricKey = metricKey, Day = day,
        Value = total / count,
        Min = Math.Min(samples.Count > 0 ? samples.Min(s => s.Value) : double.MaxValue, minutes.Min(m => m.Min)),
        Max = Math.Max(samples.Count > 0 ? samples.Max(s => s.Value) : double.MinValue, minutes.Max(m => m.Max)),
        Count = count, ComputedAt = computedAtUtc,
      };
    }
    if (samples.Count == 0) return null;

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
