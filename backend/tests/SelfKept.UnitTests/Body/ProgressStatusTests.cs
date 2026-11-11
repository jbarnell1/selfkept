using FluentAssertions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Body;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>ProgressService.Status: every branch, on a loss (90 to 80), a gain (70 to 75) and a hold (80).</summary>
public class ProgressStatusTests
{
  [Fact]
  public void Status_LossPastTheTarget_IsReached()
  {
    ProgressService.Status(current: 79, start: 90, target: 80, pace: -0.5, plannedPace: -0.5).Should().Be(PaceStatuses.Reached);
  }

  [Fact]
  public void Status_LossWithinHalfAPercentOfTheTarget_IsReached()
  {
    ProgressService.Status(current: 80.3, start: 90, target: 80, pace: null, plannedPace: null).Should().Be(PaceStatuses.Reached);
  }

  [Fact]
  public void Status_LossJustOutsideTheTolerance_IsNotReached()
  {
    ProgressService.Status(current: 80.5, start: 90, target: 80, pace: -0.5, plannedPace: null).Should().Be(PaceStatuses.OnTrack);
  }

  [Fact]
  public void Status_NoPace_IsUnknown()
  {
    ProgressService.Status(current: 85, start: 90, target: 80, pace: null, plannedPace: -0.5).Should().Be(PaceStatuses.Unknown);
  }

  [Fact]
  public void Status_PaceAsGoodAsFlat_IsBehind()
  {
    ProgressService.Status(current: 85, start: 90, target: 80, pace: -0.02, plannedPace: -0.5).Should().Be(PaceStatuses.Behind);
    ProgressService.Status(current: 85, start: 90, target: 80, pace: 0.02, plannedPace: -0.5).Should().Be(PaceStatuses.Behind);
  }

  [Fact]
  public void Status_PacePointingAwayFromTheTarget_IsTheWrongWay()
  {
    ProgressService.Status(current: 85, start: 90, target: 80, pace: 0.3, plannedPace: -0.5).Should().Be(PaceStatuses.WrongWay);
  }

  [Fact]
  public void Status_RightWayWithNoPlannedPace_IsOnTrack()
  {
    ProgressService.Status(current: 85, start: 90, target: 80, pace: -0.1, plannedPace: null).Should().Be(PaceStatuses.OnTrack);
    ProgressService.Status(current: 85, start: 90, target: 80, pace: -0.1, plannedPace: 0).Should().Be(PaceStatuses.OnTrack);
  }

  [Fact]
  public void Status_AtThePlannedPace_IsOnTrack()
  {
    ProgressService.Status(current: 85, start: 90, target: 80, pace: -0.5, plannedPace: -0.5).Should().Be(PaceStatuses.OnTrack);
  }

  [Fact]
  public void Status_AtSixtyPercentOfThePlannedPace_IsStillOnTrack()
  {
    ProgressService.Status(current: 85, start: 90, target: 80, pace: -0.3, plannedPace: -0.5).Should().Be(PaceStatuses.OnTrack);
  }

  [Fact]
  public void Status_WellUnderThePlannedPace_IsBehind()
  {
    ProgressService.Status(current: 85, start: 90, target: 80, pace: -0.2, plannedPace: -0.5).Should().Be(PaceStatuses.Behind);
  }

  [Fact]
  public void Status_AQuarterFasterThanPlanned_IsAhead()
  {
    ProgressService.Status(current: 85, start: 90, target: 80, pace: -0.625, plannedPace: -0.5).Should().Be(PaceStatuses.Ahead);
    ProgressService.Status(current: 85, start: 90, target: 80, pace: -0.9, plannedPace: -0.5).Should().Be(PaceStatuses.Ahead);
  }

  [Fact]
  public void Status_GainOnPace_IsOnTrack()
  {
    ProgressService.Status(current: 72, start: 70, target: 75, pace: 0.25, plannedPace: 0.25).Should().Be(PaceStatuses.OnTrack);
  }

