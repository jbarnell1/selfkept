namespace SelfKept.Core.Entities;

// Body, goals and the day log (docs/body-design.md). Everything here hangs off a person and is
// read as (viewerPersonId, targetPersonId) in the repository layer.

/// <summary>
/// What a person is working towards. One active goal per slot: a main goal and, optionally, a
/// secondary one. A goal that ends keeps its row so progress can be read back later.
/// </summary>
public class Goal
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }

  /// <summary>main or secondary. See <see cref="GoalSlots"/>.</summary>
  public string Slot { get; set; } = GoalSlots.Main;

  /// <summary>See <see cref="GoalKinds"/>.</summary>
  public string Kind { get; set; } = GoalKinds.Custom;

  /// <summary>The person's own words for the goal, encrypted. Null when the kind says it all.</summary>
  public string? TitleCipher { get; set; }

  /// <summary>The metric the goal is measured by, when it has one (weight_kg, body_fat_pct, ...).</summary>
  public string? MetricKey { get; set; }

  public double? StartValue { get; set; }
  public double? TargetValue { get; set; }

  /// <summary>Planned change per week in the metric's unit, signed (negative to lose).</summary>
  public double? PacePerWeek { get; set; }

  public DateOnly StartedOn { get; set; }
  public DateOnly? TargetDate { get; set; }

  /// <summary>active, done or dropped. See <see cref="GoalStatuses"/>.</summary>
  public string Status { get; set; } = GoalStatuses.Active;

  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
  public DateTime? EndedAt { get; set; }
}

public static class GoalSlots
{
  public const string Main = "main";
  public const string Secondary = "secondary";
  public static readonly string[] All = [Main, Secondary];
}

public static class GoalKinds
{
  public const string LoseFat = "lose_fat";
  public const string GainMuscle = "gain_muscle";
  public const string Recomposition = "recomposition";
  public const string Maintain = "maintain";
  public const string KeepStrength = "keep_strength";
  public const string Fitness = "fitness";
  public const string SleepBetter = "sleep_better";
  public const string ImproveLabs = "improve_labs";
  public const string Custom = "custom";
  public static readonly string[] All = [LoseFat, GainMuscle, Recomposition, Maintain, KeepStrength, Fitness, SleepBetter, ImproveLabs, Custom];

  public static string DisplayName(string kind) => kind switch
  {
    LoseFat => "Lose fat",
    GainMuscle => "Gain muscle",
    Recomposition => "Lose fat and gain muscle",
    Maintain => "Hold steady",
    KeepStrength => "Keep strength",
    Fitness => "Get fitter",
    SleepBetter => "Sleep better",
    ImproveLabs => "Improve blood work",
    _ => "Your goal",
  };
}

public static class GoalStatuses
{
  public const string Active = "active";
  public const string Done = "done";
  public const string Dropped = "dropped";
  public static readonly string[] All = [Active, Done, Dropped];
}

/// <summary>
/// One thing to hit each day or week on the way to a goal: a number read from a metric, a count
/// of day log entries with a tag, or the number of days with a strength session.
/// </summary>
public class DailyTarget
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }
  public Guid? GoalId { get; set; }

  /// <summary>Short label shown on Today, for example "Protein" or "No alcohol".</summary>
  public string Label { get; set; } = string.Empty;

  /// <summary>metric, journal or strength. See <see cref="TargetKinds"/>.</summary>
  public string Kind { get; set; } = TargetKinds.Metric;

  public string? MetricKey { get; set; }
  public string? Tag { get; set; }

  /// <summary>at_least or at_most.</summary>
  public string Comparator { get; set; } = TargetComparators.AtLeast;

  public double Value { get; set; }

  /// <summary>day or week (the last seven days).</summary>
  public string Period { get; set; } = TargetPeriods.Day;

  public bool Active { get; set; } = true;
  public short SortOrder { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}

public static class TargetKinds
{
  public const string Metric = "metric";
  public const string Journal = "journal";
  public const string Strength = "strength";
  public static readonly string[] All = [Metric, Journal, Strength];
}

public static class TargetComparators
{
  public const string AtLeast = "at_least";
  public const string AtMost = "at_most";
  public static readonly string[] All = [AtLeast, AtMost];
}

public static class TargetPeriods
{
  public const string Day = "day";
  public const string Week = "week";
  public static readonly string[] All = [Day, Week];
}

/// <summary>
/// One line of the day log: something eaten or drunk, a symptom, a training note, or a plain
/// note. The words are encrypted; the coarse tags are not, so targets and patterns can count them.
/// </summary>
public class JournalEntry
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }
  public DateTime At { get; set; }

  /// <summary>The calendar day in the person's time zone when the entry was written.</summary>
  public DateOnly Day { get; set; }

  /// <summary>See <see cref="JournalKinds"/>.</summary>
  public string Kind { get; set; } = JournalKinds.Note;

  public byte[]? TextCipher { get; set; }
  public string[] Tags { get; set; } = [];

  /// <summary>small, usual or large; food and drink only.</summary>
  public string? Portion { get; set; }

  /// <summary>chip, typed, assistant or checkin. See <see cref="JournalSources"/>.</summary>
  public string Source { get; set; } = JournalSources.Typed;

  /// <summary>The assistant message this entry was saved from, when it was. Lets the save be undone from the chat.</summary>
  public Guid? MessageId { get; set; }

  public DateTime CreatedAt { get; set; }
}

