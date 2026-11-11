using SelfKept.Core.Entities;
using SelfKept.Core.Formatting;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Body;
using SelfKept.Infrastructure.Services.ContextEngine;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

// The rules that read context, the day log and the targets (docs/context-design.md, "Rules").
// A figure from the literature appears only when docs/context-design.md lists it as checked;
// every other number here is a plain default, named as a starting point.

/// <summary>
/// The overnight readings that move together at the start of an illness, each against the
/// person's own 28-day baseline. Breathing rate is one of these signals and is never a card of
/// its own: a rise alone says nothing (most ill people in the source showed none).
/// </summary>
public static class EarlySignals
{
  /// <summary>Nights of baseline a reading needs before it counts. The proposal's own floor, a starting point.</summary>
  public const int MinBaselineNights = 21;

  /// <summary>
  /// A sleeping breathing rate this far above the person's usual counts as up. Natarajan 2021
  /// (npj Digit Med 4:136) reported readings 3 or more breaths a minute above people's usual.
  /// </summary>
  public const double BreathingRiseBpm = 3;

  public sealed record Signal(string Metric, string Words, double Value, double Usual);

  /// <summary>Last night's signals: resting heart rate above p90, HRV below p10, skin temperature above p90, breathing 3 or more above the median.</summary>
  public static IReadOnlyList<Signal> Read(RuleContext context)
  {
    var signals = new List<Signal>();
    if (Ready(context, MetricKeys.RestingHrBpm, out var rhr, out var rhrBase) && rhrBase.P90_28 is { } rhrP90 && rhr > rhrP90)
      signals.Add(new Signal(MetricKeys.RestingHrBpm, "resting heart rate is up", rhr, rhrBase.Median28 ?? rhrP90));
    if (Ready(context, context.HrvMetricKey, out var hrv, out var hrvBase) && hrvBase.P10_28 is { } hrvP10 && hrv < hrvP10)
      signals.Add(new Signal(context.HrvMetricKey, "HRV is down", hrv, hrvBase.Median28 ?? hrvP10));
    if (Ready(context, MetricKeys.WristTempDeltaC, out var temp, out var tempBase) && tempBase.P90_28 is { } tempP90 && temp > tempP90)
      signals.Add(new Signal(MetricKeys.WristTempDeltaC, "skin temperature is up", temp, tempBase.Median28 ?? tempP90));
    if (BreathingRateUp(context) is { } breathing) signals.Add(breathing);
    return signals;
  }

  /// <summary>Breathing rate 3 or more above the usual. Only ever read as part of the early signs.</summary>
  public static Signal? BreathingRateUp(RuleContext context) =>
    Ready(context, MetricKeys.RespiratoryRateBpm, out var value, out var baseline) && baseline.Median28 is { } median && value >= median + BreathingRiseBpm
      ? new Signal(MetricKeys.RespiratoryRateBpm, "breathing rate is up", value, median)
      : null;

  private static bool Ready(RuleContext context, string metric, out double value, out Baseline baseline)
  {
    value = 0;
    baseline = null!;
    if (context.BaselineFor(metric) is not { } b || b.N28 < MinBaselineNights) return false;
    if (context.ValueOn(metric, context.Day) is not { } v) return false;
    value = v;
    baseline = b;
    return true;
  }

  public static string Join(IEnumerable<Signal> signals)
  {
    var words = signals.Select(s => s.Words).ToList();
    return words.Count switch
    {
      0 => string.Empty,
      1 => words[0],
      _ => string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1],
    };
  }

  public static (string Key, object? Value)[] Evidence(IEnumerable<Signal> signals) =>
    signals.Select(s => (s.Metric, (object?)new Dictionary<string, object?> { ["value"] = Math.Round(s.Value, 2), ["usual"] = Math.Round(s.Usual, 2) })).ToArray();

  /// <summary>The cold-like symptom logged most recently in the last three days, with its day.</summary>
  public static (DateOnly Day, string Tag)? ColdSymptom(RuleContext context)
  {
    for (var i = 0; i < ContextDerivation.SymptomDays; i++)
    {
      var day = context.Day.AddDays(-i);
      var tag = context.Context.TagsOn(day).FirstOrDefault(t => JournalTags.ColdLike.Contains(t));
      if (tag != null) return (day, tag);
    }
    return null;
  }
}

