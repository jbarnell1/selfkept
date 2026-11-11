using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Body;
using SelfKept.Infrastructure.Services.Collect;
using SelfKept.Infrastructure.Services.Interpret;

namespace SelfKept.Infrastructure.Services.ContextEngine;

/// <summary>
/// The weekly review, built by code (docs/context-design.md, "Weekly review"): one Monday to
/// Sunday week of targets, goal movement, sleep, standout readings, cards and patterns. Every
/// section is read under its own grant and is null when the viewer may not see it.
/// </summary>
public sealed class WeeklyReviewService : IWeeklyReviewService
{
  /// <summary>Seven hours, the AASM and SRS consensus for adults (Watson 2015).</summary>
  public const double SevenHoursMinutes = 420;

  /// <summary>A week's median counts as a standout past this share of the four weeks' interquartile range. A starting point.</summary>
  public const double StandoutIqrShare = 0.5;

  public const int UsualDays = 28;
  public const int MinWeekDays = 3;
  public const int MinUsualDays = 14;
  private const int JournalReadLimit = 500;

  /// <summary>The readings a standout is looked for in.</summary>
  public static readonly string[] StandoutMetrics =
    [MetricKeys.RestingHrBpm, MetricKeys.HrvRmssdMs, MetricKeys.HrvSdnnMs, MetricKeys.RespiratoryRateBpm, MetricKeys.SleepStage, MetricKeys.Steps, MetricKeys.ExerciseMin];

  private readonly IReadBackRepository _readBack;
  private readonly IDailyTargetRepository _targets;
  private readonly IGoalRepository _goals;
  private readonly IJournalRepository _journal;
  private readonly IStrengthRepository _strength;
  private readonly IDailySummaryRepository _summaries;
  private readonly IInsightRepository _insights;
  private readonly IPatternService _patterns;
  private readonly IPersonRepository _persons;
  private readonly IMetricCatalogue _catalogue;
  private readonly IEncryptionService _encryption;
  private readonly TimeProvider _clock;

  public WeeklyReviewService(
    IReadBackRepository readBack,
    IDailyTargetRepository targets,
    IGoalRepository goals,
    IJournalRepository journal,
    IStrengthRepository strength,
    IDailySummaryRepository summaries,
    IInsightRepository insights,
    IPatternService patterns,
    IPersonRepository persons,
    IMetricCatalogue catalogue,
    IEncryptionService encryption,
    TimeProvider clock)
  {
    _readBack = readBack;
    _targets = targets;
    _goals = goals;
    _journal = journal;
    _strength = strength;
    _summaries = summaries;
    _insights = insights;
    _patterns = patterns;
    _persons = persons;
    _catalogue = catalogue;
    _encryption = encryption;
    _clock = clock;
  }

  public async Task<WeeklyReviewDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? weekOf, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var allowed = await _readBack.AllowedCategoriesAsync(viewerPersonId, targetPersonId, ct);
    if (allowed.Count == 0) return null;

    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
    var monday = ReadBackService.WeekOf(weekOf ?? DefaultWeekDay(today));
    if (monday > today) throw new DomainValidationException("weekOf cannot be in the future.");
    var sunday = monday.AddDays(6);
    var units = person.Units;

