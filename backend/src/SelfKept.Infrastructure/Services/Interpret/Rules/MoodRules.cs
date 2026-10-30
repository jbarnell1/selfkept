using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>Check-in mood at 1 or 2 (daily average at or below 2) on 3 of the last 5 days. Watch.</summary>
public sealed class MoodLowRunRule : RuleBase
{
  public const int LookbackDays = 5;
  public const int LowDaysRequired = 3;
  public const double LowMood = 2;

  public override string Kind => InsightKinds.MoodLowRun;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [MetricKeys.CheckinMood];

  public static bool Fires(RuleContext context, out double[] lowDays)
  {
    var days = context.LastValues(MetricKeys.CheckinMood, LookbackDays);
    lowDays = Present(days).Where(v => v <= LowMood).ToArray();
    return lowDays.Length >= LowDaysRequired;
  }

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (context.BaselineFor(MetricKeys.CheckinMood) == null) return null;
    if (!Fires(context, out var lowDays)) return null;
    return Candidate(InsightSeverities.Watch, MetricsUsed(context),
      "Three low days this week. If sleep has been short too, that is the usual link; if not, it may be worth talking to someone.",
      LowDaysRequired - lowDays.Average(),
      ("lowDays", lowDays.Select(v => Round(v)).ToArray()), ("days", IsoDays(context, Enumerable.Range(0, LookbackDays).Reverse())));
  }
}

/// <summary>mood_low_run and sleep_debt both active. Watch.</summary>
public sealed class MoodSleepLinkRule : RuleBase
{
  public override string Kind => InsightKinds.MoodSleepLink;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [MetricKeys.CheckinMood, MetricKeys.SleepStage];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (context.BaselineFor(MetricKeys.CheckinMood) == null) return null;
    if (!MoodLowRunRule.Fires(context, out var lowDays)) return null;
    if (!SleepDebtRule.Fires(context, out var shortNights, out var sleepBaseline)) return null;
    var sleepDistance = BaselineMath.BandDistance(shortNights.Average(), sleepBaseline!.P25_28!.Value, sleepBaseline.P75_28!.Value, sleepBaseline.P25_28.Value, sleepBaseline.P75_28.Value);
    return Candidate(InsightSeverities.Watch, MetricsUsed(context),
      "Low mood and short sleep have lined up this week.",
      sleepDistance + 1,
      ("lowMoodDays", lowDays.Select(v => Round(v)).ToArray()), ("shortNightsMinutes", shortNights.Select(v => Round(v, 0)).ToArray()), ("sleepP25Minutes", Round(sleepBaseline.P25_28.Value, 0)));
  }
}