/// <summary>
/// Two or more overnight readings off together against the person's own baseline, with no cold
/// symptom logged. Notice: readings alone say "unusual for you", never "ill". The study figures
/// are Mason 2022 (Sci Rep 12:3463): sensitivity 82 %, specificity 63 %.
/// </summary>
public sealed class EarlySignsRule : RuleBase
{
  /// <summary>How many signals have to be off together. A starting point, not a published threshold.</summary>
  public const int MinSignals = 2;

  public override string Kind => InsightKinds.EarlySigns;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var signals = EarlySignals.Read(context);
    if (signals.Count < MinSignals || EarlySignals.ColdSymptom(context) != null) return null;
    var candidate = Candidate(InsightSeverities.Notice, signals.Select(s => s.Metric).ToList(),
      $"Your readings look unusual for you last night: {EarlySignals.Join(signals)}. A drink, stress, travel or a hard day can look the same, so it is a pattern worth watching. "
      + "In one study of ring wearers, readings like these picked up 82 % of infections but also flagged 37 % of people who were not ill (Mason 2022).",
      signals.Count,
      [.. EarlySignals.Evidence(signals), ("signals", signals.Count), ("day", context.Day.ToIso())]);
    return ContextNotes.Explain(context, candidate, demote: true, ContextNotes.IllnessLike);
  }
}

/// <summary>
/// A cold-like symptom logged in the last three days with at least one overnight reading off. Watch.
/// The card says what was seen and that it fits the start of a cold; it never says "you are ill".
/// </summary>
public sealed class ColdStartingRule : RuleBase
{
  public override string Kind => InsightKinds.ColdStarting;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (EarlySignals.ColdSymptom(context) is not { } symptom) return null;
    var signals = EarlySignals.Read(context);
    if (signals.Count == 0) return null;
    var candidate = Candidate(InsightSeverities.Watch, signals.Select(s => s.Metric).ToList(),
      $"You logged {ContextDerivation.TagPhrase(symptom.Tag)} on {LabRules.Spoken(symptom.Day)}, and last night {EarlySignals.Join(signals)}. "
      + "Together these can fit the start of a cold. It is a pattern worth watching: an easier day helps, and if you feel very unwell or it gets worse, talk to a clinician.",
      signals.Count + 1,
      [.. EarlySignals.Evidence(signals), ("symptom", symptom.Tag), ("symptomDay", symptom.Day.ToIso()), ("signals", signals.Count)]);
    return ContextNotes.Explain(context, candidate, demote: true, ContextKinds.CyclePhase, ContextKinds.Travel);
  }
}

/// <summary>
/// Yesterday to last night: a day log tag yesterday (alcohol, late caffeine, a late meal, a hard
/// session or other hard training) beside a night outside the person's usual range (below p10 or
/// above p90). Notice, said as an observation, never as a cause.
/// </summary>
public sealed class YesterdayLastNightRule : RuleBase
{
  /// <summary>The tags whose effect shows overnight, in the order a card speaks of them.</summary>
  public static readonly string[] Tags = [JournalTags.Alcohol, JournalTags.LateCaffeine, JournalTags.LateMeal, JournalTags.HardSession];

  public const string HardTraining = "hard_training";

  public override string Kind => InsightKinds.YesterdayLastNight;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var yesterday = context.Day.AddDays(-1);
    var tags = context.Context.TagsOn(yesterday);
    var tag = Tags.FirstOrDefault(tags.Contains);
    if (tag == null && context.Context.HardTrainingDays.Contains(yesterday)) tag = HardTraining;
    if (tag == null) return null;

    var off = new List<(string Metric, double Value, double P10, double P90)>();
    foreach (var metric in new[] { MetricKeys.SleepStage, context.HrvMetricKey, MetricKeys.RestingHrBpm })
    {
      if (context.BaselineFor(metric) is not { P10_28: { } p10, P90_28: { } p90 } b || b.N28 < EarlySignals.MinBaselineNights) continue;
      if (context.ValueOn(metric, context.Day) is { } v && (v < p10 || v > p90)) off.Add((metric, v, p10, p90));
    }
    if (off.Count == 0) return null;

