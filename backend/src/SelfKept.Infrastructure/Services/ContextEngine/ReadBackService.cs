using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.ContextEngine;

/// <summary>
/// Sleep detail and workouts read back from what the phone stored (docs/context-design.md,
/// "Read-backs"). Sleep is read under the sleep grant, workouts under the activity grant.
/// </summary>
public sealed class ReadBackService : IReadBackService
{
  public const int DefaultSleepDays = 14;
  public const int DefaultWorkoutDays = 28;
  public const int MaxDays = 92;

  private readonly IReadBackRepository _readBack;
  private readonly IWorkoutRepository _workouts;
  private readonly IPersonRepository _persons;
  private readonly TimeProvider _clock;

  public ReadBackService(IReadBackRepository readBack, IWorkoutRepository workouts, IPersonRepository persons, TimeProvider clock)
  {
    _readBack = readBack;
    _workouts = workouts;
    _persons = persons;
    _clock = clock;
  }

  public async Task<SleepNightsDto?> GetSleepAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var (first, last) = Range(from, to, DefaultSleepDays, zone);

    // A night belongs to the day it ended, so it can start the evening before the first day.
    var (fromUtc, _) = DayBoundaries.UtcWindow(first.AddDays(-1), zone);
    var (_, toUtc) = DayBoundaries.UtcWindow(last, zone);
    var samples = await _readBack.GetSleepSamplesAsync(viewerPersonId, targetPersonId, fromUtc, toUtc, ct);
    if (samples == null) return null;
    return new SleepNightsDto(first, last, Nights(samples, zone, first, last));
  }

  public async Task<WorkoutsDto?> GetWorkoutsAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var (first, last) = Range(from, to, DefaultWorkoutDays, zone);
    var (fromUtc, _) = DayBoundaries.UtcWindow(first, zone);
    var (_, toUtc) = DayBoundaries.UtcWindow(last, zone);
    var rows = await _workouts.ListAsync(viewerPersonId, targetPersonId, fromUtc, toUtc, ct);
    if (rows == null) return null;

    var workouts = rows
      .OrderBy(w => w.StartAt)
      .Select(w => new WorkoutReadDto(w.Id, w.Type, Group(w.Type), DayBoundaries.LocalDay(w.StartAt, zone), w.StartAt, w.EndAt,
        Math.Round((w.EndAt - w.StartAt).TotalMinutes, 1), w.DistanceM, w.EnergyKcal, w.AvgHr, w.MaxHr))
      .ToList();
    var weeks = workouts
      .GroupBy(w => WeekOf(w.Day))
      .OrderBy(g => g.Key)
      .Select(g => new WorkoutWeekDto(g.Key, g.Count(), Math.Round(g.Sum(w => w.DurationMinutes), 1),
        g.Count(w => w.Group == WorkoutGroups.Strength), g.Count(w => w.Group == WorkoutGroups.Cardio), g.Count(w => w.Group == WorkoutGroups.Mixed)))
      .ToList();
    return new WorkoutsDto(first, last, workouts, weeks);
  }

  private (DateOnly First, DateOnly Last) Range(DateOnly? from, DateOnly? to, int defaultDays, TimeZoneInfo zone)
  {
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, zone);
    var last = to ?? today;
    var first = from ?? last.AddDays(-(defaultDays - 1));
    if (first > last) throw new DomainValidationException("from must not be after to.");
    if (last.DayNumber - first.DayNumber + 1 > MaxDays) throw new DomainValidationException($"A read-back covers at most {MaxDays} days.");
    return (first, last);
  }

  /// <summary>The Monday that starts the week a day falls in.</summary>
  public static DateOnly WeekOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

  /// <summary>strength, cardio or mixed, from the workout type the phone sent.</summary>
  public static string Group(string type)
  {
    var t = type.ToLowerInvariant();
    if (t.Contains("strength") || t.Contains("weight") || t.Contains("functional") || t.Contains("core")) return WorkoutGroups.Strength;
    if (t.Contains("hiit") || t.Contains("interval") || t.Contains("cross") || t.Contains("circuit") || t.Contains("mixed")) return WorkoutGroups.Mixed;
    return WorkoutGroups.Cardio;
  }

  /// <summary>
  /// One row per night in [first, last], built the way the daily summary is: the night belongs to
  /// the day it ended, and of several apps or devices the one with the most minutes asleep is used.
  /// </summary>
  public static IReadOnlyList<SleepNightDto> Nights(IReadOnlyList<Sample> samples, TimeZoneInfo zone, DateOnly first, DateOnly last)
  {
    var nights = new List<SleepNightDto>();
    foreach (var day in samples.Where(s => s.EndAt != null).GroupBy(s => DailySummaryRules.DayFor(s, zone) ?? DateOnly.MinValue).OrderBy(g => g.Key))
    {
      if (day.Key < first || day.Key > last) continue;
      var best = day
        .GroupBy(DailySummaryRules.OriginOf)
        .Select(g => (Samples: g.ToList(), Asleep: Merged(g.Where(s => SleepStages.IsAsleep(s.Value)))))
        .MaxBy(x => x.Asleep);
      if (best.Samples == null || best.Asleep <= 0) continue;

      var set = best.Samples;
      double Stage(int stage) => Math.Round(Merged(set.Where(s => (int)s.Value == stage)), 1);
      var light = Stage(SleepStages.Light);
      var deep = Stage(SleepStages.Deep);
      var rem = Stage(SleepStages.Rem);
      var hasStages = light + deep + rem > 0;
      var bed = set.Min(s => s.StartAt);
      var wake = set.Max(s => s.EndAt!.Value);
      nights.Add(new SleepNightDto(
        day.Key,
        DayBoundaries.AsUtc(bed),
        DayBoundaries.AsUtc(wake),
        Math.Round((wake - bed).TotalMinutes, 1),
        Math.Round(best.Asleep, 1),
        hasStages ? Stage(SleepStages.Awake) : null,
        hasStages ? light : null,
        hasStages ? deep : null,
        hasStages ? rem : null,
        Stage(SleepStages.Unspecified) is var unspecified && unspecified > 0 ? unspecified : null,
        hasStages));
    }
    return nights;
  }

  /// <summary>Minutes covered by the samples' intervals, overlaps counted once.</summary>
  private static double Merged(IEnumerable<Sample> samples)
  {
    var total = 0.0;
    DateTime? start = null, end = null;
    foreach (var s in samples.Where(s => s.EndAt != null).OrderBy(s => s.StartAt))
    {
      if (start == null || s.StartAt > end)
      {
        if (start != null) total += (end!.Value - start.Value).TotalMinutes;
        start = s.StartAt;
        end = s.EndAt;
      }
      else if (s.EndAt > end)
      {
        end = s.EndAt;
      }
    }
    if (start != null) total += (end!.Value - start.Value).TotalMinutes;
    return total;
  }
}

public static class WorkoutGroups
{
  public const string Strength = "strength";
  public const string Cardio = "cardio";
  public const string Mixed = "mixed";
}
