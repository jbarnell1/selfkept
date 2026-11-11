namespace SelfKept.Core.Entities;

// Context (docs/context-design.md): what is going on in a person's life that explains a reading.
// A stored row is something the person said (source user) or a rule recorded (source rule).
// Derived context (a new goal, a medicine start, symptom tags, a hard session) is worked out at
// interpret time and on read, and never stored.

public class ContextEvent
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }

  /// <summary>See <see cref="ContextKinds"/>.</summary>
  public string Kind { get; set; } = ContextKinds.Other;

  public DateOnly StartedOn { get; set; }

  /// <summary>Null while it is still going on. An open event counts for <see cref="ContextKinds.OpenEndedDays"/> days.</summary>
  public DateOnly? EndedOn { get; set; }

  /// <summary>user or rule. See <see cref="ContextSources"/>.</summary>
  public string Source { get; set; } = ContextSources.User;

  /// <summary>The person's own words, encrypted with IEncryptionService. Null when none.</summary>
  public byte[]? NoteCipher { get; set; }

  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }

  /// <summary>The last day the event counts: its end, or the open-ended allowance from its start.</summary>
  public DateOnly LastDay => EndedOn ?? StartedOn.AddDays(ContextKinds.OpenEndedDays - 1);

  public bool ActiveOn(DateOnly day) => StartedOn <= day && LastDay >= day;
}

public static class ContextKinds
{
  public const string Illness = "illness";
  public const string CyclePhase = "cycle_phase";
  public const string NewGoal = "new_goal";
  public const string ScaleChange = "scale_change";
  public const string Travel = "travel";
  public const string MedicationChange = "medication_change";
  public const string HardTraining = "hard_training";
  public const string Other = "other";

  public static readonly string[] All = [Illness, CyclePhase, NewGoal, ScaleChange, Travel, MedicationChange, HardTraining, Other];

  /// <summary>
  /// How long an event with no end date counts, from its start. A starting point, not a
  /// published threshold: long enough for a cold or a trip, short enough that a forgotten event
  /// stops explaining readings on its own.
  /// </summary>
  public const int OpenEndedDays = 14;

  /// <summary>What a person reads for a kind, in a sentence: "You logged {phrase} on ...".</summary>
  public static string Phrase(string kind) => kind switch
  {
    Illness => "feeling unwell",
    CyclePhase => "a point in your cycle",
    NewGoal => "a new goal",
    ScaleChange => "a change of scale",
    Travel => "travel",
    MedicationChange => "a change of medicine",
    HardTraining => "hard training",
    _ => "something out of the ordinary",
  };

  /// <summary>A short label for a chip: "Illness", "Cycle phase".</summary>
  public static string Label(string kind) =>
    kind.Length == 0 ? kind : char.ToUpperInvariant(kind[0]) + kind[1..].Replace('_', ' ');
}

public static class ContextSources
{
  public const string User = "user";
  public const string Rule = "rule";
  public const string Derived = "derived";

  /// <summary>Values a stored row may carry. Derived context is never stored.</summary>
  public static readonly string[] Stored = [User, Rule];

  public static readonly string[] All = [User, Rule, Derived];
}