public static class JournalKinds
{
  public const string Food = "food";
  public const string Drink = "drink";
  public const string Symptom = "symptom";
  public const string Training = "training";
  public const string Sleep = "sleep";
  public const string Note = "note";
  public static readonly string[] All = [Food, Drink, Symptom, Training, Sleep, Note];
}

public static class JournalSources
{
  public const string Chip = "chip";
  public const string Typed = "typed";
  public const string Assistant = "assistant";
  public const string Checkin = "checkin";
  public static readonly string[] All = [Chip, Typed, Assistant, Checkin];
}

public static class Portions
{
  public const string Small = "small";
  public const string Usual = "usual";
  public const string Large = "large";
  public static readonly string[] All = [Small, Usual, Large];
}

/// <summary>
/// A medicine or supplement a person takes. Brand, product, dose and the ingredient list are
/// encrypted; the coarse substance tags are worked out by code from the names so rules can explain
/// a reading ("you take creatine, which raises creatinine") without reading the words.
/// </summary>
public class Medication
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }

  /// <summary>medication or supplement.</summary>
  public string Kind { get; set; } = MedicationKinds.Supplement;

  /// <summary>Encrypted JSON: brand, product, dose, schedule note and ingredients (name, amount, unit).</summary>
  public string DetailCipher { get; set; } = string.Empty;

  /// <summary>True for a generic, false for a name brand, null when not said.</summary>
  public bool? Generic { get; set; }

  /// <summary>daily, weekly or as_needed.</summary>
  public string Schedule { get; set; } = MedicationSchedules.Daily;

  /// <summary>See <see cref="SubstanceTags"/>.</summary>
  public string[] Substances { get; set; } = [];

  public DateOnly? StartedOn { get; set; }
  public DateOnly? StoppedOn { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }

  public bool ActiveOn(DateOnly day) => (StartedOn == null || StartedOn <= day) && (StoppedOn == null || StoppedOn >= day);
}

public static class MedicationKinds
{
  public const string Medication = "medication";
  public const string Supplement = "supplement";
  public static readonly string[] All = [Medication, Supplement];
}

public static class MedicationSchedules
{
  public const string Daily = "daily";
  public const string Weekly = "weekly";
  public const string AsNeeded = "as_needed";
  public static readonly string[] All = [Daily, Weekly, AsNeeded];
}

/// <summary>
/// The substances the cross-reference rules know about (docs/cross-reference.md, "Medicines and
/// supplements"). A tag explains a reading; it never advises on a medicine.
/// </summary>
public static class SubstanceTags
{
  public const string Testosterone = "testosterone";
  public const string Statin = "statin";
  public const string Thyroid = "thyroid";
  public const string Biotin = "biotin";
  public const string Iron = "iron";
  public const string VitaminD = "vitamin_d";
  public const string Creatine = "creatine";
  public const string Glp1 = "glp1";
  public const string BetaBlocker = "beta_blocker";
  public const string Estrogen = "estrogen";
  public const string Metformin = "metformin";
  public const string Zinc = "zinc";
  public const string Magnesium = "magnesium";
  public const string Omega3 = "omega_3";
  public const string B12 = "vitamin_b12";
  public const string Caffeine = "caffeine";
  public const string Melatonin = "melatonin";
  public static readonly string[] All =
  [
    Testosterone, Statin, Thyroid, Biotin, Iron, VitaminD, Creatine, Glp1, BetaBlocker, Estrogen, Metformin, Zinc,
    Magnesium, Omega3, B12, Caffeine, Melatonin,
  ];

  /// <summary>The name a person reads for a tag: "Vitamin D", "Omega-3", "GLP-1". An unknown tag reads as its words.</summary>
  public static string DisplayName(string tag) => tag switch
  {
    Testosterone => "Testosterone",
    Statin => "Statin",
    Thyroid => "Thyroid hormone",
    Biotin => "Biotin",
    Iron => "Iron",
    VitaminD => "Vitamin D",
    Creatine => "Creatine",
    Glp1 => "GLP-1",
    BetaBlocker => "Beta blocker",
    Estrogen => "Estrogen",
    Metformin => "Metformin",
    Zinc => "Zinc",
    Magnesium => "Magnesium",
    Omega3 => "Omega-3",
    B12 => "Vitamin B12",
    Caffeine => "Caffeine",
    Melatonin => "Melatonin",
    _ => tag.Replace('_', ' '),
  };
}

/// <summary>The heaviest set of one lift on one day. One row per (person, day, lift).</summary>
public class StrengthSet
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }
  public DateOnly Day { get; set; }

  /// <summary>A key from <see cref="Lifts"/> or the person's own short name.</summary>
  public string Lift { get; set; } = string.Empty;

  public double WeightKg { get; set; }
  public short Reps { get; set; }

  /// <summary>How hard it felt, 1 to 10. Optional.</summary>
  public double? Rpe { get; set; }

  public DateTime CreatedAt { get; set; }
}

public static class Lifts
{
  public const string Squat = "squat";
  public const string Deadlift = "deadlift";
  public const string BenchPress = "bench_press";
  public const string OverheadPress = "overhead_press";
  public const string Row = "row";
  public const string PullUp = "pull_up";
  public static readonly string[] Common = [Squat, Deadlift, BenchPress, OverheadPress, Row, PullUp];

  public static string DisplayName(string lift) => lift switch
  {
    Squat => "Squat",
    Deadlift => "Deadlift",
    BenchPress => "Bench press",
    OverheadPress => "Overhead press",
    Row => "Row",
    PullUp => "Pull-up",
    _ => lift.Length == 0 ? lift : char.ToUpperInvariant(lift[0]) + lift[1..].Replace('_', ' '),
  };
}