  [Fact]
  public void Status_GainLosingWeight_IsTheWrongWay()
  {
    ProgressService.Status(current: 72, start: 70, target: 75, pace: -0.2, plannedPace: 0.25).Should().Be(PaceStatuses.WrongWay);
  }

  [Fact]
  public void Status_GainPastTheTarget_IsReached()
  {
    ProgressService.Status(current: 75.5, start: 70, target: 75, pace: 0.25, plannedPace: 0.25).Should().Be(PaceStatuses.Reached);
  }

  [Fact]
  public void Status_HoldWithinOnePercent_IsReached()
  {
    ProgressService.Status(current: 80.5, start: 80, target: 80, pace: 0.4, plannedPace: null).Should().Be(PaceStatuses.Reached);
    ProgressService.Status(current: 79.4, start: 80, target: 80, pace: null, plannedPace: null).Should().Be(PaceStatuses.Reached);
  }

  [Fact]
  public void Status_HoldOffTargetWithNoPace_IsUnknown()
  {
    ProgressService.Status(current: 82, start: 80, target: 80, pace: null, plannedPace: null).Should().Be(PaceStatuses.Unknown);
  }

  [Fact]
  public void Status_HoldDriftingBack_IsOnTrack()
  {
    ProgressService.Status(current: 82, start: 80, target: 80, pace: -0.3, plannedPace: null).Should().Be(PaceStatuses.OnTrack);
    ProgressService.Status(current: 78, start: 80, target: 80, pace: 0.3, plannedPace: null).Should().Be(PaceStatuses.OnTrack);
  }

  [Fact]
  public void Status_HoldDriftingFurtherAway_IsTheWrongWay()
  {
    ProgressService.Status(current: 82, start: 80, target: 80, pace: 0.3, plannedPace: null).Should().Be(PaceStatuses.WrongWay);
  }

  [Fact]
  public void GoalMetric_GoalWithItsOwnMetric_UsesIt()
  {
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.LoseFat, MetricKey = MetricKeys.WaistCm }).Should().Be(MetricKeys.WaistCm);
  }

  [Fact]
  public void GoalMetric_KindWithAUsualMetric_FallsBackToIt()
  {
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.LoseFat }).Should().Be(MetricKeys.WeightKg);
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.GainMuscle }).Should().Be(MetricKeys.WeightKg);
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.Maintain }).Should().Be(MetricKeys.WeightKg);
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.Recomposition }).Should().Be(MetricKeys.BodyFatPct);
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.Fitness }).Should().Be(MetricKeys.Vo2Max);
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.SleepBetter }).Should().Be(MetricKeys.SleepStage);
  }

  [Fact]
  public void GoalMetric_KindWithoutANumber_IsNull()
  {
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.KeepStrength }).Should().BeNull();
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.ImproveLabs }).Should().BeNull();
    ProgressService.GoalMetric(new Goal { Kind = GoalKinds.Custom }).Should().BeNull();
  }

  [Fact]
  public void WeightView_SeriesWithAStrayReading_ScreensItOutBeforeSmoothing()
  {
    var start = new DateOnly(2026, 10, 1);
    var readings = new[] { 82.0, 82.2, 81.9, 61.0, 82.1, 81.8, 82.0 }
      .Select((v, i) => new DayValue(start.AddDays(i), v))
      .ToList();

    var view = ProgressService.WeightView.From(readings, start.AddDays(6));

    view.SetAside.Should().Be(1);
    view.Kept.Should().HaveCount(6);
    view.Latest.Should().Be(82.0);
    view.TrendNow.Should().BeInRange(81.8, 82.2, "the stray 61 never reached the trend");
    view.TrendOn(start.AddDays(3)).Should().Be(view.Trend[2].Value, "a day with no kept reading reads the trend from the day before");
    view.TrendOn(start.AddDays(-1)).Should().BeNull();
    view.RatePerWeek.Should().BeNull("a week of readings is too short for a rate");
  }
}
