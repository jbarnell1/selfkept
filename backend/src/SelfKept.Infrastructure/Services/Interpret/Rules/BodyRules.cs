using System.Globalization;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Body;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>
/// Shared by the body and goal rules (docs/body-design.md, "Rules"). Weigh-ins, waist readings and
/// goals do not arrive daily, so none of these rules asks for a 28-day baseline: each returns no
/// metrics from MetricsUsed and decides from the readings themselves. To keep a card from sitting
/// on Today for weeks, each fires on a fresh reading or once a week, never every day.
/// </summary>
public abstract class BodyRuleBase : RuleBase
{
  /// <summary>A reading counts as new on the day it arrives and the day after.</summary>
  public const int FreshDays = 1;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  protected static bool Fresh(IReadOnlyList<DatedValue> readings, DateOnly day) =>
    readings.Count > 0 && readings[^1].Day <= day && day.DayNumber - readings[^1].Day.DayNumber <= FreshDays;

  /// <summary>True once a week, on the weekday the goal started, from two weeks in.</summary>
  protected static bool WeeklyCheck(Goal goal, DateOnly day)
  {
    var days = day.DayNumber - goal.StartedOn.DayNumber;
    return days >= 14 && days % 7 == 0;
  }

  protected static string GoalName(Goal goal) => GoalKinds.DisplayName(goal.Kind).ToLowerInvariant();
}

/// <summary>
/// A fat-loss goal with weight coming down faster than 1 % of body weight a week, on a day with a
/// new weigh-in. Notice. The 0.5 to 1 % range is from Helms 2014, a review written for natural
/// bodybuilders preparing for a contest; the sentence says where it comes from.
/// </summary>
public sealed class LossRateFastRule : BodyRuleBase
{
  public const double MaxSharePerWeek = 0.01;

  public override string Kind => InsightKinds.LossRateFast;

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var body = context.Body;
    if (!body.Goals.Any(g => g.Goal.Kind is GoalKinds.LoseFat or GoalKinds.Recomposition)) return null;
    if (body.WeeklyRateKg is not { } rate || body.TrendWeightKg is not { } weight || weight <= 0) return null;
    if (!Fresh(body.Weight, context.Day)) return null;
    var share = -rate / weight;
    if (share <= MaxSharePerWeek) return null;

    var units = context.Person.Units;
    return Candidate(InsightSeverities.Notice, [MetricKeys.WeightKg],
      $"Weight is coming down by about {BodyUnits.Format(-rate, "kg", units)} a week, which is {Format(share * 100)} % of your body weight. "
      + "A review written for lean, trained people puts the pace that keeps muscle at about 0.5 to 1 % a week.",
      (share - MaxSharePerWeek) / MaxSharePerWeek,
      ("ratePerWeekKg", Round(rate, 2)), ("sharePerWeekPct", Round(share * 100, 2)), ("trendWeightKg", Round(weight)));
  }
}

/// <summary>A goal with a target that has been reached within the last week. Notice.</summary>
public sealed class GoalReachedRule : BodyRuleBase
{
  public override string Kind => InsightKinds.GoalReached;

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var standing = context.Body.Goals.FirstOrDefault(g =>
      g.Status == PaceStatuses.Reached && !g.ReachedAWeekAgo && g.Current != null
      && g.Goal.TargetValue is { } target && g.Goal.StartValue is { } start && start != target);
    if (standing is not { Current: { } now, MetricKey: { } key }) return null;

    var units = context.Person.Units;
    return Candidate(InsightSeverities.Notice, [key],
      $"You reached your goal to {GoalName(standing.Goal)}: {BodyUnits.Format(now, standing.Unit, units)} now, "
      + $"against a target of {BodyUnits.Format(standing.Goal.TargetValue!.Value, standing.Unit, units)}. "
      + "You can end it, or set the next one.",
      1,
      ("goalId", standing.Goal.Id), ("current", Round(now, 2)), ("target", Round(standing.Goal.TargetValue.Value, 2)));
  }
}

/// <summary>
/// A goal whose measure has moved away from its target over the last four weeks. Checked once a
/// week on the weekday the goal started, from two weeks in. Notice.
/// </summary>
public sealed class GoalOffCourseRule : BodyRuleBase
{
  public override string Kind => InsightKinds.GoalOffCourse;

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var standing = context.Body.Goals.FirstOrDefault(g =>
      g.Status == PaceStatuses.WrongWay && g.PacePerWeek != null && g.MetricKey != null && WeeklyCheck(g.Goal, context.Day));
    if (standing is not { PacePerWeek: { } pace, MetricKey: { } key }) return null;

