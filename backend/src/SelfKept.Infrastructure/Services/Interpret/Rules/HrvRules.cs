using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>HRV below p10_28 for 3 consecutive nights. Watch.</summary>
public sealed class HrvLowRunRule : RuleBase
{
  public const int Nights = 3;

  public override string Kind => InsightKinds.HrvLowRun;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [context.HrvMetricKey];

  /// <summary>True when the three nights ending on <paramref name="endDay"/> all sat below p10.</summary>
  public static bool RunEndsOn(RuleContext context, DateOnly endDay, out double[] values)
  {
    values = [];
    var baseline = context.BaselineFor(context.HrvMetricKey);
    if (baseline?.P10_28 is not { } p10) return false;
    var nights = new double?[Nights];
    for (var i = 0; i < Nights; i++) nights[i] = context.ValueOn(context.HrvMetricKey, endDay.AddDays(-(Nights - 1 - i)));
    if (nights.Any(v => v == null)) return false;
    values = Present(nights);
    return values.All(v => v < p10);
  }

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (!RunEndsOn(context, context.Day, out var values)) return null;
    var baseline = context.BaselineFor(context.HrvMetricKey)!;
    var distance = BaselineMath.BandDistance(values.Average(), baseline.P10_28!.Value, baseline.P90_28!.Value, baseline.P25_28!.Value, baseline.P75_28!.Value);
    return Candidate(InsightSeverities.Watch, MetricsUsed(context),
      "HRV has sat below your usual range for three nights. That often follows poor sleep, a hard training block or the start of an illness.",
      distance,
      ("metric", context.HrvMetricKey), ("nights", values.Select(v => Round(v)).ToArray()), ("p10", Round(baseline.P10_28.Value)), ("days", IsoDays(context, [2, 1, 0])));
  }
}

/// <summary>HRV back above p25_28 the night after an hrv_low_run. Notice.</summary>
public sealed class HrvRecoveredRule : RuleBase
{
  public override string Kind => InsightKinds.HrvRecovered;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [context.HrvMetricKey];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var baseline = context.BaselineFor(context.HrvMetricKey);
    if (baseline?.P25_28 is not { } p25) return null;
    var today = context.ValueOn(context.HrvMetricKey, context.Day);
    if (today is not { } value || value <= p25) return null;
    if (!HrvLowRunRule.RunEndsOn(context, context.Day.AddDays(-1), out var lowNights)) return null;
    return Candidate(InsightSeverities.Notice, MetricsUsed(context),
      "HRV is back inside your normal range.",
      0,
      ("metric", context.HrvMetricKey), ("value", Round(value)), ("p25", Round(p25)), ("lowNights", lowNights.Select(v => Round(v)).ToArray()));
  }
}
