using FluentAssertions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret;
using SelfKept.Infrastructure.Services.Interpret.Rules;
using Xunit;

namespace SelfKept.UnitTests.Interpret;

/// <summary>
/// The body and goal rules. They read BodyContext only, so a person with three weeks of data and
/// no 28-day baseline still gets them; that is the point of the first test.
/// </summary>
public class BodyRuleTests
{
  // A Sunday, so the target week rule is awake.
  private static readonly DateOnly Day = new(2026, 11, 8);
  private static readonly RuleEngine Engine = new(RuleEngine.DefaultRules());

  private static RuleContext Context(BodyContext body, UnitSystem units = UnitSystem.Metric, double? heightCm = 180, DateOnly? day = null) =>
    new(
      new Person { Id = Guid.NewGuid(), DisplayName = "Test", TimeZone = "UTC", Units = units, HeightCm = heightCm },
      day ?? Day,
      new Dictionary<string, Baseline>(),
      new Dictionary<string, IReadOnlyList<DailySummary>>(),
      null,
      body);

  private static GoalStanding Standing(
    string kind, string status, double? current = 82, double? pace = -0.4, double? start = 86, double? target = 80,
    bool reachedBefore = false, DateOnly? startedOn = null, string metric = MetricKeys.WeightKg, string unit = "kg") =>
    new(
      new Goal { Id = Guid.NewGuid(), Kind = kind, StartValue = start, TargetValue = target, StartedOn = startedOn ?? Day.AddDays(-28) },
      metric, "Weight", unit, current, pace, status, reachedBefore);

  private static IReadOnlyList<DatedValue> Readings(DateOnly last, params double[] values) =>
    values.Select((v, i) => new DatedValue(last.AddDays(-(values.Length - 1 - i)), v)).ToList();

  // --- the gate ---

