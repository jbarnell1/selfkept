using SelfKept.Core.Entities;
using SelfKept.Core.Formatting;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>Resting HR above p90_28 by 5 bpm or more on each of the last 2 nights. Watch.</summary>
public sealed class RhrElevatedRule : RuleBase
{
  public const int Nights = 2;
  public const double MarginBpm = 5;

  public override string Kind => InsightKinds.RhrElevated;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [MetricKeys.RestingHrBpm];

  public static bool Fires(RuleContext context, out double[] values, out Baseline? baseline)
  {
    values = [];
    baseline = context.BaselineFor(MetricKeys.RestingHrBpm);
    if (baseline?.P90_28 is not { } p90) return false;
    var nights = context.LastValues(MetricKeys.RestingHrBpm, Nights);
    if (nights.Any(v => v == null)) return false;
    values = Present(nights);
    return values.All(v => v >= p90 + MarginBpm);
  }

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (!Fires(context, out var values, out var baseline)) return null;
    var distance = BaselineMath.BandDistance(values.Average(), baseline!.P10_28!.Value, baseline.P90_28!.Value, baseline.P25_28!.Value, baseline.P75_28!.Value);
    return Candidate(InsightSeverities.Watch, MetricsUsed(context),
      "Resting heart rate is up on your normal. Worth an easier day.",
      distance,
      ("nights", values.Select(v => Round(v)).ToArray()), ("p90", Round(baseline.P90_28.Value)), ("days", IsoDays(context, [1, 0])));
  }
}

/// <summary>
/// rhr_elevated plus wrist temperature above p90_28 the same night, or respiratory rate above
/// p90_28 too. Watch. Reads only the companion metrics the person actually has a baseline for.
/// </summary>
public sealed class IllnessPatternRule : RuleBase
{
  public override string Kind => InsightKinds.IllnessPattern;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context)
  {
    // Companions count only once their own baseline is established; a metric still short of 14
    // days is simply not consulted rather than blocking the rule.
    var used = new List<string> { MetricKeys.RestingHrBpm };
    if (context.BaselineFor(MetricKeys.WristTempDeltaC) is { IsEstablished: true }) used.Add(MetricKeys.WristTempDeltaC);
    if (context.BaselineFor(MetricKeys.RespiratoryRateBpm) is { IsEstablished: true }) used.Add(MetricKeys.RespiratoryRateBpm);
    return used;
  }

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (!RhrElevatedRule.Fires(context, out var rhr, out var rhrBaseline)) return null;

    var evidence = new List<(string, object?)> { ("restingHrNights", rhr.Select(v => Round(v)).ToArray()), ("restingHrP90", Round(rhrBaseline!.P90_28!.Value)) };
    var distance = BaselineMath.BandDistance(rhr.Average(), rhrBaseline.P10_28!.Value, rhrBaseline.P90_28.Value, rhrBaseline.P25_28!.Value, rhrBaseline.P75_28!.Value);
    var companions = 0;

    if (Above(context, MetricKeys.WristTempDeltaC, out var temp, out var tempP90))
    {
      companions++;
      evidence.Add(("wristTempDelta", Round(temp, 2)));
      evidence.Add(("wristTempP90", Round(tempP90, 2)));
    }
    if (Above(context, MetricKeys.RespiratoryRateBpm, out var resp, out var respP90))
    {
      companions++;
      evidence.Add(("respiratoryRate", Round(resp)));
      evidence.Add(("respiratoryRateP90", Round(respP90)));
    }
    if (companions == 0) return null;
    evidence.Add(("day", context.Day.ToIso()));

    return Candidate(InsightSeverities.Watch, MetricsUsed(context),
      "Heart rate, temperature and breathing are all up tonight. This pattern often goes with coming down with something.",
      distance + companions, evidence.ToArray());
  }

  private static bool Above(RuleContext context, string metricKey, out double value, out double p90)
  {
    value = 0;
    p90 = 0;
    var baseline = context.BaselineFor(metricKey);
    if (baseline is not { IsEstablished: true } || baseline.P90_28 is not { } threshold) return false;
    if (context.ValueOn(metricKey, context.Day) is not { } v) return false;
    value = v;
    p90 = threshold;
    return v > threshold;
  }
}

/// <summary>Nightly SpO2 average below 93 percent on 2 of the last 7 nights, the latest night among them. Clinician.</summary>
public sealed class Spo2LowRule : RuleBase
{
  public const double ThresholdPct = 93;
  public const int LookbackNights = 7;
  public const int NightsRequired = 2;

  public override string Kind => InsightKinds.Spo2Low;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [MetricKeys.Spo2Pct];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var baseline = context.BaselineFor(MetricKeys.Spo2Pct);
    if (baseline == null) return null;
    var nights = context.LastValues(MetricKeys.Spo2Pct, LookbackNights);
    if (nights[^1] is not { } latest || latest >= ThresholdPct) return null;
    var low = new List<(int Offset, double Value)>();
    for (var i = 0; i < nights.Length; i++)
    {
      if (nights[i] is { } v && v < ThresholdPct) low.Add((LookbackNights - 1 - i, v));
    }
    if (low.Count < NightsRequired) return null;

    var days = IsoDays(context, low.Select(l => l.Offset));
    var distance = (ThresholdPct - low.Min(l => l.Value)) / Iqr(baseline);
    return Candidate(InsightSeverities.Clinician, MetricsUsed(context),
      "Overnight oxygen has dipped on two nights. Worth raising with a clinician, especially with snoring or daytime tiredness. " +
      $"Bring your overnight SpO2 history and these dates: {string.Join("; ", low.Select(l => LabRules.Spoken(context.Day.AddDays(-l.Offset))))}.",
      distance,
      ("nights", low.Select(l => Round(l.Value)).ToArray()), ("threshold", ThresholdPct), ("days", days));
  }
}
