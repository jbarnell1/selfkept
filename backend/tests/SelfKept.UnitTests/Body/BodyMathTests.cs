using FluentAssertions;
using SelfKept.Infrastructure.Services.Body;
using Xunit;

namespace SelfKept.UnitTests.Body;

public class BodyMathTests
{
  private static readonly DateOnly Start = new(2026, 10, 1);

  private static List<DayValue> Series(params double[] values) =>
    values.Select((v, i) => new DayValue(Start.AddDays(i), v)).ToList();

  [Fact]
  public void Bmi_WeightAndHeight_IsWeightOverHeightSquared()
  {
    BodyMath.Bmi(81, 180).Should().BeApproximately(25.0, 1e-9);
  }

  [Fact]
  public void Bmi_HeightMissingOrZero_IsNull()
  {
    BodyMath.Bmi(81, null).Should().BeNull();
    BodyMath.Bmi(81, 0).Should().BeNull();
    BodyMath.Bmi(null, 180).Should().BeNull();
  }

  [Fact]
  public void FatMassKg_WeightAndBodyFat_IsTheirProduct()
  {
    BodyMath.FatMassKg(80, 25).Should().BeApproximately(20.0, 1e-9);
  }

  [Fact]
  public void FatMassKg_BodyFatMissing_IsNull()
  {
    BodyMath.FatMassKg(80, null).Should().BeNull();
    BodyMath.FatMassKg(null, 25).Should().BeNull();
  }

  [Fact]
  public void LeanMassKg_WeightAndBodyFat_IsTheRest()
  {
    BodyMath.LeanMassKg(80, 25).Should().BeApproximately(60.0, 1e-9);
  }

  [Fact]
  public void LeanMassKg_BodyFatMissing_IsNull()
  {
    BodyMath.LeanMassKg(80, null).Should().BeNull();
    BodyMath.LeanMassKg(null, 25).Should().BeNull();
  }

  [Fact]
  public void LeanMassIndex_LeanMassAndHeight_IsLeanOverHeightSquared()
  {
    BodyMath.LeanMassIndex(64.8, 180).Should().BeApproximately(20.0, 1e-9);
  }

  [Fact]
  public void LeanMassIndex_HeightMissingOrZero_IsNull()
  {
    BodyMath.LeanMassIndex(64.8, null).Should().BeNull();
    BodyMath.LeanMassIndex(64.8, 0).Should().BeNull();
    BodyMath.LeanMassIndex(null, 180).Should().BeNull();
  }

  [Fact]
  public void WaistToHeight_WaistAndHeight_IsTheRatio()
  {
    BodyMath.WaistToHeight(90, 180).Should().BeApproximately(0.5, 1e-9);
  }

  [Fact]
  public void WaistToHeight_HeightMissingOrZero_IsNull()
  {
    BodyMath.WaistToHeight(90, null).Should().BeNull();
    BodyMath.WaistToHeight(90, 0).Should().BeNull();
    BodyMath.WaistToHeight(null, 180).Should().BeNull();
  }

  [Fact]
  public void Screen_SecondPersonOnTheScale_IsSetAside()
  {
    var readings = Series(82.0, 82.2, 81.9, 61.0, 82.1, 81.8, 82.0);

    var (kept, aside) = BodyMath.Screen(readings);

    aside.Should().ContainSingle().Which.Value.Should().Be(61.0);
    kept.Select(r => r.Value).Should().Equal(82.0, 82.2, 81.9, 82.1, 81.8, 82.0);
  }

  [Fact]
  public void Screen_OutlierAtTheEnd_IsSetAside()
  {
    var readings = Series(82.0, 82.2, 81.9, 82.1, 95.0);

    var (kept, aside) = BodyMath.Screen(readings);

    aside.Should().ContainSingle().Which.Day.Should().Be(Start.AddDays(4));
    kept.Should().HaveCount(4);
  }

  [Fact]
  public void Screen_SteadyLoss_KeepsEveryReading()
  {
    var readings = Enumerable.Range(0, 30).Select(i => new DayValue(Start.AddDays(i), 90 - i * 0.15)).ToList();

    var (kept, aside) = BodyMath.Screen(readings);

    aside.Should().BeEmpty();
    kept.Should().HaveCount(30);
  }

  [Fact]
  public void Screen_FewerThanFourReadings_SetsNothingAside()
  {
    var readings = Series(82.0, 61.0, 82.1);

    var (kept, aside) = BodyMath.Screen(readings);

    aside.Should().BeEmpty();
    kept.Should().HaveCount(3);
  }

  [Fact]
  public void Trend_FirstReading_IsTheReadingItself()
  {
    BodyMath.Trend(Series(82.4)).Should().ContainSingle().Which.Value.Should().Be(82.4);
  }

  [Fact]
  public void Trend_NextDay_TakesUpATenthOfTheChange()
  {
    var trend = BodyMath.Trend(Series(80, 81));

    trend[1].Value.Should().BeApproximately(80.1, 1e-9);
    trend[1].Day.Should().Be(Start.AddDays(1));
  }

  [Fact]
  public void Trend_GapOfSeveralDays_TakesUpMoreOfTheChange()
  {
    var readings = new List<DayValue> { new(Start, 80), new(Start.AddDays(3), 81) };

    var trend = BodyMath.Trend(readings);

    trend[1].Value.Should().BeApproximately(80 + (1 - Math.Pow(0.9, 3)), 1e-9);
  }

