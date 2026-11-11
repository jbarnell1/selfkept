using FluentAssertions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret;
using SelfKept.Infrastructure.Services.Interpret.Rules;
using Xunit;
using static SelfKept.UnitTests.Interpret.Synthetic;

namespace SelfKept.UnitTests.ContextEngine;

/// <summary>Each rule of the context package: fires, stays quiet, and behaves under context.</summary>
public class NewRuleTests
{
  // Synthetic.Day is Thursday 29 Oct 2026; this is the Sunday after.
  private static readonly DateOnly Sunday = new(2026, 11, 1);

  private static RuleContext Build(RuleContext c, ContextState? state = null, BodyContext? body = null) =>
    new(c.Person, c.Day, c.Baselines, c.Series, c.Labs, body ?? c.Body, state ?? ContextState.Empty);

  private static ContextState Logged(params (DateOnly Day, string[] Tags)[] days) =>
    new() { Logged = days.ToDictionary(d => d.Day, d => (IReadOnlyList<string>)d.Tags) };

  private static BodyContext FatLoss(DateOnly startedOn, double? weeklyRateKg = -0.5) => new()
  {
    Goals = [new GoalStanding(new Goal { Id = Guid.NewGuid(), Kind = GoalKinds.LoseFat, StartedOn = startedOn }, MetricKeys.WeightKg, "Weight", "kg", 82, weeklyRateKg, PaceStatuses.OnTrack, false)],
    WeeklyRateKg = weeklyRateKg,
  };

  private static Dictionary<string, double?[]> RhrUp() => new() { ["resting_hr_bpm"] = Wobble(56, 4, 66) };

  private static Dictionary<string, double?[]> RhrUpHrvDown() => new() { ["resting_hr_bpm"] = Wobble(56, 4, 66), ["hrv_rmssd_ms"] = Wobble(50, 6, 30) };

  // --- early signs and breathing rate ---