    var units = context.Person.Units;
    var measure = (standing.MetricName ?? "The measure").Trim();
    return Candidate(InsightSeverities.Notice, [key],
      $"Your goal to {GoalName(standing.Goal)} is moving away from its target: {measure.ToLowerInvariant()} is "
      + $"{BodyUnits.Change(pace, standing.Unit, units, 2)} a week over the last four weeks. "
      + "A slower pace or a different target is a fair answer too.",
      1,
      ("goalId", standing.Goal.Id), ("pacePerWeek", Round(pace, 3)), ("current", standing.Current is { } now ? Round(now, 2) : (object?)null));
  }
}

/// <summary>
/// The week of one target, said on the last day of the week (Sunday): kept on at least five of
/// the days that are known, or missed on at least five. Unknown days count for neither. Notice.
/// </summary>
public sealed class TargetWeekRule : BodyRuleBase
{
  public const int MinDays = 5;

  public override string Kind => InsightKinds.TargetWeek;

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (context.Day.DayOfWeek != DayOfWeek.Sunday) return null;
    var days = context.Body.Targets.Where(t => t.Period == TargetPeriods.Day).ToList();

    var kept = days.Where(t => t.MetDays >= MinDays).OrderByDescending(t => t.MetDays).FirstOrDefault();
    if (kept != null)
    {
      return Candidate(InsightSeverities.Notice, [],
        $"\"{kept.Label}\" held on {kept.MetDays} of the {kept.KnownDays} days this week that have an answer.",
        kept.MetDays / 7.0,
        ("label", kept.Label), ("metDays", kept.MetDays), ("knownDays", kept.KnownDays), ("kept", true));
    }

    var missed = days.Where(t => t.KnownDays - t.MetDays >= MinDays).OrderByDescending(t => t.KnownDays - t.MetDays).FirstOrDefault();
    if (missed != null)
    {
      var misses = missed.KnownDays - missed.MetDays;
      return Candidate(InsightSeverities.Notice, [],
        $"\"{missed.Label}\" was missed on {misses} of the {missed.KnownDays} days this week that have an answer. If it does not fit, change it.",
        misses / 7.0,
        ("label", missed.Label), ("metDays", missed.MetDays), ("knownDays", missed.KnownDays), ("kept", false));
    }

    var week = context.Body.Targets.FirstOrDefault(t => t.Period == TargetPeriods.Week && t.MetThisWeek == true);
    if (week == null) return null;
    return Candidate(InsightSeverities.Notice, [],
      $"\"{week.Label}\" was met this week.",
      0.5,
      ("label", week.Label), ("kept", true));
  }
}

/// <summary>
/// A new waist reading at or above half of height. The bands and the "less than half your height"
/// line are NICE NG246, which applies them to adults with a BMI under 35, so the rule stays quiet
/// at 35 or above. Notice.
/// </summary>
public sealed class WaistHeightRule : BodyRuleBase
{
  public const double Increased = 0.5;
  public const double High = 0.6;
  public const double BmiLimit = 35;

  public override string Kind => InsightKinds.WaistHeight;

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var body = context.Body;
    if (!Fresh(body.Waist, context.Day) || context.Person.HeightCm is not { } height || height <= 0) return null;
    var waist = body.Waist[^1].Value;
    if (BodyMath.WaistToHeight(waist, height) is not { } ratio || ratio < Increased) return null;
    var weight = body.TrendWeightKg ?? (body.Weight.Count > 0 ? body.Weight[^1].Value : null);
    if (BodyMath.Bmi(weight, height) is { } bmi && bmi >= BmiLimit) return null;

    var band = ratio >= High ? "0.6 or more as high central adiposity" : "0.5 to 0.59 as increased central adiposity";
    return Candidate(InsightSeverities.Notice, [MetricKeys.WaistCm],
      $"Your waist is {Round(ratio, 2).ToString("0.00", CultureInfo.InvariantCulture)} of your height. NICE describes {band}, and suggests keeping your waist to less than half your height.",
      (ratio - Increased) / 0.1,
      ("waistCm", Round(waist)), ("heightCm", Round(height)), ("ratio", Round(ratio, 2)));
  }
}
