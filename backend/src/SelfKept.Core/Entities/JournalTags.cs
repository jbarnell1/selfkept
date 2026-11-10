namespace SelfKept.Core.Entities;

/// <summary>
/// The coarse tags the day log knows about. A tag is a plain fact about the day ("alcohol",
/// "late_meal"), never an amount. Chips on Today write them directly; the assistant picks them out
/// of what a person types. Targets and patterns count them, so the list changes rarely.
/// </summary>
public static class JournalTags
{
  // Food
  public const string HighProtein = "high_protein";
  public const string Vegetables = "vegetables";
  public const string Fruit = "fruit";
  public const string WholeGrains = "whole_grains";
  public const string Sweets = "sweets";
  public const string FriedFood = "fried_food";
  public const string Takeaway = "takeaway";
  public const string LateMeal = "late_meal";
  public const string SkippedMeal = "skipped_meal";
  public const string HomeCooked = "home_cooked";
  public const string Swap = "swap";

  // Drink
  public const string Alcohol = "alcohol";
  public const string Caffeine = "caffeine";
  public const string LateCaffeine = "late_caffeine";
  public const string SugaryDrink = "sugary_drink";
  public const string Water = "water";

  // Symptoms
  public const string Headache = "headache";
  public const string SoreThroat = "sore_throat";
  public const string Congestion = "congestion";
  public const string Cough = "cough";
  public const string Fever = "fever";
  public const string Fatigue = "fatigue";
  public const string Nausea = "nausea";
  public const string StomachUpset = "stomach_upset";
  public const string Soreness = "soreness";
  public const string Pain = "pain";
  public const string Stress = "stress";
  public const string LowMood = "low_mood";

  // Training
  public const string Lifted = "lifted";
  public const string Cardio = "cardio";
  public const string HardSession = "hard_session";
  public const string RestDay = "rest_day";
  public const string Walk = "walk";

  // Sleep
  public const string LateNight = "late_night";
  public const string Nap = "nap";
  public const string PoorSleep = "poor_sleep";
  public const string GoodSleep = "good_sleep";

  public static readonly string[] Food =
    [HighProtein, Vegetables, Fruit, WholeGrains, Sweets, FriedFood, Takeaway, LateMeal, SkippedMeal, HomeCooked, Swap];

  public static readonly string[] Drink = [Alcohol, Caffeine, LateCaffeine, SugaryDrink, Water];

  public static readonly string[] Symptom =
    [Headache, SoreThroat, Congestion, Cough, Fever, Fatigue, Nausea, StomachUpset, Soreness, Pain, Stress, LowMood];

  public static readonly string[] Training = [Lifted, Cardio, HardSession, RestDay, Walk];

  public static readonly string[] Sleep = [LateNight, Nap, PoorSleep, GoodSleep];

  /// <summary>The symptom tags that together read as a cold or similar bug starting.</summary>
  public static readonly string[] ColdLike = [SoreThroat, Congestion, Cough, Fever];

  public static IReadOnlyList<string> ForKind(string kind) => kind switch
  {
    JournalKinds.Food => Food,
    JournalKinds.Drink => Drink,
    JournalKinds.Symptom => Symptom,
    JournalKinds.Training => Training,
    JournalKinds.Sleep => Sleep,
    _ => [],
  };

  public static readonly string[] All = [.. Food, .. Drink, .. Symptom, .. Training, .. Sleep];

  /// <summary>How a tag reads on a chip: "late_meal" becomes "Late meal".</summary>
  public static string DisplayName(string tag) =>
    tag.Length == 0 ? tag : char.ToUpperInvariant(tag[0]) + tag[1..].Replace('_', ' ');

  /// <summary>The kind a known tag belongs to, or note for a tag the list does not know.</summary>
  public static string KindOf(string tag)
  {
    foreach (var kind in JournalKinds.All)
    {
      if (ForKind(kind).Contains(tag)) return kind;
    }
    return JournalKinds.Note;
  }
}