  [Fact]
  public void EarlySigns_RestingHeartRateUpAndHrvDown_Fires_AsNotice()
  {
    var candidate = new EarlySignsRule().Evaluate(Context(RhrUpHrvDown()));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Notice);
    candidate.Templated.Should().Contain("resting heart rate is up and HRV is down").And.Contain("pattern worth watching").And.Contain("Mason 2022");
    candidate.Templated.Should().NotContainAny("you are getting sick", "diagnos");
  }

  [Fact]
  public void EarlySigns_OneSignalOnly_StaysQuiet() =>
    new EarlySignsRule().Evaluate(Context(RhrUp())).Should().BeNull();

  [Fact]
  public void EarlySigns_BreathingRateUpAlone_StaysQuiet() =>
    new EarlySignsRule().Evaluate(Context(new() { ["respiratory_rate_bpm"] = Wobble(15, 0.4, 19) })).Should().BeNull();

  [Fact]
  public void EarlySigns_BreathingThreeAboveUsualWithHeartRate_Fires()
  {
    var series = RhrUp();
    series["respiratory_rate_bpm"] = Wobble(15, 0.4, 18.1);

    new EarlySignsRule().Evaluate(Context(series))!.Metrics.Should().Equal("resting_hr_bpm", "respiratory_rate_bpm");
  }

  [Fact]
  public void EarlySigns_BreathingUnderThreeAboveUsual_DoesNotCount()
  {
    var series = RhrUp();
    series["respiratory_rate_bpm"] = Wobble(15, 0.4, 17.5);

    new EarlySignsRule().Evaluate(Context(series)).Should().BeNull();
  }

  [Fact]
  public void EarlySigns_WithTwentyNightsOfBaseline_StaysQuiet()
  {
    var series = RhrUpHrvDown();
    for (var i = 0; i < 8; i++) { series["resting_hr_bpm"][i] = null; series["hrv_rmssd_ms"][i] = null; }

    new EarlySignsRule().Evaluate(Context(series)).Should().BeNull("21 nights of baseline are needed");
  }

  [Fact]
  public void EarlySigns_WithAColdSymptomLogged_LeavesItToColdStarting()
  {
    var context = Build(Context(RhrUpHrvDown()), Logged((Day.AddDays(-1), [JournalTags.SoreThroat])));

    new EarlySignsRule().Evaluate(context).Should().BeNull();
    new ColdStartingRule().Evaluate(context).Should().NotBeNull();
  }

  [Fact]
  public void EarlySigns_UnderHardTraining_IsAnnotated()
  {
    var state = new ContextState { Active = [new ActiveContext(ContextKinds.HardTraining, Day.AddDays(-1), Day.AddDays(-1), ContextSources.Derived, "You logged a strength session on Oct 28, 2026")] };

    new EarlySignsRule().Evaluate(Build(Context(RhrUpHrvDown()), state))!.Templated.Should().EndWith("which can explain readings like these.");
  }

  // --- cold starting ---

  [Fact]
  public void ColdStarting_SymptomAndOneReadingOff_Fires_AsWatch()
  {
    var candidate = new ColdStartingRule().Evaluate(Build(Context(RhrUp()), Logged((Day.AddDays(-1), [JournalTags.SoreThroat]))));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Watch);
    candidate.Templated.Should().StartWith("You logged a sore throat on Oct 28, 2026, and last night resting heart rate is up.")
      .And.Contain("can fit the start of a cold").And.Contain("pattern worth watching");
  }

  [Fact]
  public void ColdStarting_ReadingsOffWithoutASymptom_StaysQuiet() =>
    new ColdStartingRule().Evaluate(Context(RhrUpHrvDown())).Should().BeNull();

  [Fact]
  public void ColdStarting_SymptomWithNormalReadings_StaysQuiet() =>
    new ColdStartingRule().Evaluate(Build(Context(new() { ["resting_hr_bpm"] = Wobble(56, 4, 56) }), Logged((Day, [JournalTags.Cough])))).Should().BeNull();

  [Fact]
  public void ColdStarting_SymptomFourDaysAgo_StaysQuiet() =>
    new ColdStartingRule().Evaluate(Build(Context(RhrUp()), Logged((Day.AddDays(-3), [JournalTags.Cough])))).Should().BeNull();

  [Fact]
  public void ColdStarting_UnderALoggedCyclePhase_IsDemotedToNotice()
  {
    var state = new ContextState
    {
      Logged = new Dictionary<DateOnly, IReadOnlyList<string>> { [Day] = [JournalTags.Congestion] },
      Active = [new ActiveContext(ContextKinds.CyclePhase, Day.AddDays(-4), null, ContextSources.User, "You logged a point in your cycle on Oct 25, 2026")],
    };

    new ColdStartingRule().Evaluate(Build(Context(RhrUp()), state))!.Severity.Should().Be(InsightSeverities.Notice);
  }

  // --- yesterday to last night ---

  [Fact]
  public void YesterdayLastNight_AlcoholThenHighRestingHeartRate_Fires_AsAnObservation()
  {
    var candidate = new YesterdayLastNightRule().Evaluate(Build(Context(RhrUp()), Logged((Day.AddDays(-1), [JournalTags.Alcohol]))));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Notice);
    candidate.Templated.Should().StartWith("You logged alcohol yesterday, and last night your resting heart rate was 66 bpm, above your usual range");
    candidate.Evidence["tag"].Should().Be(JournalTags.Alcohol);
  }

  [Fact]
  public void YesterdayLastNight_NoTag_StaysQuiet() =>
    new YesterdayLastNightRule().Evaluate(Context(RhrUp())).Should().BeNull();

  [Fact]
  public void YesterdayLastNight_TagWithAnOrdinaryNight_StaysQuiet() =>
    new YesterdayLastNightRule().Evaluate(Build(Context(new() { ["resting_hr_bpm"] = Wobble(56, 4, 56) }), Logged((Day.AddDays(-1), [JournalTags.Alcohol])))).Should().BeNull();

  [Fact]
  public void YesterdayLastNight_LateCaffeineThenShortSleep_QuotesTheReview()
  {
    var context = Build(Context(new() { ["sleep_stage"] = Wobble(470, 20, 380) }), Logged((Day.AddDays(-1), [JournalTags.LateCaffeine])));

    var candidate = new YesterdayLastNightRule().Evaluate(context)!;

    candidate.Templated.Should().Contain("you slept 6 h 20 min, below your usual range").And.Contain("Gardiner 2023");
  }

  [Fact]
  public void YesterdayLastNight_HardTrainingFromTheStrengthLog_Counts()
  {
    var state = new ContextState { HardTrainingDays = [Day.AddDays(-1)] };

    new YesterdayLastNightRule().Evaluate(Build(Context(RhrUp()), state))!.Templated.Should().StartWith("You logged hard training yesterday");
  }

  // --- target slipping ---

  private static TargetSnapshot Day4Of6(Guid id, int met = 2, int known = 6, string label = "Protein") => new(id, label, TargetPeriods.Day, met, known, null);

  [Fact]
  public void TargetSlipping_NewlyMissedOnFourOfTheKnownDays_Fires()
  {
    var id = Guid.NewGuid();
    var state = new ContextState { Targets = [Day4Of6(id)], TargetsYesterday = [Day4Of6(id, met: 3)] };

    var candidate = new TargetSlippingRule().Evaluate(Build(Context([]), state));

    candidate.Should().NotBeNull();
    candidate!.Templated.Should().Be("\"Protein\" was missed on 4 of the last 6 days that have an answer. What got in the way? If it does not fit, change it.");
  }

  [Fact]
  public void TargetSlipping_AlreadySlippingYesterday_StaysQuiet()
  {
    var id = Guid.NewGuid();
    var state = new ContextState { Targets = [Day4Of6(id)], TargetsYesterday = [Day4Of6(id)] };

    new TargetSlippingRule().Evaluate(Build(Context([]), state)).Should().BeNull("it already spoke");
  }

  [Fact]
  public void TargetSlipping_ThreeMisses_StaysQuiet() =>
    new TargetSlippingRule().Evaluate(Build(Context([]), new ContextState { Targets = [Day4Of6(Guid.NewGuid(), met: 3)] })).Should().BeNull();

  [Fact]
  public void TargetSlipping_WhileIll_StaysQuiet()
  {
    var state = new ContextState { Targets = [Day4Of6(Guid.NewGuid())], Active = [ContextRuleTests.Ill(Day)] };

    new TargetSlippingRule().Evaluate(Build(Context([]), state)).Should().BeNull();
  }

  [Fact]
  public void TargetSlipping_WeeklyTargetMissedTwoWeeksRunning_FiresOnSunday()
  {
    var id = Guid.NewGuid();
    var state = new ContextState
    {
      Targets = [new TargetSnapshot(id, "Lifting days", TargetPeriods.Week, 1, 5, false)],
      TargetsWeekAgo = [new TargetSnapshot(id, "Lifting days", TargetPeriods.Week, 1, 6, false)],
    };

    new TargetSlippingRule().Evaluate(Build(Context([], Sunday), state))!.Templated.Should().StartWith("\"Lifting days\" was missed this week and the week before.");
    new TargetSlippingRule().Evaluate(Build(Context([]), state)).Should().BeNull("weekly targets are said on Sunday");
  }

  [Fact]
  public void TargetSlipping_OnASundayTargetWeekSpeaksFor_LeavesThatTargetAlone()
  {
    var id = Guid.NewGuid();
    var state = new ContextState { Targets = [Day4Of6(id, met: 1, known: 6)], TargetsYesterday = [Day4Of6(id, met: 3)] };
    var body = new BodyContext { Targets = [new TargetWeek("Protein", TargetKinds.Metric, TargetPeriods.Day, 1, 6, null)] };
    var context = Build(Context([], Sunday), state, body);

    new TargetWeekRule().Evaluate(context)!.Evidence["label"].Should().Be("Protein");
    new TargetSlippingRule().Evaluate(context).Should().BeNull("one card per target on the day target_week speaks");
  }

  // --- short sleep during a diet ---

  private static double?[] ShortNights() => Steady(480, 480, 400, 400, 480, 400, 480, 400);

  [Fact]
  public void ShortSleepDiet_FourShortNightsInSevenWithAFatLossGoal_Fires_AsWatch()
  {
    var candidate = new ShortSleepDietRule().Evaluate(Build(Context(new() { ["sleep_stage"] = ShortNights() }), body: FatLoss(Day.AddDays(-30))));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Watch);
    candidate.Templated.Should().Contain("under 7 hours on 4 of the last 7 nights").And.Contain("7 or more hours");
  }

  [Fact]
  public void ShortSleepDiet_WithoutAFatLossGoal_StaysQuiet() =>
    new ShortSleepDietRule().Evaluate(Context(new() { ["sleep_stage"] = ShortNights() })).Should().BeNull();

  [Fact]
  public void ShortSleepDiet_AlreadyTrueTheNightBefore_StaysQuiet() =>
    new ShortSleepDietRule().Evaluate(Build(Context(new() { ["sleep_stage"] = Steady(480, 400, 400, 400, 400, 480, 480, 480) }), body: FatLoss(Day.AddDays(-30)))).Should().BeNull();

  // --- recovery strain ---

  private static RuleContext LowHrv(params DateOnly[] trainingDays) =>
    Build(Context(new() { ["hrv_rmssd_ms"] = Wobble(50, 6, 45) }), new ContextState { HardTrainingDays = trainingDays });

  [Fact]
  public void RecoveryStrain_LowHrvTheDayAfterHardTraining_Fires_SayingItIsExpected()
  {
    var candidate = new RecoveryStrainRule().Evaluate(LowHrv(Day.AddDays(-1)));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Notice);
    candidate.Templated.Should().Contain("a day after hard training").And.Contain("That is expected").And.Contain("Stanley 2013");
  }

  [Fact]
  public void RecoveryStrain_TwoDaysAfter_StillInsideTheWindow() =>
    new RecoveryStrainRule().Evaluate(LowHrv(Day.AddDays(-2)))!.Templated.Should().Contain("two days after");

  [Fact]
  public void RecoveryStrain_ThreeDaysAfter_StaysQuiet() =>
    new RecoveryStrainRule().Evaluate(LowHrv(Day.AddDays(-3))).Should().BeNull();

  [Fact]
  public void RecoveryStrain_HrvInsideTheRange_StaysQuiet() =>
    new RecoveryStrainRule().Evaluate(Build(Context(new() { ["hrv_rmssd_ms"] = Wobble(50, 6, 52) }), new ContextState { HardTrainingDays = [Day.AddDays(-1)] })).Should().BeNull();

  [Fact]
  public void RecoveryStrain_WhileIll_StaysQuiet()
  {
    var context = Build(Context(new() { ["hrv_rmssd_ms"] = Wobble(50, 6, 45) }), new ContextState { HardTrainingDays = [Day.AddDays(-1)], Active = [ContextRuleTests.Ill(Day)] });

    new RecoveryStrainRule().Evaluate(context).Should().BeNull();
  }

  // --- strength on track ---

  private static RuleContext Lifting(DateOnly goalStart, params (int DaysAgo, double Kg)[] squats) =>
    Build(Context([]),
      new ContextState { Strength = squats.Select(s => new LiftEstimate(Day.AddDays(-s.DaysAgo), "squat", "Squat", s.Kg)).ToList() },
      FatLoss(goalStart));

  [Fact]
  public void StrengthOnTrack_EstimateRisingOnTheWeeklyCheckDay_Fires()
  {
    var candidate = new StrengthOnTrackRule().Evaluate(Lifting(Day.AddDays(-14), (13, 100), (2, 102.5)));

    candidate.Should().NotBeNull();
    candidate!.Templated.Should().Be("Your estimated best squat is 102.5 kg, against 100 kg when your goal to lose fat started: strength is rising while you lose fat.");
  }

  [Fact]
  public void StrengthOnTrack_EstimateHolding_SaysHolding() =>
    new StrengthOnTrackRule().Evaluate(Lifting(Day.AddDays(-14), (13, 100), (2, 98)))!.Templated.Should().Contain("strength is holding");

  [Fact]
  public void StrengthOnTrack_EstimateFalling_StaysQuiet() =>
    new StrengthOnTrackRule().Evaluate(Lifting(Day.AddDays(-14), (13, 100), (2, 90))).Should().BeNull();

  [Fact]
  public void StrengthOnTrack_NotTheWeeklyCheckDay_StaysQuiet() =>
    new StrengthOnTrackRule().Evaluate(Lifting(Day.AddDays(-15), (13, 100), (2, 102.5))).Should().BeNull();

  [Fact]
  public void StrengthOnTrack_NoSetInTheLastWeek_StaysQuiet() =>
    new StrengthOnTrackRule().Evaluate(Lifting(Day.AddDays(-14), (13, 100), (8, 102.5))).Should().BeNull();

  // --- mood during a deficit ---

  private static double?[] Mood(double before, double recent)
  {
    var values = new double?[28];
    for (var i = 0; i < 28; i++) values[i] = i < 14 ? before : recent;
    return values;
  }

  [Fact]
  public void MoodDeficit_APointLowerWhileLosing_Fires_AsWatch()
  {
    var candidate = new MoodDeficitRule().Evaluate(Build(Context(new() { ["checkin_mood"] = Mood(5, 3.5) }), body: FatLoss(Day.AddDays(-14))));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Watch);
    candidate.Templated.Should().StartWith("Mood at check-ins averaged 3.5 over the last two weeks, against 5 the two weeks before");
  }

  [Fact]
  public void MoodDeficit_HalfAPointLower_StaysQuiet() =>
    new MoodDeficitRule().Evaluate(Build(Context(new() { ["checkin_mood"] = Mood(5, 4.5) }), body: FatLoss(Day.AddDays(-14)))).Should().BeNull();

  [Fact]
  public void MoodDeficit_WeightNotComingDown_StaysQuiet() =>
    new MoodDeficitRule().Evaluate(Build(Context(new() { ["checkin_mood"] = Mood(5, 3) }), body: FatLoss(Day.AddDays(-14), weeklyRateKg: 0.1))).Should().BeNull();

  [Fact]
  public void MoodDeficit_TooFewCheckins_StaysQuiet()
  {
    var mood = Mood(5, 3);
    for (var i = 0; i < 8; i++) mood[i] = null;

    new MoodDeficitRule().Evaluate(Build(Context(new() { ["checkin_mood"] = mood }), body: FatLoss(Day.AddDays(-14)))).Should().BeNull();
  }

  // --- personal pattern ---

  [Fact]
  public void PersonalPattern_NewlyEstablished_Fires_AsAPatternNotProof()
  {
    var pattern = new PatternStats(JournalTags.Alcohol, MetricKeys.RestingHrBpm, 9, 31, 62, 58, 4, 60, 65, 56, 59, 0.2, 1.5, 6, 0, true);

    var candidate = new PersonalPatternRule().Evaluate(Build(Context([]), new ContextState { NewPatterns = [pattern] }));

    candidate.Should().NotBeNull();
    candidate!.Templated.Should().StartWith("A new pattern in your own data. On 9 nights after alcohol, against 31 without, your resting heart rate ran 4 bpm higher")
      .And.Contain("6 of the 9 were Friday or Saturday nights").And.EndWith("A pattern in your own data, not proof.");
  }

  [Fact]
  public void PersonalPattern_NothingNew_StaysQuiet() =>
    new PersonalPatternRule().Evaluate(Context([])).Should().BeNull();

  // --- registration ---

  [Fact]
  public void DefaultRules_IncludeEveryNewKind() =>
    RuleEngine.DefaultRules().Select(r => r.Kind).Should().Contain(
    [
      InsightKinds.YesterdayLastNight, InsightKinds.TargetSlipping, InsightKinds.EarlySigns, InsightKinds.ColdStarting,
      InsightKinds.ShortSleepDiet, InsightKinds.RecoveryStrain, InsightKinds.StrengthOnTrack, InsightKinds.MoodDeficit, InsightKinds.PersonalPattern,
    ]);
}
