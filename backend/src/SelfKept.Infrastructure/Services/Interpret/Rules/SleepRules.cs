using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>Nightly minutes asleep below p25_28 on 3 of the last 4 nights. Watch.</summary>
public sealed class SleepDebtRule : RuleBase
{
  public const int LookbackNights = 4;
  public const int ShortNightsRequired = 3;

  public override string Kind => InsightKinds.SleepDebt;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [MetricKeys.SleepStage];

  public static bool Fires(RuleContext context, out double[] shortNights, out Baseline? baseline)
  {
    shortNights = [];
    baseline = context.BaselineFor(MetricKeys.SleepStage);
    if (baseline?.P25_28 is not { } p25) return false;
    var nights = context.LastValues(MetricKeys.SleepStage, LookbackNights);
    shortNights = Present(nights).Where(v => v < p25).ToArray();
    return shortNights.Length >= ShortNightsRequired;
  }

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (!Fires(context, out var shortNights, out var baseline)) return null;
    var distance = BaselineMath.BandDistance(shortNights.Average(), baseline!.P25_28!.Value, baseline.P75_28!.Value, baseline.P25_28.Value, baseline.P75_28.Value);
    return Candidate(InsightSeverities.Watch, MetricsUsed(context),
      "Three short nights in four. Sleep debt shows up in HRV and mood within days.",
      distance,
      ("shortNightsMinutes", shortNights.Select(v => Round(v, 0)).ToArray()), ("p25Minutes", Round(baseline.P25_28.Value, 0)), ("days", IsoDays(context, [3, 2, 1, 0])));
  }
}

/// <summary>7-day median asleep above the 28-day median by 20 minutes or more, with at least 4 of the 7 nights present. Notice.</summary>
public sealed class SleepGoodWeekRule : RuleBase
{
  public const double MarginMinutes = 20;
  public const int MinNights = 4;

  public override string Kind => InsightKinds.SleepGoodWeek;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [MetricKeys.SleepStage];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var baseline = context.BaselineFor(MetricKeys.SleepStage);
    if (baseline is not { Median7: { } median7, Median28: { } median28 } || baseline.N7 < MinNights) return null;
    if (median7 < median28 + MarginMinutes) return null;
    return Candidate(InsightSeverities.Notice, MetricsUsed(context),
      "A better week of sleep than your usual.",
      (median7 - median28) / Iqr(baseline),
      ("median7Minutes", Round(median7, 0)), ("median28Minutes", Round(median28, 0)), ("nights", baseline.N7));
  }
}