    return new WeeklyReviewDto(
      monday,
      sunday,
      sunday <= today,
      await TargetsAsync(viewerPersonId, targetPersonId, sunday, ct),
      await GoalsAsync(viewerPersonId, targetPersonId, monday, sunday, units, ct),
      await SleepAsync(viewerPersonId, targetPersonId, monday, sunday, ct),
      await StandoutsAsync(viewerPersonId, targetPersonId, allowed, monday, sunday, units, ct),
      (await _insights.ListAsync(viewerPersonId, targetPersonId, monday, sunday, includeDismissed: false, ct))?.Select(i => i.ToDto()).ToList(),
      (await _patterns.GetAsOfAsync(viewerPersonId, targetPersonId, sunday, ct))?.Patterns);
  }

  /// <summary>On a Sunday the week that ends today; any other day the last complete week.</summary>
  public static DateOnly DefaultWeekDay(DateOnly today) => today.DayOfWeek == DayOfWeek.Sunday ? today : ReadBackService.WeekOf(today).AddDays(-1);

  private async Task<ReviewTargetsDto?> TargetsAsync(Guid viewer, Guid target, DateOnly sunday, CancellationToken ct)
  {
    var targets = await _targets.ListAsync(viewer, target, ct);
    if (targets == null) return null;
    var active = targets.Where(t => t.Active).ToList();
    var from = sunday.AddDays(-(TargetTally.WeekDays - 1));

    // Each input under the viewer's grant for it, exactly as the tally reads them.
    var metrics = new Dictionary<string, IReadOnlyDictionary<DateOnly, double>>(StringComparer.Ordinal);
    foreach (var key in active.Where(t => t.Kind == TargetKinds.Metric && t.MetricKey != null).Select(t => t.MetricKey!).Distinct(StringComparer.Ordinal))
    {
      if (await _summaries.GetRangeAsync(viewer, target, key, from, sunday, ct) is { } rows) metrics[key] = rows.ToDictionary(r => r.Day, r => r.Value);
    }
    var journal = active.Any(t => t.Kind == TargetKinds.Journal) ? await _journal.ListAsync(viewer, target, from, sunday, JournalReadLimit, ct) : null;
    var strength = active.Any(t => t.Kind == TargetKinds.Strength) ? await _strength.ListAsync(viewer, target, from, sunday, ct) : null;
    var data = new TallyData(metrics, journal, strength);

    var rows2 = active
      .Select(t => (Target: t, Tally: TargetTally.Compute(t, t.ToDto(_catalogue), sunday, data)))
      .Select(x => new ReviewTargetDto(x.Target.Id, x.Target.Label, x.Target.Period, x.Tally.MetDays, x.Tally.KnownDays,
        x.Target.Period == TargetPeriods.Week ? x.Tally.Met : x.Tally.KnownDays == 0 ? null : x.Tally.MetDays * 2 > x.Tally.KnownDays))
      .ToList();
    return new ReviewTargetsDto(
      rows2.Where(r => r.Met == true).ToList(),
      rows2.Where(r => r.Met == false).ToList(),
      rows2.Where(r => r.Met == null).ToList());
  }

  private async Task<IReadOnlyList<ReviewGoalDto>?> GoalsAsync(Guid viewer, Guid target, DateOnly monday, DateOnly sunday, UnitSystem units, CancellationToken ct)
  {
    var goals = await _goals.ListAsync(viewer, target, includeEnded: false, ct);
    if (goals == null) return null;
    var result = new List<ReviewGoalDto>();
    foreach (var goal in goals)
    {
      var dto = goal.ToDto(_encryption);
      var key = ProgressService.GoalMetric(goal);
      if (key == null)
      {
        result.Add(new ReviewGoalDto(goal.Id, goal.Slot, goal.Kind, dto.Title, null, null, null, null, null, 0, "No number is set for this goal. The targets show how the week went."));
        continue;
      }
      var unit = BodyMapping.SummaryUnit(key, _catalogue);
      var rows = await _summaries.GetRangeAsync(viewer, target, key, monday, sunday, ct);
      if (rows == null)
      {
        result.Add(new ReviewGoalDto(goal.Id, goal.Slot, goal.Kind, dto.Title, key, unit, null, null, null, 0, "Not shared with you."));
        continue;
      }
      if (rows.Count == 0)
      {
        result.Add(new ReviewGoalDto(goal.Id, goal.Slot, goal.Kind, dto.Title, key, unit, null, null, null, 0, "No readings this week."));
        continue;
      }
      var first = rows[0].Value;
      var last = rows[^1].Value;
      var change = last - first;
      var summary = rows.Count == 1
        ? $"One reading this week: {BodyUnits.Format(last, unit, units)}."
        : $"{BodyUnits.Format(first, unit, units)} to {BodyUnits.Format(last, unit, units)} over {rows.Count} readings, {BodyUnits.Change(change, unit, units)}.";
      result.Add(new ReviewGoalDto(goal.Id, goal.Slot, goal.Kind, dto.Title, key, unit, first, last, Math.Round(change, 2), rows.Count, summary));
    }
    return result;
  }

  private async Task<ReviewSleepDto?> SleepAsync(Guid viewer, Guid target, DateOnly monday, DateOnly sunday, CancellationToken ct)
  {
    var rows = await _summaries.GetRangeAsync(viewer, target, MetricKeys.SleepStage, monday.AddDays(-7), sunday, ct);
    if (rows == null) return null;
    var week = rows.Where(r => r.Day >= monday).Select(r => r.Value).ToList();
    var before = rows.Where(r => r.Day < monday).Select(r => r.Value).ToList();
    var median = BaselineMath.Median(week);
    var previous = BaselineMath.Median(before);
    var seven = week.Count(v => v >= SevenHoursMinutes);
    string summary;
    if (median is not { } m)
    {
      summary = "No nights recorded this week.";
    }
    else
    {
      summary = $"{week.Count} night{(week.Count == 1 ? string.Empty : "s")} recorded, a median of {BodyUnits.Format(m, "min", UnitSystem.Metric)}; {seven} of them 7 hours or more.";
      if (previous is { } p) summary += $" The week before had a median of {BodyUnits.Format(p, "min", UnitSystem.Metric)}.";
      summary += " Sleep researchers' consensus for adults is 7 or more hours a night.";
    }
    return new ReviewSleepDto(week.Count, median is { } md ? Math.Round(md, 1) : null, previous is { } pv ? Math.Round(pv, 1) : null, seven, summary);
  }

  private async Task<IReadOnlyList<ReviewStandoutDto>?> StandoutsAsync(Guid viewer, Guid target, IReadOnlySet<ShareCategory> allowed, DateOnly monday, DateOnly sunday, UnitSystem units, CancellationToken ct)
  {
    var metrics = StandoutMetrics.Select(_catalogue.Find).OfType<Metric>().Where(m => allowed.Contains(m.Category)).ToList();
    if (metrics.Count == 0) return null;
    var result = new List<ReviewStandoutDto>();
    foreach (var metric in metrics)
    {
      var rows = await _summaries.GetRangeAsync(viewer, target, metric.Key, monday.AddDays(-UsualDays), sunday, ct);
      if (rows == null) continue;
      if (Standout(metric.Key, rows, monday, units) is { } standout) result.Add(standout);
    }
    return result;
  }

  /// <summary>The week's median against the four weeks before, when the gap is past half their interquartile range.</summary>
  public static ReviewStandoutDto? Standout(string metricKey, IReadOnlyList<DailySummary> rows, DateOnly monday, UnitSystem units)
  {
    var week = rows.Where(r => r.Day >= monday).Select(r => r.Value).ToList();
    var usual = rows.Where(r => r.Day < monday).Select(r => r.Value).ToList();
    if (week.Count < MinWeekDays || usual.Count < MinUsualDays) return null;
    var weekMedian = BaselineMath.Median(week)!.Value;
    var usualMedian = BaselineMath.Median(usual)!.Value;
    var iqr = BaselineMath.Percentile(usual, 75)!.Value - BaselineMath.Percentile(usual, 25)!.Value;
    var difference = weekMedian - usualMedian;
    if (difference == 0 || Math.Abs(difference) <= StandoutIqrShare * iqr) return null;

    var unit = metricKey == MetricKeys.SleepStage ? "min" : PatternMath.Unit(metricKey);
    var direction = difference > 0 ? "up" : "down";
    var name = MetricNames.For(metricKey);
    var decimals = metricKey is MetricKeys.Steps ? 0 : 1;
    return new ReviewStandoutDto(metricKey, name, unit, Math.Round(weekMedian, 1), Math.Round(usualMedian, 1), Math.Round(difference, 1), direction,
      $"{name} ran {(difference > 0 ? "higher" : "lower")} than the four weeks before: a median of {BodyUnits.Format(weekMedian, unit, units, decimals)} against {BodyUnits.Format(usualMedian, unit, units, decimals)}.");
  }
}
