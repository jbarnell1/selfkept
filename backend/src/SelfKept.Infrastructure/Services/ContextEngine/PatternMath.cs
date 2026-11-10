using System.Globalization;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Services.Interpret;

namespace SelfKept.Infrastructure.Services.ContextEngine;

/// <summary>
/// Personal patterns (docs/context-design.md, "Patterns"): for each day log tag, the night after a
/// day with the tag against the night after a logged day without it. A day with nothing logged is
/// unknown, never "without", the same rule the tally uses. Pure maths over what the caller read.
///
/// No published minimum count exists for a pattern in one person's data. The floors and the noise
/// line here are the proposal's own and ours: a starting point, not a published threshold.
/// </summary>
public static class PatternMath
{
  /// <summary>How far back a pattern looks, in days. A starting point.</summary>
  public const int WindowDays = 120;

  /// <summary>Nights after a tagged day a pattern needs. The proposal's own floor, a starting point.</summary>
  public const int MinNightsWith = 8;

  /// <summary>Nights after a logged day without the tag a pattern needs. The proposal's own floor, a starting point.</summary>
  public const int MinNightsWithout = 16;

  /// <summary>The noise line is this share of the interquartile range of the "without" nights. A starting point.</summary>
  public const double NoiseLineIqrShare = 0.5;

  public const string Note = "A pattern in your own data, not proof.";

  /// <summary>The overnight readings a pattern compares, in display order.</summary>
  public static readonly string[] Metrics =
    [MetricKeys.SleepStage, MetricKeys.HrvRmssdMs, MetricKeys.HrvSdnnMs, MetricKeys.RestingHrBpm, MetricKeys.WristTempDeltaC];

  /// <summary>
  /// Every (tag, metric) pair with at least one night after a tagged day, as of <paramref name="endDay"/>.
  /// Tag days run from endDay minus the window to the day before endDay, so the night after each
  /// is on or before endDay. contextDays are days with illness or travel context, counted as overlaps.
  /// </summary>
  public static IReadOnlyList<PatternStats> Compute(
    IReadOnlyList<LoggedDay> logged,
    IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, double>> nights,
    DateOnly endDay,
    IReadOnlySet<DateOnly>? contextDays = null)
  {
    var from = endDay.AddDays(-WindowDays);
    var days = logged.Where(l => l.Day >= from && l.Day < endDay).ToList();
    var tags = days.SelectMany(d => d.Tags).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
    var result = new List<PatternStats>();
    foreach (var tag in tags)
    {
      foreach (var metric in Metrics)
      {
        if (!nights.TryGetValue(metric, out var series) || series.Count == 0) continue;
        var with = new List<(DateOnly Day, double Value)>();
        var without = new List<double>();
        foreach (var d in days)
        {
          if (!series.TryGetValue(d.Day.AddDays(1), out var value)) continue;
          if (d.Tags.Contains(tag)) with.Add((d.Day, value));
          else without.Add(value);
        }
        if (with.Count == 0) continue;
        result.Add(Stats(tag, metric, with, without, contextDays));
      }
    }
    return result;
  }

  public static PatternStats Stats(string tag, string metric, IReadOnlyList<(DateOnly Day, double Value)> with, IReadOnlyList<double> without, IReadOnlySet<DateOnly>? contextDays = null)
  {
    var withValues = with.Select(w => w.Value).ToList();
    var medianWith = BaselineMath.Median(withValues) ?? 0;
    var withLow = BaselineMath.Percentile(withValues, 25) ?? medianWith;
    var withHigh = BaselineMath.Percentile(withValues, 75) ?? medianWith;
    double medianWithout = 0, withoutLow = 0, withoutHigh = 0, overlap = 0, noise = 0;
    if (without.Count > 0)
    {
      medianWithout = BaselineMath.Median(without)!.Value;
      withoutLow = BaselineMath.Percentile(without, 25)!.Value;
      withoutHigh = BaselineMath.Percentile(without, 75)!.Value;
      overlap = withValues.Count(v => v >= withoutLow && v <= withoutHigh) / (double)withValues.Count;
      noise = NoiseLineIqrShare * (withoutHigh - withoutLow);
    }
    var difference = medianWith - medianWithout;
    var established = with.Count >= MinNightsWith && without.Count >= MinNightsWithout && Math.Abs(difference) > noise && difference != 0;
    var weekend = with.Count(w => w.Day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday);
    var inContext = contextDays == null ? 0 : with.Count(w => contextDays.Contains(w.Day) || contextDays.Contains(w.Day.AddDays(1)));
    return new PatternStats(tag, metric, with.Count, without.Count,
      R(medianWith), R(medianWithout), R(difference), R(withLow), R(withHigh), R(withoutLow), R(withoutHigh),
      Math.Round(overlap, 2), R(noise), weekend, inContext, established);
  }

