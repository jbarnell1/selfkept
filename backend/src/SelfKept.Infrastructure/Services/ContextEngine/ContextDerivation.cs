using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret.Rules;

namespace SelfKept.Infrastructure.Services.ContextEngine;

/// <summary>A workout reduced to what context needs: its local day and how long it ran.</summary>
public readonly record struct WorkoutDay(DateOnly Day, double Minutes);

/// <summary>
/// The raw record context is worked out from. A null list means the reader may not see that part
/// of the record (another member without the grant), so nothing is derived from it.
/// </summary>
public sealed record ContextRecord(
  IReadOnlyList<ContextEvent> Events,
  IReadOnlyList<Goal>? Goals,
  IReadOnlyList<Medication>? Medications,
  IReadOnlyList<LoggedDay>? Logged,
  IReadOnlyList<StrengthSet>? Strength,
  IReadOnlyList<WorkoutDay>? Workouts);

/// <summary>
/// Works out what is going on in a person's life on one day (docs/context-design.md, "Derived
/// context"). Pure: the caller reads the record, this decides. Every window here is a plain
/// product default, a starting point and not a published threshold.
/// </summary>
public static class ContextDerivation
{
  /// <summary>A goal counts as new for its first two weeks.</summary>
  public const int NewGoalDays = 14;

  /// <summary>A medicine or supplement start or stop counts for two weeks.</summary>
  public const int MedicationChangeDays = 14;

  /// <summary>Symptom tags count on the day and the two days before.</summary>
  public const int SymptomDays = 3;

  /// <summary>A workout at least this long counts as hard training.</summary>
  public const double LongWorkoutMinutes = 60;

  /// <summary>How far back hard training days are listed for the recovery rule.</summary>
  public const int HardTrainingLookbackDays = 14;

  /// <summary>Symptom tags that read as being unwell. Soreness, pain, stress and low mood are left out: they have other usual causes.</summary>
  public static readonly string[] IllnessTags =
    [JournalTags.SoreThroat, JournalTags.Congestion, JournalTags.Cough, JournalTags.Fever, JournalTags.Nausea, JournalTags.StomachUpset];

  public static IReadOnlyList<ActiveContext> Active(DateOnly day, ContextRecord sources)
  {
    var active = new List<ActiveContext>();

    foreach (var e in sources.Events.Where(e => e.ActiveOn(day)))
    {
      active.Add(new ActiveContext(e.Kind, e.StartedOn, e.EndedOn, e.Source,
        $"You logged {ContextKinds.Phrase(e.Kind)} on {Spoken(e.StartedOn)}", e.Id));
    }

    foreach (var goal in sources.Goals ?? [])
    {
      var age = day.DayNumber - goal.StartedOn.DayNumber;
      if (goal.Status != GoalStatuses.Active || age < 0 || age >= NewGoalDays) continue;
      active.Add(new ActiveContext(ContextKinds.NewGoal, goal.StartedOn, goal.StartedOn.AddDays(NewGoalDays - 1), ContextSources.Derived,
        $"Your goal to {GoalKinds.DisplayName(goal.Kind).ToLowerInvariant()} started on {Spoken(goal.StartedOn)}", Detail: goal.Kind));
    }

    foreach (var m in sources.Medications ?? [])
    {
      var what = m.Kind == MedicationKinds.Supplement ? "A supplement" : "A medicine";
      if (m.StartedOn is { } started && Within(started, day, MedicationChangeDays))
      {
        active.Add(new ActiveContext(ContextKinds.MedicationChange, started, started.AddDays(MedicationChangeDays - 1), ContextSources.Derived,
          $"{what} on your list started on {Spoken(started)}", Detail: "started"));
      }
      if (m.StoppedOn is { } stopped && Within(stopped, day, MedicationChangeDays))
      {
        active.Add(new ActiveContext(ContextKinds.MedicationChange, stopped, stopped.AddDays(MedicationChangeDays - 1), ContextSources.Derived,
          $"{what} on your list stopped on {Spoken(stopped)}", Detail: "stopped"));
      }
    }

    if (sources.Logged != null)
    {
      // The earliest symptom in the window, so the card says when it began.
      var symptom = sources.Logged
        .Where(l => Within(l.Day, day, SymptomDays))
        .OrderBy(l => l.Day)
        .Select(l => (l.Day, Tag: l.Tags.FirstOrDefault(t => IllnessTags.Contains(t))))
        .FirstOrDefault(x => x.Tag != null);
      if (symptom.Tag != null)
      {
        active.Add(new ActiveContext(ContextKinds.Illness, symptom.Day, null, ContextSources.Derived,
          $"You logged {TagPhrase(symptom.Tag)} on {Spoken(symptom.Day)}", Detail: symptom.Tag));
      }
    }

    var yesterday = day.AddDays(-1);
    if (HardTrainingOn(yesterday, sources) is { } training)
    {
      active.Add(new ActiveContext(ContextKinds.HardTraining, yesterday, yesterday, ContextSources.Derived, training.Reason, Detail: training.Detail));
    }

    return active.OrderByDescending(a => a.Since).ThenBy(a => a.Kind, StringComparer.Ordinal).ToList();
  }

  /// <summary>Days in the lookback with hard training of any kind, the interpreted day included.</summary>
  public static IReadOnlyCollection<DateOnly> HardTrainingDays(DateOnly day, ContextRecord sources)
  {
    var days = new HashSet<DateOnly>();
    for (var i = 0; i < HardTrainingLookbackDays; i++)
    {
      var d = day.AddDays(-i);
      if (HardTrainingOn(d, sources) != null) days.Add(d);
    }
    foreach (var e in sources.Events.Where(e => e.Kind == ContextKinds.HardTraining))
    {
      for (var d = e.StartedOn; d <= e.LastDay && d <= day; d = d.AddDays(1))
      {
        if (day.DayNumber - d.DayNumber < HardTrainingLookbackDays) days.Add(d);
      }
    }
    return days;
  }

  private static (string Reason, string Detail)? HardTrainingOn(DateOnly d, ContextRecord sources)
  {
    if (sources.Strength?.Any(s => s.Day == d) == true) return ($"You logged a strength session on {Spoken(d)}", "strength");
    if (sources.Logged?.FirstOrDefault(l => l.Day == d) is { } logged && logged.Tags.Contains(JournalTags.HardSession))
      return ($"You logged a hard session on {Spoken(d)}", JournalTags.HardSession);
    if (sources.Workouts?.Where(w => w.Day == d).Select(w => w.Minutes).DefaultIfEmpty(0).Max() is >= LongWorkoutMinutes and var minutes)
      return ($"A {Math.Round(minutes):0} minute workout on {Spoken(d)}", "long_workout");
    return null;
  }

  private static bool Within(DateOnly when, DateOnly day, int days)
  {
    var age = day.DayNumber - when.DayNumber;
    return age >= 0 && age < days;
  }

  /// <summary>"sore_throat" reads "a sore throat"; "fever" reads "a fever"; "nausea" reads "nausea".</summary>
  public static string TagPhrase(string tag)
  {
    var words = JournalTags.DisplayName(tag).ToLowerInvariant();
    return tag is JournalTags.Nausea or JournalTags.Congestion or JournalTags.Fatigue or JournalTags.Alcohol or JournalTags.LateCaffeine or JournalTags.Caffeine
      ? words
      : $"a {words}";
  }

  public static string Spoken(DateOnly day) => LabRules.Spoken(day);
}