    var lead = off[0];
    var units = context.Person.Units;
    var unit = lead.Metric == MetricKeys.SleepStage ? "min" : Unit(lead.Metric);
    var what = lead.Metric == MetricKeys.SleepStage ? "you slept" : $"your {PatternMath.MetricWords(lead.Metric)} was";
    var side = lead.Value < lead.P10 ? "below" : "above";
    var text = $"You logged {Words(tag)} yesterday, and last night {what} {BodyUnits.Format(lead.Value, unit, units, 0)}, {side} your usual range "
      + $"({BodyUnits.Format(lead.P10, unit, units, 0)} to {BodyUnits.Format(lead.P90, unit, units, 0)}). Nights like this are what your patterns compare over time.";
    if (tag == JournalTags.LateCaffeine && lead.Metric == MetricKeys.SleepStage && lead.Value < lead.P10)
    {
      text += " A review of trials found caffeine cut total sleep by about 45 minutes, and coffee still cost sleep when drunk up to 8.8 hours before bed (Gardiner 2023).";
    }
    var candidate = Candidate(InsightSeverities.Notice, off.Select(o => o.Metric).ToList(), text, off.Count,
      ("tag", tag), ("day", yesterday.ToIso()),
      ("readings", off.Select(o => new Dictionary<string, object?> { ["metric"] = o.Metric, ["value"] = Round(o.Value, 1), ["p10"] = Round(o.P10, 1), ["p90"] = Round(o.P90, 1) }).ToArray()));
    return ContextNotes.Explain(context, candidate, demote: true, ContextKinds.Illness, ContextKinds.Travel);
  }

  private static string Words(string tag) => tag == HardTraining ? "hard training" : ContextDerivation.TagPhrase(tag);

  private static string Unit(string metric) => PatternMath.Unit(metric);

}

/// <summary>
/// A target slipping: a daily target newly missed on 4 of the last 7 days that have an answer, or,
/// on a Sunday, a weekly target missed this week and the week before. Notice, with a question
/// rather than a red mark. Quiet for a target the Sunday target_week card already speaks about,
/// and while illness is logged. The 4 of 7 and the two weeks are the proposal's, a starting point.
/// </summary>
public sealed class TargetSlippingRule : BodyRuleBase
{
  public const int MinMissedDays = 4;

  public override string Kind => InsightKinds.TargetSlipping;

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var state = context.Context;
    if (state.Has(ContextKinds.Illness)) return null;
    var spoken = context.Day.DayOfWeek == DayOfWeek.Sunday && new TargetWeekRule().Evaluate(context) is { } week
      ? week.Evidence.GetValueOrDefault("label") as string
      : null;

    var slipping = state.Targets
      .Where(t => t.Period == TargetPeriods.Day && t.MissedDays >= MinMissedDays && t.Label != spoken)
      .Where(t => state.TargetsYesterday.FirstOrDefault(y => y.Id == t.Id) is not { } before || before.MissedDays < MinMissedDays)
      .OrderByDescending(t => t.MissedDays)
      .FirstOrDefault();
    if (slipping != null)
    {
      return Candidate(InsightSeverities.Notice, [],
        $"\"{slipping.Label}\" was missed on {slipping.MissedDays} of the last {slipping.KnownDays} days that have an answer. What got in the way? If it does not fit, change it.",
        slipping.MissedDays / 7.0,
        ("targetId", slipping.Id), ("label", slipping.Label), ("period", slipping.Period), ("missedDays", slipping.MissedDays), ("knownDays", slipping.KnownDays));
    }

    if (context.Day.DayOfWeek != DayOfWeek.Sunday) return null;
    var weekly = state.Targets.FirstOrDefault(t =>
      t.Period == TargetPeriods.Week && t.Met == false && t.Label != spoken
      && state.TargetsWeekAgo.FirstOrDefault(w => w.Id == t.Id) is { Met: false });
    if (weekly == null) return null;
    return Candidate(InsightSeverities.Notice, [],
      $"\"{weekly.Label}\" was missed this week and the week before. What got in the way? If it does not fit, change it.",
      0.5,
      ("targetId", weekly.Id), ("label", weekly.Label), ("period", weekly.Period), ("weeksMissed", 2));
  }
}

/// <summary>
/// Short sleep during a diet: a fat-loss goal and under 7 hours asleep on 4 or more of the last 7
/// nights, said on the night it first becomes true. Watch. Seven hours is the AASM and SRS
/// consensus (Watson 2015); "4 of 7" is a starting point.
/// </summary>
public sealed class ShortSleepDietRule : BodyRuleBase
{
  public const double ShortNightMinutes = 420;
  public const int MinShortNights = 4;
  public const int MinNights = 5;

