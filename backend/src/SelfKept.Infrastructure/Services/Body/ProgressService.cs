using System.Globalization;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// The Progress tab in one read (docs/body-design.md, "Progress"): goals with their pace, the
/// target tally, the body snapshot, the series behind the charts, the strength log and, for the
/// person themself, goals they could pick with figures taken from their own numbers. Everything
/// is worked out on read from daily summaries; nothing here is stored.
/// </summary>
public sealed class ProgressService : IProgressService
{
  public const int WindowDays = 120;

  /// <summary>A goal within this share of its target counts as reached.</summary>
  private const double ReachedTolerance = 0.005;

  /// <summary>A weekly pace smaller than this share of the current value is "not moving".</summary>
  private const double FlatShare = 0.0005;

  private const int MaxProjectionWeeks = 156;

  /// <summary>Charted in this order; a series with no points is left out.</summary>
  private static readonly string[] SeriesKeys =
  [
    MetricKeys.WeightKg, MetricKeys.BodyFatPct, MetricKeys.WaistCm, MetricKeys.LeanMassKg,
    MetricKeys.Vo2Max, MetricKeys.ProteinG, MetricKeys.EnergyIntakeKcal,
  ];

  /// <summary>Metrics read a few times a week at most: the goal's "now" is the latest reading, not a weekly average.</summary>
  private static readonly HashSet<string> Sparse = new(StringComparer.Ordinal)
  {
    MetricKeys.BodyFatPct, MetricKeys.WaistCm, MetricKeys.LeanMassKg, MetricKeys.BoneMassKg, MetricKeys.BodyWaterKg, MetricKeys.Vo2Max,
  };

  private readonly IGoalRepository _goals;
  private readonly ITargetService _targets;
  private readonly IStrengthRepository _strength;
  private readonly IDailySummaryRepository _summaries;
  private readonly IPersonRepository _persons;
  private readonly IMetricCatalogue _catalogue;
  private readonly IEncryptionService _encryption;
  private readonly TimeProvider _clock;

  public ProgressService(
    IGoalRepository goals,
    ITargetService targets,
    IStrengthRepository strength,
    IDailySummaryRepository summaries,
    IPersonRepository persons,
    IMetricCatalogue catalogue,
    IEncryptionService encryption,
    TimeProvider clock)
  {
    _goals = goals;
    _targets = targets;
    _strength = strength;
    _summaries = summaries;
    _persons = persons;
    _catalogue = catalogue;
    _encryption = encryption;
    _clock = clock;
  }

  public async Task<ProgressDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    // The goal list is the gate: it is null unless the viewer may read body data.
    var goals = await _goals.ListAsync(viewerPersonId, targetPersonId, includeEnded: false, ct);
    if (goals == null) return null;
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var viewer = viewerPersonId == targetPersonId ? person : await _persons.GetByIdAsync(viewerPersonId, ct);
    var units = viewer?.Units ?? UnitSystem.Metric;

    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
    var from = today.AddDays(-(WindowDays - 1));

    var read = new Dictionary<string, IReadOnlyList<DayValue>>(StringComparer.Ordinal);
    foreach (var key in SeriesKeys.Concat(goals.Select(GoalMetric).OfType<string>()).Distinct(StringComparer.Ordinal))
    {
      var rows = await _summaries.GetRangeAsync(viewerPersonId, targetPersonId, key, from, today, ct);
      if (rows != null) read[key] = rows.OrderBy(r => r.Day).Select(r => new DayValue(r.Day, r.Value)).ToList();
    }

    var weight = WeightView.From(read.GetValueOrDefault(MetricKeys.WeightKg) ?? [], today);
    var bodyFat = read.GetValueOrDefault(MetricKeys.BodyFatPct) ?? [];
    var waist = read.GetValueOrDefault(MetricKeys.WaistCm) ?? [];
    var snapshot = Snapshot(person, weight, bodyFat, waist);

    var tally = await _targets.TallyAsync(viewerPersonId, targetPersonId, ct);
    var sets = await _strength.ListAsync(viewerPersonId, targetPersonId, from, today, ct);