  /// <summary>Patterns established on <paramref name="day"/> and on none of the <paramref name="lookback"/> days before.</summary>
  public static IReadOnlyList<PatternStats> NewlyEstablished(
    IReadOnlyList<LoggedDay> logged,
    IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, double>> nights,
    DateOnly day,
    int lookback = 7,
    IReadOnlySet<DateOnly>? contextDays = null)
  {
    var now = Compute(logged, nights, day, contextDays).Where(p => p.Established).ToList();
    if (now.Count == 0) return [];
    var before = new HashSet<(string, string)>();
    for (var i = 1; i <= lookback; i++)
    {
      foreach (var p in Compute(logged, nights, day.AddDays(-i), contextDays).Where(p => p.Established)) before.Add((p.Tag, p.MetricKey));
    }
    return now.Where(p => !before.Contains((p.Tag, p.MetricKey))).ToList();
  }

  /// <summary>Nights by metric and day, from daily summary rows.</summary>
  public static IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, double>> NightsFrom(IEnumerable<DailySummary> rows) =>
    rows.GroupBy(r => r.MetricKey, StringComparer.Ordinal)
      .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<DateOnly, double>)g.GroupBy(r => r.Day).ToDictionary(x => x.Key, x => x.First().Value), StringComparer.Ordinal);

  public static string Unit(string metric) => metric switch
  {
    MetricKeys.SleepStage => "min",
    _ => MetricSeed.All.FirstOrDefault(m => m.Key == metric)?.Unit ?? string.Empty,
  };

  /// <summary>
  /// "On 9 nights after alcohol, against 31 without, your resting heart rate ran 4 bpm higher
  /// (58 to 66 bpm on those nights, 54 to 59 bpm on the rest). 6 of the 9 were Friday or Saturday
  /// nights. A pattern in your own data, not proof."
  /// </summary>
  public static string Sentence(PatternStats p)
  {
    var unit = Unit(p.MetricKey);
    var after = $"On {p.NightsWith} nights after {TagWords(p.Tag)}, against {p.NightsWithout} without, ";
    string change;
    if (p.MetricKey == MetricKeys.SleepStage)
    {
      change = $"you slept {Num(Math.Abs(p.Difference), 0)} min {(p.Difference < 0 ? "less" : "more")}";
    }
    else
    {
      var decimals = p.MetricKey == MetricKeys.WristTempDeltaC ? 2 : 0;
      change = $"your {MetricWords(p.MetricKey)} ran {Num(Math.Abs(p.Difference), decimals)} {UnitWords(unit)} {(p.Difference < 0 ? "lower" : "higher")}";
    }
    var places = p.MetricKey == MetricKeys.WristTempDeltaC ? 2 : 0;
    var ranges = $" ({Num(p.WithLow, places)} to {Num(p.WithHigh, places)} {UnitWords(unit)} on those nights, {Num(p.WithoutLow, places)} to {Num(p.WithoutHigh, places)} {UnitWords(unit)} on the rest).";
    var weekend = p.WeekendNights > 0 ? $" {p.WeekendNights} of the {p.NightsWith} were Friday or Saturday nights." : string.Empty;
    var context = p.ContextNights > 0 ? $" {p.ContextNights} fell while you were unwell or travelling." : string.Empty;
    return after + change + ranges + weekend + context + " " + Note;
  }

  /// <summary>How a metric reads inside a sentence: "HRV", "resting heart rate", "wrist temperature change".</summary>
  public static string MetricWords(string metric) => metric switch
  {
    MetricKeys.HrvRmssdMs or MetricKeys.HrvSdnnMs => "HRV",
    MetricKeys.SleepStage => "sleep",
    _ => MetricNames.For(metric).ToLowerInvariant(),
  };

  private static string TagWords(string tag) => JournalTags.DisplayName(tag).ToLowerInvariant();

  private static string UnitWords(string unit) => unit == "C" ? "C" : unit;

  private static string Num(double value, int decimals) =>
    Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString(decimals == 0 ? "0" : "0.0#", CultureInfo.InvariantCulture);

  private static double R(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