  public override string Kind => InsightKinds.ShortSleepDiet;

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (!FatLoss(context) || context.Context.Has(ContextKinds.Illness)) return null;
    var (nights, shortNights) = Count(context, context.Day);
    if (nights < MinNights || shortNights < MinShortNights) return null;
    if (Count(context, context.Day.AddDays(-1)).Short >= MinShortNights) return null;
    return Candidate(InsightSeverities.Watch, [MetricKeys.SleepStage],
      $"Sleep was under 7 hours on {shortNights} of the last {nights} nights while you are working on losing fat. Sleep researchers' consensus for adults is 7 or more hours a night.",
      shortNights / 7.0,
      ("shortNights", shortNights), ("nights", nights), ("thresholdMinutes", ShortNightMinutes));
  }

  private static (int Nights, int Short) Count(RuleContext context, DateOnly end)
  {
    var values = Enumerable.Range(0, 7).Select(i => context.ValueOn(MetricKeys.SleepStage, end.AddDays(-i))).Where(v => v != null).Select(v => v!.Value).ToList();
    return (values.Count, values.Count(v => v < ShortNightMinutes));
  }

  public static bool FatLoss(RuleContext context) => context.Body.Goals.Any(g => g.Goal.Kind is GoalKinds.LoseFat or GoalKinds.Recomposition);
}

/// <summary>
/// Recovery strain: hard training yesterday or the day before, and HRV below the person's usual
/// range last night. Notice, and the card says this is expected: Stanley 2013 (Sports Med
/// 43:1259) found cardiac autonomic recovery takes 24 to 48 hours after threshold-intensity
/// exercise and at least 48 hours after high-intensity exercise. Quiet while illness is logged.
/// </summary>
public sealed class RecoveryStrainRule : RuleBase
{
  /// <summary>Days back a hard session still explains a low HRV: the 48 hours of the source.</summary>
  public const int WindowDays = 2;

  public override string Kind => InsightKinds.RecoveryStrain;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [context.HrvMetricKey];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (context.Context.Has(ContextKinds.Illness)) return null;
    if (context.BaselineFor(context.HrvMetricKey) is not { P25_28: { } p25 } baseline) return null;
    if (context.ValueOn(context.HrvMetricKey, context.Day) is not { } hrv || hrv >= p25) return null;
    var daysAgo = Enumerable.Range(1, WindowDays).FirstOrDefault(i => context.Context.HardTrainingDays.Contains(context.Day.AddDays(-i)));
    if (daysAgo == 0) return null;

    var when = daysAgo == 1 ? "a day" : "two days";
    return Candidate(InsightSeverities.Notice, [context.HrvMetricKey],
      $"HRV was below your usual range last night, {when} after hard training. That is expected: a review found the heart's recovery takes 24 to 48 hours after threshold-intensity sessions and at least 48 hours after high-intensity ones (Stanley 2013).",
      (p25 - hrv) / Iqr(baseline),
      ("hrv", Round(hrv)), ("p25", Round(p25)), ("trainingDay", context.Day.AddDays(-daysAgo).ToIso()));
  }
}

/// <summary>
/// Strength on track: during a fat-loss goal, the estimated best of a lift (Epley) holding at 97 %
/// or more of where it stood when the goal started, with a set in the last week. Said once a week
/// on the goal's weekday, from two weeks in. Notice. The 97 % is a starting point.
/// </summary>
public sealed class StrengthOnTrackRule : BodyRuleBase
{
  public const double HoldingShare = 0.97;
  public const int RecentDays = 7;

  public override string Kind => InsightKinds.StrengthOnTrack;

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (context.Context.Has(ContextKinds.Illness)) return null;
    var goal = context.Body.Goals.Select(g => g.Goal).FirstOrDefault(g => g.Kind is GoalKinds.LoseFat or GoalKinds.Recomposition && WeeklyCheck(g, context.Day));
    if (goal == null) return null;

    var best = context.Context.Strength
      .Where(s => s.Day >= goal.StartedOn && s.Day <= context.Day)
      .GroupBy(s => s.Lift, StringComparer.Ordinal)
      .Select(g => g.OrderBy(s => s.Day).ToList())
      .Where(sets => sets.Count >= 2 && context.Day.DayNumber - sets[^1].Day.DayNumber < RecentDays && sets[^1].Day > sets[0].Day)
      .Select(sets => (First: sets[0], Last: sets[^1], Ratio: sets[^1].EstimatedMaxKg / sets[0].EstimatedMaxKg))
      .Where(x => x.Ratio >= HoldingShare)
      .OrderByDescending(x => x.Ratio)
      .FirstOrDefault();
    if (best.First == null) return null;

