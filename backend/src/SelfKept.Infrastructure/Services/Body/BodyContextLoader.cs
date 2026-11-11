using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// Reads what the body and goal rules need for one person and day (docs/body-design.md, "Rules").
/// Everything here is the person's own data, read for the nightly job: no viewer is involved, so
/// no grant applies.
/// </summary>
public sealed class BodyContextLoader : IBodyContextLoader
{
  /// <summary>How far back a "reached" verdict looks to tell a new arrival from an old one.</summary>
  public const int ReachedLookbackDays = 7;

  private readonly IGoalRepository _goals;
  private readonly IMedicationRepository _medications;
  private readonly IDailySummaryRepository _summaries;
  private readonly ITargetService _targets;
  private readonly IMetricCatalogue _catalogue;

  public BodyContextLoader(
    IGoalRepository goals,
    IMedicationRepository medications,
    IDailySummaryRepository summaries,
    ITargetService targets,
    IMetricCatalogue catalogue)
  {
    _goals = goals;
    _medications = medications;
    _summaries = summaries;
    _targets = targets;
    _catalogue = catalogue;
  }

  public async Task<BodyContext> LoadAsync(Person person, DateOnly day, CancellationToken ct = default)
  {
    var goals = (await _goals.ListOwnActiveAsync(person.Id, ct))
      .OrderBy(g => g.Slot == GoalSlots.Main ? 0 : 1)
      .ToList();
    var keys = new HashSet<string>(StringComparer.Ordinal) { MetricKeys.WeightKg, MetricKeys.WaistCm };
    foreach (var key in goals.Select(ProgressService.GoalMetric).OfType<string>()) keys.Add(key);

    var from = day.AddDays(-(ProgressService.WindowDays - 1));
    var rows = await _summaries.GetOwnRangeAsync(person.Id, keys, from, day, ct);
    var read = rows
      .GroupBy(r => r.MetricKey, StringComparer.Ordinal)
      .ToDictionary(
        g => g.Key,
        g => (IReadOnlyList<DayValue>)g.OrderBy(r => r.Day).Select(r => new DayValue(r.Day, r.Value)).ToList(),
        StringComparer.Ordinal);

    var weight = ProgressService.WeightView.From(read.GetValueOrDefault(MetricKeys.WeightKg) ?? [], day);
    var tallies = await _targets.TallyOwnAsync(person.Id, day, ct);
    var medications = await _medications.ListOwnAsync(person.Id, ct);

    return new BodyContext
    {
      Goals = goals.Select(g => Standing(g, read, weight, day)).ToList(),
      Weight = weight.Kept.Select(p => new DatedValue(p.Day, p.Value)).ToList(),
      TrendWeightKg = weight.TrendNow,
      WeeklyRateKg = weight.RatePerWeek,
      Waist = (read.GetValueOrDefault(MetricKeys.WaistCm) ?? []).Select(p => new DatedValue(p.Day, p.Value)).ToList(),
      Targets = tallies
        .Select(t => new TargetWeek(t.Target.Label, t.Target.Kind, t.Target.Period, t.MetDays, t.KnownDays, t.Target.Period == TargetPeriods.Week ? t.Met : null))
        .ToList(),
      Substances = medications
        .Where(m => m.ActiveOn(day))
        .SelectMany(m => m.Substances)
        .ToHashSet(StringComparer.Ordinal),
    };
  }

  private GoalStanding Standing(Goal goal, IReadOnlyDictionary<string, IReadOnlyList<DayValue>> read, ProgressService.WeightView weight, DateOnly day)
  {
    var key = ProgressService.GoalMetric(goal);
    if (key == null || !read.TryGetValue(key, out var points))
    {
      return new GoalStanding(goal, key, Name(key), Unit(key), null, null, PaceStatuses.Unknown, false);
    }
    var isWeight = key == MetricKeys.WeightKg;
    var current = isWeight ? weight.TrendNow : ProgressService.Current(key, points, day);
    var pace = isWeight ? weight.RatePerWeek : BodyMath.WeeklyRate(points, day);
    if (current is not { } now || goal.TargetValue is not { } target)
    {
      return new GoalStanding(goal, key, Name(key), Unit(key), current, pace, PaceStatuses.Unknown, false);
    }

    var start = goal.StartValue ?? now;
    var status = ProgressService.Status(now, start, target, pace, goal.PacePerWeek);
    var earlierDay = day.AddDays(-ReachedLookbackDays);
    var earlier = isWeight
      ? weight.TrendOn(earlierDay)
      : ProgressService.Current(key, points.Where(p => p.Day <= earlierDay).ToList(), earlierDay);
    var reachedBefore = earlier is { } then && ProgressService.Status(then, start, target, null, null) == PaceStatuses.Reached;
    return new GoalStanding(goal, key, Name(key), Unit(key), now, pace, status, reachedBefore);
  }

  private string? Name(string? key) => key == null ? null : _catalogue.Find(key)?.DisplayName;

  private string? Unit(string? key) => key == null ? null : BodyMapping.SummaryUnit(key, _catalogue);
}
