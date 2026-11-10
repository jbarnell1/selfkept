using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Body;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.ContextEngine;

/// <summary>
/// Reads what the context-aware rules need for one person and day: stored and derived context,
/// the day log tags, hard training days, strength estimates, three tallies of the targets and the
/// personal patterns that became established that day. The person's own data for the nightly job:
/// no viewer is involved, so no grant applies.
/// </summary>
public sealed class ContextLoader : IContextLoader
{
  private readonly IContextEventRepository _events;
  private readonly IGoalRepository _goals;
  private readonly IMedicationRepository _medications;
  private readonly IJournalRepository _journal;
  private readonly IStrengthRepository _strength;
  private readonly IReadBackRepository _readBack;
  private readonly IDailySummaryRepository _summaries;
  private readonly ITargetService _targets;

  public ContextLoader(
    IContextEventRepository events,
    IGoalRepository goals,
    IMedicationRepository medications,
    IJournalRepository journal,
    IStrengthRepository strength,
    IReadBackRepository readBack,
    IDailySummaryRepository summaries,
    ITargetService targets)
  {
    _events = events;
    _goals = goals;
    _medications = medications;
    _journal = journal;
    _strength = strength;
    _readBack = readBack;
    _summaries = summaries;
    _targets = targets;
  }

  public async Task<ContextState> LoadOwnAsync(Person person, DateOnly day, CancellationToken ct = default)
  {
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var from = day.AddDays(-PatternMath.WindowDays - 8);

    var events = await _events.ListOwnAsync(person.Id, from, day, ct);
    var goals = await _goals.ListOwnActiveAsync(person.Id, ct);
    var medications = await _medications.ListOwnAsync(person.Id, ct);
    var journal = await _journal.ListOwnAsync(person.Id, from, day, ct);
    var logged = Logged(journal);
    var strength = await _strength.ListOwnAsync(person.Id, day.AddDays(-(ProgressService.WindowDays - 1)), day, ct);
    var (workoutsFrom, _) = DayBoundaries.UtcWindow(day.AddDays(-(ContextDerivation.HardTrainingLookbackDays + 1)), zone);
    var (_, workoutsTo) = DayBoundaries.UtcWindow(day, zone);
    var workouts = (await _readBack.ListOwnWorkoutsAsync(person.Id, workoutsFrom, workoutsTo, ct))
      .Select(w => new WorkoutDay(DayBoundaries.LocalDay(w.StartAt, zone), (w.EndAt - w.StartAt).TotalMinutes))
      .ToList();

    var record = new ContextRecord(events, goals, medications, logged, strength, workouts);
    var nights = PatternMath.NightsFrom(await _summaries.GetOwnRangeAsync(person.Id, PatternMath.Metrics, from, day, ct));

    return new ContextState
    {
      Active = ContextDerivation.Active(day, record),
      Logged = logged.ToDictionary(l => l.Day, l => l.Tags),
      HardTrainingDays = ContextDerivation.HardTrainingDays(day, record),
      Strength = strength
        .Select(s => (Set: s, Max: StrengthMath.EstimatedMaxKg(s.WeightKg, s.Reps)))
        .Where(x => x.Max != null)
        .Select(x => new LiftEstimate(x.Set.Day, x.Set.Lift, Lifts.DisplayName(x.Set.Lift), x.Max!.Value))
        .ToList(),
      Targets = Snapshot(await _targets.TallyOwnAsync(person.Id, day, ct)),
      TargetsYesterday = Snapshot(await _targets.TallyOwnAsync(person.Id, day.AddDays(-1), ct)),
      TargetsWeekAgo = Snapshot(await _targets.TallyOwnAsync(person.Id, day.AddDays(-7), ct)),
      NewPatterns = PatternMath.NewlyEstablished(logged, nights, day, contextDays: ContextDays(events, logged)),
    };
  }

  /// <summary>Days with entries, oldest first, with the distinct tags written that day.</summary>
  public static IReadOnlyList<LoggedDay> Logged(IEnumerable<JournalEntry> entries) =>
    entries
      .GroupBy(e => e.Day)
      .OrderBy(g => g.Key)
      .Select(g => new LoggedDay(g.Key, g.SelectMany(e => e.Tags).Distinct(StringComparer.Ordinal).ToList()))
      .ToList();

  /// <summary>Days covered by logged illness or travel, or with an illness symptom tag: the overlaps a pattern names.</summary>
  public static IReadOnlySet<DateOnly> ContextDays(IEnumerable<ContextEvent> events, IEnumerable<LoggedDay> logged)
  {
    var days = new HashSet<DateOnly>();
    foreach (var e in events.Where(e => e.Kind is ContextKinds.Illness or ContextKinds.Travel))
    {
      for (var d = e.StartedOn; d <= e.LastDay; d = d.AddDays(1)) days.Add(d);
    }
    foreach (var l in logged.Where(l => l.Tags.Any(t => ContextDerivation.IllnessTags.Contains(t)))) days.Add(l.Day);
    return days;
  }

  private static IReadOnlyList<TargetSnapshot> Snapshot(IEnumerable<TargetTallyDto> tallies) =>
    tallies.Select(t => new TargetSnapshot(t.Target.Id, t.Target.Label, t.Target.Period, t.MetDays, t.KnownDays, t.Met)).ToList();
}