    return new ProgressDto(
      today,
      goals.Select(g => GoalProgress(g, read, weight, today, units)).ToList(),
      tally?.Targets ?? [],
      snapshot,
      SeriesKeys.Where(k => read.TryGetValue(k, out var points) && points.Count > 0).Select(k => Series(k, read[k], weight)).ToList(),
      sets == null ? [] : StrengthService.Group(sets),
      viewerPersonId == targetPersonId ? GoalSuggestions.For(snapshot, units) : []);
  }

  /// <summary>The metric a goal is measured by: its own, or the usual one for its kind.</summary>
  public static string? GoalMetric(Goal goal) => goal.MetricKey ?? goal.Kind switch
  {
    GoalKinds.LoseFat or GoalKinds.GainMuscle or GoalKinds.Maintain => MetricKeys.WeightKg,
    GoalKinds.Recomposition => MetricKeys.BodyFatPct,
    GoalKinds.Fitness => MetricKeys.Vo2Max,
    GoalKinds.SleepBetter => MetricKeys.SleepStage,
    _ => null,
  };

  private static BodySnapshotDto Snapshot(Person person, WeightView weight, IReadOnlyList<DayValue> bodyFat, IReadOnlyList<DayValue> waist)
  {
    double? latestFat = bodyFat.Count > 0 ? bodyFat[^1].Value : null;
    double? latestWaist = waist.Count > 0 ? waist[^1].Value : null;
    var lean = BodyMath.LeanMassKg(weight.TrendNow, latestFat);
    return new BodySnapshotDto(
      weight.Latest,
      weight.TrendNow,
      weight.RatePerWeek,
      weight.RatePerWeek is { } rate && weight.TrendNow is { } now and > 0 ? rate / now * 100 : null,
      latestFat,
      BodyMath.FatMassKg(weight.TrendNow, latestFat),
      lean,
      BodyMath.Bmi(weight.TrendNow, person.HeightCm),
      BodyMath.LeanMassIndex(lean, person.HeightCm),
      latestWaist,
      BodyMath.WaistToHeight(latestWaist, person.HeightCm),
      LeanShare(weight, bodyFat),
      person.HeightCm,
      weight.SetAside,
      weight.Kept.Count > 0 ? weight.Kept[^1].Day : null);
  }

  /// <summary>Lean share of the weight lost between the first and last body fat readings in the window.</summary>
  private static double? LeanShare(WeightView weight, IReadOnlyList<DayValue> bodyFat)
  {
    if (bodyFat.Count < 2 || weight.Trend.Count == 0) return null;
    var start = weight.TrendOn(bodyFat[0].Day);
    var end = weight.TrendOn(bodyFat[^1].Day);
    if (start == null || end == null) return null;
    return BodyMath.LeanShareOfLoss(start.Value, bodyFat[0].Value, end.Value, bodyFat[^1].Value);
  }

  private BodySeriesDto Series(string key, IReadOnlyList<DayValue> points, WeightView weight)
  {
    var metric = _catalogue.Find(key);
    var isWeight = key == MetricKeys.WeightKg;
    var shown = isWeight ? weight.Kept : points;
    var line = isWeight ? weight.Trend : shown;
    return new BodySeriesDto(
      key,
      metric?.DisplayName ?? key,
      BodyMapping.SummaryUnit(key, _catalogue) ?? string.Empty,
      shown.Select(p => new BodyPointDto(p.Day, p.Value)).ToList(),
      isWeight ? weight.Trend.Select(p => new BodyPointDto(p.Day, Math.Round(p.Value, 2))).ToList() : null,
      shown.Count > 0 ? shown[^1].Value : null,
      line.Count >= 2 ? line[^1].Value - line[0].Value : null);
  }

  private GoalProgressDto GoalProgress(Goal goal, IReadOnlyDictionary<string, IReadOnlyList<DayValue>> read, WeightView weight, DateOnly today, UnitSystem units)
  {
    var dto = goal.ToDto(_encryption);
    var key = GoalMetric(goal);
    if (key == null || !read.TryGetValue(key, out var points))
    {
      return new GoalProgressDto(dto, null, null, null, null, PaceStatuses.Unknown, null,
        key == null ? "No number is set for this goal. The targets show how the days are going." : "Not shared with you.");
    }
    var unit = BodyMapping.SummaryUnit(key, _catalogue);
    var isWeight = key == MetricKeys.WeightKg;
    var current = isWeight ? weight.TrendNow : Current(key, points, today);
    var pace = isWeight ? weight.RatePerWeek : BodyMath.WeeklyRate(points, today);
    if (current is not { } now)
    {
      return new GoalProgressDto(dto, unit, null, null, null, PaceStatuses.Unknown, null, "No readings yet. Once a few arrive this shows where you stand.");
    }

    double? fraction = null;
    DateOnly? projected = null;
    var status = PaceStatuses.Unknown;
    if (goal.TargetValue is { } target)
    {
      var start = goal.StartValue ?? now;
      if (target != start) fraction = Math.Clamp((now - start) / (target - start), 0, 1);
      status = Status(now, start, target, pace, goal.PacePerWeek);
      if (status is PaceStatuses.OnTrack or PaceStatuses.Ahead or PaceStatuses.Behind && pace is { } p && p != 0)
      {
        var weeks = (target - now) / p;
        if (weeks is > 0 and <= MaxProjectionWeeks) projected = today.AddDays((int)Math.Round(weeks * 7));
      }
    }
    return new GoalProgressDto(dto, unit, now, fraction, pace, status, projected, Summary(goal, unit, now, pace, status, projected, units));
  }

  /// <summary>Where a non-weight metric stands: the latest reading for a sparse one, else the mean of the last seven days.</summary>
  internal static double? Current(string key, IReadOnlyList<DayValue> points, DateOnly today)
  {
    if (points.Count == 0) return null;
    if (Sparse.Contains(key)) return points[^1].Value;
    var week = points.Where(p => p.Day > today.AddDays(-7)).ToList();
    return week.Count > 0 ? week.Average(p => p.Value) : points[^1].Value;
  }

  /// <summary>
  /// Reached when the target has been passed in the direction of travel. Otherwise the actual
  /// pace is compared with the planned one: 125 % or more is ahead, 60 % or more is on track,
  /// less is behind, and a pace pointing away from the target is the wrong way.
  /// </summary>
  public static string Status(double current, double start, double target, double? pace, double? plannedPace)
  {
    var direction = Math.Sign(target - start);
    if (direction == 0)
    {
      // A hold-steady goal: within tolerance is reached, otherwise judged by which way it is drifting.
      if (Math.Abs(current - target) <= Math.Abs(target) * 0.01) return PaceStatuses.Reached;
      if (pace is not { } drift) return PaceStatuses.Unknown;
      return Math.Sign(target - current) == Math.Sign(drift) ? PaceStatuses.OnTrack : PaceStatuses.WrongWay;
    }
    if ((current - target) * direction >= -Math.Abs(target) * ReachedTolerance) return PaceStatuses.Reached;
    if (pace is not { } actual) return PaceStatuses.Unknown;

    var flat = Math.Abs(current) * FlatShare;
    if (Math.Abs(actual) <= flat) return PaceStatuses.Behind;
    if (Math.Sign(actual) != direction) return PaceStatuses.WrongWay;
    if (plannedPace is not { } planned || planned == 0) return PaceStatuses.OnTrack;
    var ratio = actual / planned;
    return ratio >= 1.25 ? PaceStatuses.Ahead : ratio >= 0.6 ? PaceStatuses.OnTrack : PaceStatuses.Behind;
  }

  private static string Summary(Goal goal, string? unit, double now, double? pace, string status, DateOnly? projected, UnitSystem units)
  {
    var parts = new List<string>();
    var current = BodyUnits.Format(now, unit, units);
    parts.Add(goal.TargetValue is { } target
      ? $"{current} now, aiming for {BodyUnits.Format(target, unit, units)}."
      : $"{current} now.");

    if (status == PaceStatuses.Reached)
    {
      parts.Add("You are there.");
      return string.Join(" ", parts);
    }
    if (pace is { } p)
    {
      parts.Add(status == PaceStatuses.Behind && Math.Abs(p) <= Math.Abs(now) * FlatShare
        ? "Holding level over the last four weeks."
        : $"Moving {BodyUnits.Change(p, unit, units, 2)} a week over the last four weeks.");
    }
    else
    {
      parts.Add("Not enough readings in the last four weeks to judge the pace.");
    }
    switch (status)
    {
      case PaceStatuses.Ahead when goal.PacePerWeek is { } planned:
        parts.Add($"That is faster than the {BodyUnits.Format(Math.Abs(planned), unit, units, 2)} a week you planned.");
        break;
      case PaceStatuses.Behind when goal.PacePerWeek is { } planned && pace is { } actual && Math.Abs(actual) > Math.Abs(now) * FlatShare:
        parts.Add($"That is slower than the {BodyUnits.Format(Math.Abs(planned), unit, units, 2)} a week you planned.");
        break;
      case PaceStatuses.WrongWay:
        parts.Add("That is away from the target for now.");
        break;
    }
    if (projected is { } date)
    {
      parts.Add($"At this pace you would get there around {date.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}.");
    }
    return string.Join(" ", parts);
  }

  /// <summary>The weight series as the rest of the page uses it: screened, smoothed and with its weekly rate.</summary>
  public sealed record WeightView(IReadOnlyList<DayValue> Kept, IReadOnlyList<DayValue> Trend, int SetAside, double? RatePerWeek)
  {
    public double? Latest => Kept.Count > 0 ? Kept[^1].Value : null;

    public double? TrendNow => Trend.Count > 0 ? Trend[^1].Value : null;

    /// <summary>The trend on a day, or on the nearest earlier day that has one. Null before the first reading.</summary>
    public double? TrendOn(DateOnly day)
    {
      double? value = null;
      foreach (var point in Trend)
      {
        if (point.Day > day) break;
        value = point.Value;
      }
      return value;
    }

    public static WeightView From(IReadOnlyList<DayValue> readings, DateOnly today)
    {
      var (kept, aside) = BodyMath.Screen(readings);
      return new WeightView(kept, BodyMath.Trend(kept), aside.Count, BodyMath.WeeklyRate(kept, today));
    }
  }
}
