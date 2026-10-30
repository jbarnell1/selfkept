using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Collect;
using SelfKept.Infrastructure.Services.Interpret;
using SelfKept.Infrastructure.Services.Interpret.Rules;
using Xunit;
using static SelfKept.UnitTests.Interpret.Synthetic;

namespace SelfKept.UnitTests.Interpret;

/// <summary>Every rule in docs/insights-design.md: a synthetic 28-day series that fires it and one that does not.</summary>
public class RuleTests
{
  private static readonly RuleEngine Engine = new(RuleEngine.DefaultRules());

  // --- HRV ---

  [Fact]
  public void HrvLowRun_ThreeNightsBelowP10_Fires_WithRmssdWhenPresent()
  {
    var context = Context(new() { ["hrv_rmssd_ms"] = Wobble(50, 6, 30, 31, 29) });

    var candidate = new HrvLowRunRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Watch);
    candidate.Metrics.Should().Equal("hrv_rmssd_ms");
    candidate.BandDistance.Should().BeGreaterThan(0);
    candidate.Templated.Should().Contain("three nights");
  }

  [Fact]
  public void HrvLowRun_OnlyTwoLowNights_DoesNotFire()
  {
    var context = Context(new() { ["hrv_sdnn_ms"] = Wobble(50, 6, 50, 30, 31) });
    new HrvLowRunRule().Evaluate(context).Should().BeNull();
  }

  [Fact]
  public void HrvLowRun_UsesSdnnWhenThereIsNoRmssd()
  {
    var context = Context(new() { ["hrv_sdnn_ms"] = Wobble(50, 6, 30, 31, 29) });
    new HrvLowRunRule().Evaluate(context)!.Metrics.Should().Equal("hrv_sdnn_ms");
  }

  [Fact]
  public void HrvRecovered_BackAboveP25AfterARun_Fires()
  {
    var context = Context(new() { ["hrv_sdnn_ms"] = Wobble(50, 4, 30, 31, 29, 53) });

    var candidate = new HrvRecoveredRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Notice);
    new HrvLowRunRule().Evaluate(context).Should().BeNull("the run ended yesterday");
  }

  [Fact]
  public void HrvRecovered_StillBelowP25_DoesNotFire()
  {
    var context = Context(new() { ["hrv_sdnn_ms"] = Wobble(50, 4, 30, 31, 29, 40) });
    new HrvRecoveredRule().Evaluate(context).Should().BeNull();
  }

  // --- resting HR and illness ---

  [Fact]
  public void RhrElevated_TwoNightsFiveOverP90_Fires()
  {
    var context = Context(new() { ["resting_hr_bpm"] = Wobble(56, 4, 66, 67) });

    var candidate = new RhrElevatedRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Watch);
  }

  [Fact]
  public void RhrElevated_OneNightOnly_DoesNotFire()
  {
    var context = Context(new() { ["resting_hr_bpm"] = Wobble(56, 4, 57, 67) });
    new RhrElevatedRule().Evaluate(context).Should().BeNull();
  }

  [Fact]
  public void IllnessPattern_ElevatedRhrAndWristTempOverP90_Fires()
  {
    var context = Context(new()
    {
      ["resting_hr_bpm"] = Wobble(56, 4, 66, 67),
      ["wrist_temp_delta_c"] = Wobble(0, 0.2, 0.9),
    });

    var candidate = new IllnessPatternRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Metrics.Should().Contain("wrist_temp_delta_c");
    candidate.Evidence.Should().ContainKey("wristTempDelta");
    candidate.BandDistance.Should().BeGreaterThan(new RhrElevatedRule().Evaluate(context)!.BandDistance, "the pattern outranks the bare elevation");
  }

  [Fact]
  public void IllnessPattern_ElevatedRhrAlone_DoesNotFire()
  {
    var context = Context(new()
    {
      ["resting_hr_bpm"] = Wobble(56, 4, 66, 67),
      ["wrist_temp_delta_c"] = Wobble(0, 0.2, 0.0),
      ["respiratory_rate_bpm"] = Wobble(14, 1, 14),
    });
    new IllnessPatternRule().Evaluate(context).Should().BeNull();
  }

  // --- sleep ---

  [Fact]
  public void SleepDebt_ThreeShortNightsInFour_Fires_WithTheNightThatSpansMidnight()
  {
    // Last night ran 23:00 to 06:30 local (+2) with 15 minutes awake: credited to 29 Oct as 435 minutes.
    var zone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
    DateTime Local(string t) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(DateTime.Parse(t, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Unspecified), zone);
    Sample Stage(string start, string end, int stage) => new() { MetricKey = "sleep_stage", StartAt = Local(start), EndAt = Local(end), Value = stage };
    var night = new[]
    {
      Stage("2026-10-28 23:00", "2026-10-29 01:00", SleepStages.Light),
      Stage("2026-10-29 01:00", "2026-10-29 03:00", SleepStages.Deep),
      Stage("2026-10-29 03:00", "2026-10-29 03:15", SleepStages.Awake),
      Stage("2026-10-29 03:15", "2026-10-29 06:30", SleepStages.Rem),
    };
    DailySummaryRules.DayFor(night[0], zone).Should().Be(Day, "a night belongs to the day it ended");
    var lastNight = DailySummaryRules.Derive(PersonId, "sleep_stage", Day, night, Computed)!.Value;
    lastNight.Should().Be(435);

    // Usual nights around 480 minutes; the last four: short, short, usual, last night (short).
    var context = Context(new() { ["sleep_stage"] = Wobble(480, 20, 390, 400, 485, lastNight) });

    var candidate = new SleepDebtRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Watch);
    candidate.Evidence["shortNightsMinutes"].Should().BeEquivalentTo(new double[] { 390, 400, 435 });
  }

  [Fact]
  public void SleepDebt_TwoShortNightsInFour_DoesNotFire()
  {
    var context = Context(new() { ["sleep_stage"] = Wobble(480, 20, 390, 480, 485, 400) });
    new SleepDebtRule().Evaluate(context).Should().BeNull();
  }

  [Fact]
  public void SleepGoodWeek_SevenDayMedianTwentyOverMonth_Fires()
  {
    var context = Context(new() { ["sleep_stage"] = Steady(420, 450, 455, 448, 452, 460, 449, 451) });

    var candidate = new SleepGoodWeekRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Notice);
  }

  [Fact]
  public void SleepGoodWeek_OnlyTenMinutesBetter_DoesNotFire()
  {
    var context = Context(new() { ["sleep_stage"] = Steady(420, 430, 431, 429, 432, 430, 428, 430) });
    new SleepGoodWeekRule().Evaluate(context).Should().BeNull();
  }

  // --- activity, training, weight ---

  [Fact]
  public void ActivityDip_WeekBelowHalfOfMonth_Fires()
  {
    var context = Context(new() { ["steps"] = Steady(8000, 3000, 2500, 3200, 2800, 3100, 2900, 3000) });
    new ActivityDipRule().Evaluate(context).Should().NotBeNull();
  }

  [Fact]
  public void ActivityDip_QuietWeekWithOnlyFourDays_DoesNotFire()
  {
    var context = Context(new() { ["steps"] = Steady(8000, 3000, null, 3200, null, 3100, null, 3000) });
    new ActivityDipRule().Evaluate(context).Should().BeNull("n7 is 4");
  }

  [Fact]
  public void TrainingSpike_WeekAboveOneAndAHalfTimesMonth_Fires()
  {
    var context = Context(new() { ["exercise_min"] = Steady(30, 60, 55, 70, 65, 60, 58, 62) });
    new TrainingSpikeRule().Evaluate(context).Should().NotBeNull();
  }

  [Fact]
  public void TrainingSpike_ModestIncrease_DoesNotFire()
  {
    var context = Context(new() { ["exercise_min"] = Steady(30, 40, 38, 42, 40, 41, 39, 40) });
    new TrainingSpikeRule().Evaluate(context).Should().BeNull();
  }

  [Fact]
  public void WeightTrend_MoreThanAKiloOverTheMonth_Fires_WithDirection()
  {
    var values = new double?[28];
    for (var i = 0; i < 28; i++) values[i] = 80 - 0.06 * i;
    var context = Context(new() { ["weight_kg"] = values });

    var candidate = new WeightTrendRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Templated.Should().Contain("down by about 1.6 kg");
    candidate.Evidence["deltaKg"].Should().Be(-1.6);
  }

  [Fact]
  public void WeightTrend_HalfAKilo_DoesNotFire()
  {
    var values = new double?[28];
    for (var i = 0; i < 28; i++) values[i] = 80 + 0.02 * i;
    new WeightTrendRule().Evaluate(Context(new() { ["weight_kg"] = values })).Should().BeNull();
  }

  // --- mood ---

  [Fact]
  public void MoodLowRun_ThreeLowDaysInFive_Fires()
  {
    var context = Context(new() { ["checkin_mood"] = Steady(4, 2, 2, 4, 1, 2) });

    var candidate = new MoodLowRunRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Watch);
  }

  [Fact]
  public void MoodLowRun_TwoLowDaysInFive_DoesNotFire()
  {
    var context = Context(new() { ["checkin_mood"] = Steady(4, 2, 4, 4, 2, 4) });
    new MoodLowRunRule().Evaluate(context).Should().BeNull();
  }

  [Fact]
  public void MoodSleepLink_LowMoodAndSleepDebtTogether_Fires()
  {
    var context = Context(new()
    {
      ["checkin_mood"] = Steady(4, 2, 2, 4, 1, 2),
      ["sleep_stage"] = Wobble(480, 20, 390, 400, 485, 395),
    });

    var candidate = new MoodSleepLinkRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Metrics.Should().BeEquivalentTo(new[] { "checkin_mood", "sleep_stage" });
    candidate.BandDistance.Should().BeGreaterThan(new SleepDebtRule().Evaluate(context)!.BandDistance, "the link outranks either half");
  }

  [Fact]
  public void MoodSleepLink_LowMoodWithNormalSleep_DoesNotFire()
  {
    var context = Context(new()
    {
      ["checkin_mood"] = Steady(4, 2, 2, 4, 1, 2),
      ["sleep_stage"] = Wobble(480, 20, 470, 490, 485, 475),
    });
    new MoodSleepLinkRule().Evaluate(context).Should().BeNull();
    new MoodLowRunRule().Evaluate(context).Should().NotBeNull();
  }

  // --- SpO2 ---

  [Fact]
  public void Spo2Low_TwoNightsBelow93InSeven_Fires_AsClinician_NamingTheDates()
  {
    var context = Context(new() { ["spo2_pct"] = Wobble(96.5, 0.6, 96, 92.4, 96, 96, 97, 96, 92.1) });

    var candidate = new Spo2LowRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Clinician);
    candidate.Templated.Should().Contain("Oct 29, 2026").And.Contain("Oct 24, 2026").And.Contain("Bring");
  }

  [Fact]
  public void Spo2Low_OneNightOnly_DoesNotFire()
  {
    var context = Context(new() { ["spo2_pct"] = Wobble(96.5, 0.6, 96, 96, 96, 96, 97, 96, 92.1) });
    new Spo2LowRule().Evaluate(context).Should().BeNull();
  }

  // --- learning, labs, the gate ---

  [Fact]
  public void Learning_FiresOnlyWhenNoMetricHasFourteenDays()
  {
    var learning = Context(new() { ["steps"] = OnlyLast(10, 5000), ["hrv_sdnn_ms"] = OnlyLast(13, 50) });
    var established = Context(new() { ["steps"] = OnlyLast(10, 5000), ["hrv_sdnn_ms"] = OnlyLast(14, 50) });

    new LearningRule().Evaluate(learning).Should().NotBeNull();
    new LearningRule().Evaluate(established).Should().BeNull();
    Engine.Evaluate(learning).Select(c => c.Kind).Should().Equal("learning");
    Engine.Evaluate(established).Should().NotContain(c => c.Kind == "learning");
  }

  [Fact]
  public void LabRules_WithoutAnyPanel_DoNotFire()
  {
    var context = Context(new() { ["steps"] = Steady(5000) });
    context.Labs.Panels.Should().BeEmpty();
    new LabOutOfRangeRule().Evaluate(context).Should().BeNull();
    new LabTrendRule().Evaluate(context).Should().BeNull();
    Engine.Evaluate(context).Should().NotContain(c => c.Kind == "lab_out_of_range" || c.Kind == "lab_trend");
  }

  [Fact]
  public void Engine_DoesNotFireARuleWhoseMetricHasFewerThanFourteenDays()
  {
    // Thirteen nights of HRV ending in three low ones: short of fourteen, so the gate holds the rule back.
    var hrv = OnlyLast(13, 50);
    hrv[^1] = 20; hrv[^2] = 20; hrv[^3] = 20;
    var context = Context(new() { ["hrv_sdnn_ms"] = hrv, ["steps"] = Steady(5000) });

    context.Established("hrv_sdnn_ms").Should().BeFalse();
    context.Established("steps").Should().BeTrue();
    Engine.Evaluate(context).Should().NotContain(c => c.Kind == "hrv_low_run");
    Engine.Evaluate(context).Should().NotContain(c => c.Kind == "learning", "steps is established");
  }
}