  [Fact]
  public void Engine_BodyRuleWithNoBaselines_StillFires()
  {
    var body = new BodyContext
    {
      Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.Ahead, pace: -1.2)],
      Weight = Readings(Day, 83.5, 82.2),
      TrendWeightKg = 82,
      WeeklyRateKg = -1.2,
    };

    var ranked = Engine.Evaluate(Context(body));

    ranked.Select(c => c.Kind).Should().Contain(InsightKinds.LossRateFast);
  }

  // --- loss rate ---

  [Fact]
  public void LossRateFast_OverOnePercentAWeekWithAFatLossGoal_Fires()
  {
    var body = new BodyContext
    {
      Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.Ahead)],
      Weight = Readings(Day, 83.5, 82.2),
      TrendWeightKg = 82,
      WeeklyRateKg = -1.2,
    };

    var candidate = new LossRateFastRule().Evaluate(Context(body));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Notice);
    candidate.Templated.Should().Contain("1.2 kg a week").And.Contain("1.5 %").And.Contain("0.5 to 1 % a week");
    candidate.Evidence["ratePerWeekKg"].Should().Be(-1.2);
  }

  [Fact]
  public void LossRateFast_ImperialPerson_SpeaksInPounds()
  {
    var body = new BodyContext
    {
      Goals = [Standing(GoalKinds.Recomposition, PaceStatuses.Ahead)],
      Weight = Readings(Day, 82.2),
      TrendWeightKg = 82,
      WeeklyRateKg = -1.2,
    };

    var candidate = new LossRateFastRule().Evaluate(Context(body, UnitSystem.Imperial));

    candidate!.Templated.Should().Contain("2.6 lb a week").And.NotContain(" kg");
  }

  [Fact]
  public void LossRateFast_WithinOnePercent_DoesNotFire()
  {
    var body = new BodyContext
    {
      Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.OnTrack)],
      Weight = Readings(Day, 82.2),
      TrendWeightKg = 82,
      WeeklyRateKg = -0.7,
    };
    new LossRateFastRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void LossRateFast_NoFatLossGoal_DoesNotFire()
  {
    var body = new BodyContext
    {
      Goals = [Standing(GoalKinds.SleepBetter, PaceStatuses.Unknown)],
      Weight = Readings(Day, 82.2),
      TrendWeightKg = 82,
      WeeklyRateKg = -1.5,
    };
    new LossRateFastRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void LossRateFast_NoWeighInForDays_DoesNotFire()
  {
    var body = new BodyContext
    {
      Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.Ahead)],
      Weight = Readings(Day.AddDays(-4), 82.2),
      TrendWeightKg = 82,
      WeeklyRateKg = -1.5,
    };
    new LossRateFastRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void LossRateFast_GainingWeight_DoesNotFire()
  {
    var body = new BodyContext
    {
      Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.WrongWay)],
      Weight = Readings(Day, 82.2),
      TrendWeightKg = 82,
      WeeklyRateKg = 1.5,
    };
    new LossRateFastRule().Evaluate(Context(body)).Should().BeNull();
  }

  // --- goals ---

  [Fact]
  public void GoalReached_NewlyReached_Fires()
  {
    var body = new BodyContext { Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.Reached, current: 79.9)] };

    var candidate = new GoalReachedRule().Evaluate(Context(body));

    candidate.Should().NotBeNull();
    candidate!.Templated.Should().Contain("You reached your goal to lose fat").And.Contain("79.9 kg").And.Contain("80 kg");
    candidate.Metrics.Should().Equal(MetricKeys.WeightKg);
  }

  [Fact]
  public void GoalReached_AlreadyReachedAWeekAgo_StaysQuiet()
  {
    var body = new BodyContext { Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.Reached, current: 79.9, reachedBefore: true)] };
    new GoalReachedRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void GoalReached_HoldSteadyGoal_StaysQuiet()
  {
    var body = new BodyContext { Goals = [Standing(GoalKinds.Maintain, PaceStatuses.Reached, current: 80, start: 80, target: 80)] };
    new GoalReachedRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void GoalOffCourse_WrongWayOnTheWeeklyDay_Fires()
  {
    var body = new BodyContext { Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.WrongWay, pace: 0.3)] };

    var candidate = new GoalOffCourseRule().Evaluate(Context(body));

    candidate.Should().NotBeNull();
    candidate!.Templated.Should().Contain("moving away from its target").And.Contain("weight is 0.30 kg up a week");
  }

  [Fact]
  public void GoalOffCourse_WrongWayBetweenWeeklyDays_StaysQuiet()
  {
    var body = new BodyContext { Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.WrongWay, pace: 0.3, startedOn: Day.AddDays(-30))] };
    new GoalOffCourseRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void GoalOffCourse_FirstWeekOfAGoal_StaysQuiet()
  {
    var body = new BodyContext { Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.WrongWay, pace: 0.3, startedOn: Day.AddDays(-7))] };
    new GoalOffCourseRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void GoalOffCourse_OnTrack_StaysQuiet()
  {
    var body = new BodyContext { Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.OnTrack)] };
    new GoalOffCourseRule().Evaluate(Context(body)).Should().BeNull();
  }

  // --- targets ---

  [Fact]
  public void TargetWeek_KeptOnFiveDays_Fires()
  {
    var body = new BodyContext { Targets = [new TargetWeek("Protein", TargetKinds.Metric, TargetPeriods.Day, 6, 7, null)] };

    var candidate = new TargetWeekRule().Evaluate(Context(body));

    candidate.Should().NotBeNull();
    candidate!.Templated.Should().Be("\"Protein\" held on 6 of the 7 days this week that have an answer.");
    candidate.Evidence["kept"].Should().Be(true);
  }

  [Fact]
  public void TargetWeek_MissedOnFiveDays_SaysItCanBeChanged()
  {
    var body = new BodyContext { Targets = [new TargetWeek("Steps", TargetKinds.Metric, TargetPeriods.Day, 1, 7, null)] };

    var candidate = new TargetWeekRule().Evaluate(Context(body));

    candidate!.Templated.Should().Contain("missed on 6 of the 7 days").And.EndWith("If it does not fit, change it.");
    candidate.Evidence["kept"].Should().Be(false);
  }

  [Fact]
  public void TargetWeek_MostlyUnknownDays_StaysQuiet()
  {
    // Two answers in a week say nothing either way: nothing logged is unknown, never a miss.
    var body = new BodyContext { Targets = [new TargetWeek("No alcohol", TargetKinds.Journal, TargetPeriods.Day, 0, 2, null)] };
    new TargetWeekRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void TargetWeek_NotSunday_StaysQuiet()
  {
    var body = new BodyContext { Targets = [new TargetWeek("Protein", TargetKinds.Metric, TargetPeriods.Day, 7, 7, null)] };
    new TargetWeekRule().Evaluate(Context(body, day: Day.AddDays(-2))).Should().BeNull();
  }

  [Fact]
  public void TargetWeek_WeekTargetMet_Fires()
  {
    var body = new BodyContext { Targets = [new TargetWeek("Lift", TargetKinds.Strength, TargetPeriods.Week, 3, 7, true)] };

    var candidate = new TargetWeekRule().Evaluate(Context(body));

    candidate!.Templated.Should().Be("\"Lift\" was met this week.");
  }

  // --- waist ---

  [Fact]
  public void WaistHeight_NewReadingOverHalfOfHeight_Fires()
  {
    var body = new BodyContext { Waist = Readings(Day, 96), TrendWeightKg = 85 };

    var candidate = new WaistHeightRule().Evaluate(Context(body));

    candidate.Should().NotBeNull();
    candidate!.Templated.Should().Contain("0.53 of your height").And.Contain("0.5 to 0.59 as increased central adiposity").And.Contain("NICE");
    candidate.Evidence["ratio"].Should().Be(0.53);
  }

  [Fact]
  public void WaistHeight_SixTenthsOrMore_UsesTheHighBand()
  {
    var body = new BodyContext { Waist = Readings(Day, 110), TrendWeightKg = 95 };
    new WaistHeightRule().Evaluate(Context(body))!.Templated.Should().Contain("0.6 or more as high central adiposity");
  }

  [Fact]
  public void WaistHeight_UnderHalf_StaysQuiet()
  {
    var body = new BodyContext { Waist = Readings(Day, 86) };
    new WaistHeightRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void WaistHeight_OldReading_StaysQuiet()
  {
    var body = new BodyContext { Waist = Readings(Day.AddDays(-10), 96) };
    new WaistHeightRule().Evaluate(Context(body)).Should().BeNull();
  }

  [Fact]
  public void WaistHeight_NoHeight_StaysQuiet()
  {
    var body = new BodyContext { Waist = Readings(Day, 96) };
    new WaistHeightRule().Evaluate(Context(body, heightCm: null)).Should().BeNull();
  }

  [Fact]
  public void WaistHeight_BmiThirtyFiveOrMore_StaysQuiet()
  {
    // NICE applies the bands to a BMI under 35. 120 kg at 180 cm is a BMI of 37.
    var body = new BodyContext { Waist = Readings(Day, 115), TrendWeightKg = 120 };
    new WaistHeightRule().Evaluate(Context(body)).Should().BeNull();
  }

  // --- weight trend and goals ---

  [Fact]
  public void WeightTrend_WithAWeightGoal_StaysQuiet()
  {
    var values = new double?[28];
    for (var i = 0; i < 28; i++) values[i] = 80 - 0.06 * i;
    var plain = Synthetic.Context(new() { ["weight_kg"] = values });
    var body = new BodyContext { Goals = [Standing(GoalKinds.LoseFat, PaceStatuses.OnTrack)] };
    var withGoal = new RuleContext(plain.Person, plain.Day, plain.Baselines, plain.Series, null, body);

    new WeightTrendRule().Evaluate(plain).Should().NotBeNull();
    new WeightTrendRule().Evaluate(withGoal).Should().BeNull();
  }

  [Fact]
  public void WeightTrend_ImperialPerson_SpeaksInPounds()
  {
    var values = new double?[28];
    for (var i = 0; i < 28; i++) values[i] = 80 - 0.06 * i;
    var plain = Synthetic.Context(new() { ["weight_kg"] = values });
    plain.Person.Units = UnitSystem.Imperial;

    new WeightTrendRule().Evaluate(plain)!.Templated.Should().Contain("down by about 3.6 lb over the month");
  }
}