    var units = context.Person.Units;
    var verb = best.Ratio > 1 ? "rising" : "holding";
    return Candidate(InsightSeverities.Notice, [],
      $"Your estimated best {best.Last.LiftName.ToLowerInvariant()} is {BodyUnits.Format(best.Last.EstimatedMaxKg, "kg", units)}, against {BodyUnits.Format(best.First.EstimatedMaxKg, "kg", units)} when your goal to {GoalName(goal)} started: strength is {verb} while you lose fat.",
      best.Ratio,
      ("lift", best.Last.Lift), ("firstKg", Round(best.First.EstimatedMaxKg)), ("firstDay", best.First.Day.ToIso()), ("latestKg", Round(best.Last.EstimatedMaxKg)), ("latestDay", best.Last.Day.ToIso()));
  }
}

/// <summary>
/// Mood during a deficit: a fat-loss goal with weight coming down, and check-in mood over the last
/// two weeks a point or more below the two weeks before, with check-ins on 21 of the 28 days.
/// Said once a week on the goal's weekday. Watch. The point, the fortnights and the 21 days are
/// the proposal's, a starting point. Quiet while illness is logged.
/// </summary>
public sealed class MoodDeficitRule : BodyRuleBase
{
  public const double MinDrop = 1;
  public const int MinDays = 21;
  public const int MinDaysPerHalf = 7;

  public override string Kind => InsightKinds.MoodDeficit;

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (context.Context.Has(ContextKinds.Illness)) return null;
    var goal = context.Body.Goals.Select(g => g.Goal).FirstOrDefault(g => g.Kind is GoalKinds.LoseFat or GoalKinds.Recomposition && WeeklyCheck(g, context.Day));
    if (goal == null || context.Body.WeeklyRateKg is not < 0) return null;

    var values = context.LastValues(MetricKeys.CheckinMood, BaselineService.WindowDays);
    if (values.Count(v => v != null) < MinDays) return null;
    var before = Present(values[..14]);
    var recent = Present(values[14..]);
    if (before.Length < MinDaysPerHalf || recent.Length < MinDaysPerHalf) return null;
    var (was, now) = (before.Average(), recent.Average());
    if (was - now < MinDrop) return null;

    return Candidate(InsightSeverities.Watch, [MetricKeys.CheckinMood],
      $"Mood at check-ins averaged {Format(now)} over the last two weeks, against {Format(was)} the two weeks before, while your weight has been coming down. "
      + "It is a pattern worth watching. If it stays low, an easier pace or a talk with someone may help.",
      was - now,
      ("recentMean", Round(now, 2)), ("previousMean", Round(was, 2)), ("days", values.Count(v => v != null)), ("weeklyRateKg", Round(context.Body.WeeklyRateKg!.Value, 2)));
  }
}

/// <summary>
/// A personal pattern that became established today: nights after a tag differ from the rest by
/// more than the noise line and by more than chance alone would explain (a rank test), with at
/// least 8 nights with and 16 without (our own floor; no published standard exists). Notice, said
/// once, always "a pattern, not proof".
/// </summary>
public sealed class PersonalPatternRule : RuleBase
{
  public override string Kind => InsightKinds.PersonalPattern;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var pattern = context.Context.NewPatterns
      .OrderByDescending(p => p.NoiseLine > 0 ? Math.Abs(p.Difference) / p.NoiseLine : Math.Abs(p.Difference))
      .FirstOrDefault();
    if (pattern == null) return null;
    return Candidate(InsightSeverities.Notice, [pattern.MetricKey],
      "A new pattern in your own data. " + PatternMath.Sentence(pattern),
      pattern.NoiseLine > 0 ? Math.Abs(pattern.Difference) / pattern.NoiseLine : 1,
      ("tag", pattern.Tag), ("metric", pattern.MetricKey), ("nightsWith", pattern.NightsWith), ("nightsWithout", pattern.NightsWithout),
      ("medianWith", pattern.MedianWith), ("medianWithout", pattern.MedianWithout), ("difference", pattern.Difference), ("overlap", pattern.Overlap));
  }
}
