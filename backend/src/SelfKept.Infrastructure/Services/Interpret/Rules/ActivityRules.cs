using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>7-day median steps below half of median28 with n7 >= 5. Notice.</summary>
public sealed class ActivityDipRule : RuleBase
{
  public const int MinDays = 5;

  public override string Kind => InsightKinds.ActivityDip;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [MetricKeys.Steps];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var baseline = context.BaselineFor(MetricKeys.Steps);
    if (baseline is not { Median7: { } median7, Median28: { } median28 } || baseline.N7 < MinDays || median28 <= 0) return null;
    if (median7 >= median28 / 2) return null;
    return Candidate(InsightSeverities.Notice, MetricsUsed(context),
      "A quieter week than your norm. Nothing wrong with that; just noting it.",
      (median28 / 2 - median7) / Iqr(baseline),
      ("median7Steps", Round(median7, 0)), ("median28Steps", Round(median28, 0)), ("days", baseline.N7));
  }
}

/// <summary>7-day median exercise minutes above 1.5 times median28, with at least 3 of the 7 days present. Notice.</summary>
public sealed class TrainingSpikeRule : RuleBase
{
  public const double Factor = 1.5;
  public const int MinDays = 3;

  public override string Kind => InsightKinds.TrainingSpike;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [MetricKeys.ExerciseMin];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var baseline = context.BaselineFor(MetricKeys.ExerciseMin);
    if (baseline is not { Median7: { } median7, Median28: { } median28 } || baseline.N7 < MinDays || median28 <= 0) return null;
    if (median7 <= median28 * Factor) return null;
    return Candidate(InsightSeverities.Notice, MetricsUsed(context),
      "Training load is well above your usual this week. Recovery markers will tell if it is too much.",
      (median7 - median28 * Factor) / Iqr(baseline),
      ("median7Minutes", Round(median7, 0)), ("median28Minutes", Round(median28, 0)), ("days", baseline.N7));
  }
}

/// <summary>slope28 of weight implies more than 1 kg of change over 28 days, with n28 >= 10. Notice.</summary>
public sealed class WeightTrendRule : RuleBase
{
  public const double ThresholdKg = 1.0;
  public const int MinDays = 10;

  public override string Kind => InsightKinds.WeightTrend;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [MetricKeys.WeightKg];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var baseline = context.BaselineFor(MetricKeys.WeightKg);
    if (baseline is not { Slope28: { } slope } || baseline.N28 < MinDays) return null;
    var delta = slope * (BaselineService.WindowDays - 1);
    if (Math.Abs(delta) <= ThresholdKg) return null;
    var direction = delta < 0 ? "down" : "up";
    var magnitude = Format(Math.Abs(delta));
    return Candidate(InsightSeverities.Notice, MetricsUsed(context),
      $"Weight has moved {direction} by about {magnitude} kg over the month.",
      (Math.Abs(delta) - ThresholdKg) / Iqr(baseline),
      ("deltaKg", Round(delta)), ("slopeKgPerDay", Round(slope, 3)), ("days", baseline.N28));
  }
}
