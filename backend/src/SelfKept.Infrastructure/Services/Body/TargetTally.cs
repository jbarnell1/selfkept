using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// What a tally is worked out from, already read under the viewer's grants. A metric the viewer
/// may not read is simply absent from <see cref="Metrics"/>; a null day log or strength log means
/// the viewer may not read it. Those targets come back unknown, never as "not met".
/// </summary>
public sealed record TallyData(
  IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, double>> Metrics,
  IReadOnlyList<JournalEntry>? Journal,
  IReadOnlyList<StrengthSet>? Strength);

/// <summary>
/// Where one target stands (docs/body-design.md, "Targets and the tally"). The rule that matters:
/// nothing logged is unknown, not a pass. A "no alcohol" day only counts when something was
/// written in the day log that day, and today stays open until the answer cannot change.
/// </summary>
public static class TargetTally
{
  public const int WeekDays = 7;

  /// <summary>A weekly average needs readings on at least this many of the seven days.</summary>
  public const int WeekMinMetricDays = 3;

  /// <summary>A weekly "at most" count from the day log needs something logged on at least this many days.</summary>
  public const int WeekMinLoggedDays = 3;

  /// <summary>
  /// Metrics whose value for a day is final when it arrives: last night's sleep, a morning
  /// weigh-in. For these today is decided at once; for running totals (steps, protein) a day that
  /// has not reached an "at least" yet, or not passed an "at most" yet, stays open.
  /// </summary>
  private static readonly HashSet<string> DecidedOnArrival = new(StringComparer.Ordinal)
  {
    MetricKeys.SleepStage, MetricKeys.HrvRmssdMs, MetricKeys.HrvSdnnMs, MetricKeys.RestingHrBpm,
    MetricKeys.WeightKg, MetricKeys.BodyFatPct, MetricKeys.WaistCm, "oura_sleep_score", "oura_readiness",
  };

  public static string UnitFor(DailyTarget target, string? metricUnit) => target.Kind switch
  {
    TargetKinds.Metric => metricUnit ?? string.Empty,
    TargetKinds.Journal => "entries",
    _ => target.Period == TargetPeriods.Week ? "days" : "lifts",
  };

  public static TargetTallyDto Compute(DailyTarget target, TargetDto dto, DateOnly today, TallyData data)
  {
    var days = Enumerable.Range(0, WeekDays).Select(i => today.AddDays(i - (WeekDays - 1))).ToList();
    var perDay = days.Select(d => DayValue(target, d, data)).ToList();
    if (perDay.All(v => v.Readable == false))
    {
      return new TargetTallyDto(dto, null, null, days.Select(_ => (bool?)null).ToList(), 0, 0);
    }

    List<bool?> week;
    double? current;
    bool? met;
    if (target.Period == TargetPeriods.Day)
    {
      week = days.Select((d, i) => DayMet(target, perDay[i], isToday: d == today)).ToList();
      current = perDay[^1].Value;
      met = week[^1];
    }
    else
    {
      // A week target shows, per day, whether that day added to it; the verdict is the seven days together.
      week = perDay.Select(v => v.Value is { } x ? x > 0 : v.Logged ? false : (bool?)null).ToList();
      (current, met) = WeekVerdict(target, perDay);
    }
    return new TargetTallyDto(dto, current, met, week, week.Count(w => w == true), week.Count(w => w != null));
  }

  private readonly record struct Reading(bool Readable, double? Value, bool Logged);

  /// <summary>The day's number for the target, and whether anything was logged that day that lets "none" be believed.</summary>
  private static Reading DayValue(DailyTarget target, DateOnly day, TallyData data)
  {
    switch (target.Kind)
    {
      case TargetKinds.Metric:
        if (target.MetricKey == null || !data.Metrics.TryGetValue(target.MetricKey, out var series)) return new Reading(false, null, false);
        return series.TryGetValue(day, out var v) ? new Reading(true, v, true) : new Reading(true, null, false);
      case TargetKinds.Journal:
        if (data.Journal == null) return new Reading(false, null, false);
        var entries = data.Journal.Where(j => j.Day == day).ToList();
        if (entries.Count == 0) return new Reading(true, null, false);
        return new Reading(true, entries.Count(j => target.Tag != null && j.Tags.Contains(target.Tag)), true);
      default:
        if (data.Strength == null) return new Reading(false, null, false);
        // A day with no set is a known rest day once there is a set somewhere in the seven days.
        // With no set at all in the window every day is unknown, like a day with nothing in the
        // day log, so an "at most" target never passes on nothing logged.
        if (data.Strength.Count == 0) return new Reading(true, null, false);
        return new Reading(true, data.Strength.Count(s => s.Day == day), true);
    }
  }

  private static bool? DayMet(DailyTarget target, Reading reading, bool isToday)
  {
    if (!reading.Readable || reading.Value is not { } value) return null;
    var final = !isToday || (target.Kind == TargetKinds.Metric && DecidedOnArrival.Contains(target.MetricKey ?? string.Empty));
    if (target.Comparator == TargetComparators.AtLeast)
    {
      if (value >= target.Value) return true;
      return final ? false : null;
    }
    if (value > target.Value) return false;
    return final ? true : null;
  }

  private static (double? Current, bool? Met) WeekVerdict(DailyTarget target, IReadOnlyList<Reading> perDay)
  {
    var known = perDay.Where(v => v.Value != null).Select(v => v.Value!.Value).ToList();
    switch (target.Kind)
    {
      case TargetKinds.Metric:
        if (known.Count < WeekMinMetricDays) return (known.Count == 0 ? null : known.Average(), null);
        var average = known.Average();
        return (average, target.Comparator == TargetComparators.AtLeast ? average >= target.Value : average <= target.Value);
      case TargetKinds.Journal:
        var count = known.Sum();
        if (target.Comparator == TargetComparators.AtLeast) return (count, count >= target.Value ? true : known.Count == 0 ? null : false);
        if (count > target.Value) return (count, false);
        return (count, perDay.Count(v => v.Logged) >= WeekMinLoggedDays ? true : null);
      default:
        var sessions = (double)known.Count(v => v > 0);
        if (!perDay.Any(v => v.Logged)) return (sessions, null);
        return (sessions, target.Comparator == TargetComparators.AtLeast ? sessions >= target.Value : sessions <= target.Value);
    }
  }
}
