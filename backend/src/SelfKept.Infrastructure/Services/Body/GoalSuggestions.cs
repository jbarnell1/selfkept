using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// Goals a person could pick, with starting numbers worked out from their own readings
/// (docs/body-design.md, "Suggestions"). Every guidance figure quoted here has a source that was
/// opened and is listed in docs/body-research.md; a default with no source behind it (how often
/// to lift) is offered as a plain starting point and claims nothing. A suggestion is only ever
/// shown to the person themself, and nothing is saved until they choose it.
/// </summary>
public static class GoalSuggestions
{
  /// <summary>Morton 2018: added protein stopped adding lean mass at about 1.6 g per kg a day.</summary>
  public const double ProteinGramsPerKg = 1.6;

  /// <summary>Helms 2014: 0.5 to 1 % of body weight a week, from a review for lean, trained people.</summary>
  public const double LossShareLow = 0.005;
  public const double LossShareHigh = 0.01;

  /// <summary>Watson 2015: adults should sleep 7 or more hours a night.</summary>
  public const double SleepMinutes = 420;

  public const double LiftingDaysPerWeek = 2;

  private const string ProteinWhy =
    "In a pooled analysis of training studies, eating more protein stopped adding muscle at about 1.6 g per kg of body weight a day.";

  private const string SleepWhy = "Sleep researchers' consensus for adults is 7 or more hours a night.";

  public static IReadOnlyList<GoalSuggestionDto> For(BodySnapshotDto body, UnitSystem units)
  {
    var weight = body.TrendWeightKg ?? body.WeightKg;
    var list = new List<GoalSuggestionDto>
    {
      new(
        GoalKinds.LoseFat,
        GoalKinds.DisplayName(GoalKinds.LoseFat),
        weight is { } w
          ? "A review written for lean, trained people suggests losing about 0.5 to 1 % of body weight a week to hold on to muscle. " +
            $"For you that is {BodyUnits.Format(w * LossShareLow, "kg", units)} to {BodyUnits.Format(w * LossShareHigh, "kg", units)} a week. " +
            ProteinWhy
          : "A review written for lean, trained people suggests losing about 0.5 to 1 % of body weight a week to hold on to muscle. " +
            "Add a weight reading to see what that is for you.",
        MetricKeys.WeightKg,
        Round(weight),
        [.. Protein(weight), Lifting(), Sleep()]),
      new(
        GoalKinds.GainMuscle,
        GoalKinds.DisplayName(GoalKinds.GainMuscle),
        ProteinWhy + " Regular lifting and enough sleep do the rest; the strength log shows whether it is working.",
        MetricKeys.WeightKg,
        Round(weight),
        [.. Protein(weight), Lifting(), Sleep()]),
      new(
        GoalKinds.KeepStrength,
        GoalKinds.DisplayName(GoalKinds.KeepStrength),
        "Log one top set per lift and the estimated best shows whether strength is holding while other things change.",
        null,
        null,
        [Lifting(), .. Protein(weight)]),
      new(
        GoalKinds.SleepBetter,
        GoalKinds.DisplayName(GoalKinds.SleepBetter),
        SleepWhy + " Noting late caffeine in the journal lets the patterns page compare those nights with the rest.",
        MetricKeys.SleepStage,
        null,
        [
          Sleep(),
          new SuggestedTargetDto("No late caffeine", TargetKinds.Journal, null, JournalTags.LateCaffeine, TargetComparators.AtMost, 0, TargetPeriods.Week, "entries"),
        ]),
      new(
        GoalKinds.Maintain,
        GoalKinds.DisplayName(GoalKinds.Maintain),
        "Holding steady is a goal too. The trend line smooths out day to day swings, so you can see drift early.",
        MetricKeys.WeightKg,
        Round(weight),
        [Lifting(), Sleep()]),
    };
    return list;
  }

  /// <summary>The daily protein figure for a body weight, rounded to the nearest 5 g.</summary>
  public static double ProteinTarget(double weightKg) => Math.Round(weightKg * ProteinGramsPerKg / 5, MidpointRounding.AwayFromZero) * 5;

  private static IEnumerable<SuggestedTargetDto> Protein(double? weightKg)
  {
    if (weightKg is not { } w) yield break;
    yield return new SuggestedTargetDto("Protein", TargetKinds.Metric, MetricKeys.ProteinG, null, TargetComparators.AtLeast, ProteinTarget(w), TargetPeriods.Day, "g");
  }

  private static SuggestedTargetDto Lifting() =>
    new("Lifting days", TargetKinds.Strength, null, null, TargetComparators.AtLeast, LiftingDaysPerWeek, TargetPeriods.Week, "days");

  private static SuggestedTargetDto Sleep() =>
    new("Sleep", TargetKinds.Metric, MetricKeys.SleepStage, null, TargetComparators.AtLeast, SleepMinutes, TargetPeriods.Day, "min");

  private static double? Round(double? value) => value is { } v ? Math.Round(v, 1) : null;
}