  [Fact]
  public void Trend_NoReadings_IsEmpty()
  {
    BodyMath.Trend([]).Should().BeEmpty();
  }

  [Fact]
  public void WeeklyRate_SteadyLossOverThreeWeeks_IsTheLossPerWeek()
  {
    var today = Start.AddDays(21);
    var readings = Enumerable.Range(0, 8).Select(i => new DayValue(Start.AddDays(i * 3), 90 - i * 0.3)).ToList();

    BodyMath.WeeklyRate(readings, today).Should().BeApproximately(-0.7, 1e-9);
  }

  [Fact]
  public void WeeklyRate_FewerThanSixReadings_IsNull()
  {
    var today = Start.AddDays(20);
    var readings = Enumerable.Range(0, 5).Select(i => new DayValue(Start.AddDays(i * 5), 90 - i)).ToList();

    BodyMath.WeeklyRate(readings, today).Should().BeNull();
  }

  [Fact]
  public void WeeklyRate_ReadingsSpanLessThanTwoWeeks_IsNull()
  {
    var today = Start.AddDays(13);
    var readings = Enumerable.Range(0, 14).Select(i => new DayValue(Start.AddDays(i), 90 - i * 0.1)).ToList();

    BodyMath.WeeklyRate(readings, today).Should().BeNull();
  }

  [Fact]
  public void WeeklyRate_ReadingsSpanExactlyTwoWeeks_IsGiven()
  {
    var today = Start.AddDays(14);
    var readings = Enumerable.Range(0, 15).Select(i => new DayValue(Start.AddDays(i), 90 - i * 0.1)).ToList();

    BodyMath.WeeklyRate(readings, today).Should().BeApproximately(-0.7, 1e-9);
  }

  [Fact]
  public void WeeklyRate_OldReadings_AreLeftOutOfTheWindow()
  {
    var today = Start.AddDays(60);
    var old = Enumerable.Range(0, 20).Select(i => new DayValue(Start.AddDays(i), 95 - i * 0.5));
    var recent = new[] { new DayValue(today.AddDays(-20), 84), new DayValue(today.AddDays(-10), 84), new DayValue(today, 84) };

    BodyMath.WeeklyRate([.. old, .. recent], today).Should().BeNull("only three readings fall inside the last 28 days");
  }

  [Fact]
  public void Slope_EveryReadingOnTheSameDay_IsNull()
  {
    BodyMath.Slope([new DayValue(Start, 80), new DayValue(Start, 81)]).Should().BeNull();
    BodyMath.Slope([new DayValue(Start, 80)]).Should().BeNull();
  }

  [Fact]
  public void LeanShareOfLoss_FatAndLeanBothLost_IsTheLeanShare()
  {
    // 90 kg at 25 % is 67.5 kg lean; 85 kg at 22 % is 66.3 kg lean: 1.2 of the 5 kg lost was lean.
    BodyMath.LeanShareOfLoss(90, 25, 85, 22).Should().BeApproximately(0.24, 1e-9);
  }

  [Fact]
  public void LeanShareOfLoss_LessThanTwoKgLost_IsNull()
  {
    BodyMath.LeanShareOfLoss(90, 25, 88.1, 24).Should().BeNull();
    BodyMath.LeanShareOfLoss(90, 25, 92, 26).Should().BeNull("weight went up, so nothing was lost");
  }

  [Fact]
  public void LeanShareOfLoss_OutsideZeroToOne_IsClamped()
  {
    BodyMath.LeanShareOfLoss(90, 25, 85, 30).Should().Be(1, "more lean mass than total weight was lost on paper");
    BodyMath.LeanShareOfLoss(90, 30, 85, 20).Should().Be(0, "lean mass went up while weight came down");
  }

  [Fact]
  public void Median_OddEvenAndEmpty_AreHandled()
  {
    BodyMath.Median([3, 1, 2]).Should().Be(2);
    BodyMath.Median([4, 1, 3, 2]).Should().Be(2.5);
    BodyMath.Median([]).Should().Be(0);
  }
}

public class StrengthMathTests
{
  [Fact]
  public void EstimatedMaxKg_OneRep_IsTheWeight()
  {
    StrengthMath.EstimatedMaxKg(140, 1).Should().Be(140);
  }

  [Fact]
  public void EstimatedMaxKg_FiveReps_FollowsEpley()
  {
    StrengthMath.EstimatedMaxKg(100, 5).Should().BeApproximately(100 * (1 + 5 / 30.0), 1e-9);
  }

  [Fact]
  public void EstimatedMaxKg_TwelveReps_IsStillGiven()
  {
    StrengthMath.EstimatedMaxKg(100, StrengthMath.MaxRepsForEstimate).Should().BeApproximately(140, 1e-9);
  }

  [Fact]
  public void EstimatedMaxKg_MoreThanTwelveReps_IsNull()
  {
    StrengthMath.EstimatedMaxKg(100, StrengthMath.MaxRepsForEstimate + 1).Should().BeNull();
  }

  [Fact]
  public void EstimatedMaxKg_BodyweightSetOrNoReps_IsNull()
  {
    StrengthMath.EstimatedMaxKg(0, 8).Should().BeNull();
    StrengthMath.EstimatedMaxKg(100, 0).Should().BeNull();
  }
}
