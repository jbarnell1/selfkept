using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Interpret;
using Xunit;

namespace SelfKept.UnitTests.Interpret;

public class BaselineMathTests
{
  [Fact]
  public void Median_OddCount_IsTheMiddleValue()
  {
    BaselineMath.Median([5, 1, 9]).Should().Be(5);
  }

  [Fact]
  public void Median_EvenCount_IsTheMeanOfTheTwoMiddleValues()
  {
    BaselineMath.Median([1, 9, 3, 7]).Should().Be(5);
  }

  [Fact]
  public void Median_Empty_IsNull()
  {
    BaselineMath.Median([]).Should().BeNull();
  }

  [Fact]
  public void Percentile_InterpolatesBetweenRanks()
  {
    // Ranks 0..4 hold 10,20,30,40,50: p25 sits at rank 1.0 (20), p90 at rank 3.6 (40 + 0.6 * 10).
    double[] values = [50, 10, 40, 20, 30];
    BaselineMath.Percentile(values, 25).Should().Be(20);
    BaselineMath.Percentile(values, 90).Should().BeApproximately(46, 1e-9);
    BaselineMath.Percentile(values, 0).Should().Be(10);
    BaselineMath.Percentile(values, 100).Should().Be(50);
  }

  [Fact]
  public void Percentile_SingleValue_IsThatValue()
  {
    BaselineMath.Percentile([42], 10).Should().Be(42);
    BaselineMath.Percentile([42], 90).Should().Be(42);
  }

  [Fact]
  public void Percentile_OutOfRange_Throws()
  {
    var act = () => BaselineMath.Percentile([1, 2], 101);
    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void Slope_OnAKnownLine_IsItsGradient_EvenWithGaps()
  {
    // y = 70 + 0.5 x, sampled on days 0, 1, 3, 7, 27 (gaps keep their distance).
    var points = new[] { (0, 70.0), (1, 70.5), (3, 71.5), (7, 73.5), (27, 83.5) };
    BaselineMath.Slope(points).Should().BeApproximately(0.5, 1e-9);
  }

  [Fact]
  public void Slope_FewerThanTwoPoints_IsNull()
  {
    BaselineMath.Slope([(3, 1.0)]).Should().BeNull();
    BaselineMath.Slope([]).Should().BeNull();
  }

  [Fact]
  public void StandardDeviation_IsTheSampleDeviation()
  {
    BaselineMath.StandardDeviation([2, 4, 4, 4, 5, 5, 7, 9]).Should().BeApproximately(2.138, 0.001);
    BaselineMath.StandardDeviation([3]).Should().BeNull();
  }

  [Fact]
  public void BandDistance_InsideIsZero_OutsideIsInIqrUnits()
  {
    BaselineMath.BandDistance(50, 40, 60, 45, 55).Should().Be(0);
    BaselineMath.BandDistance(30, 40, 60, 45, 55).Should().Be(1, "10 below the band with an IQR of 10");
    BaselineMath.BandDistance(75, 40, 60, 45, 55).Should().Be(1.5);
  }

  [Fact]
  public void BandDistance_FlatMetric_UsesFivePercentOfP75AsTheScale()
  {
    // 28 identical nights of 435 minutes: IQR is 0, so the scale is 21.75 and a 375-minute night is 2.76 units short.
    BaselineMath.Scale(435, 435).Should().BeApproximately(21.75, 1e-9);
    BaselineMath.BandDistance(375, 435, 435, 435, 435).Should().BeApproximately(60 / 21.75, 1e-9);
  }

  [Fact]
  public void Compute_CountsN7AndN28WithGaps_AndUsesTheLast7ForMedian7()
  {
    // 28-day window ending 29 Oct: data on 20 of the days, 5 of the last 7.
    var day = new DateOnly(2026, 10, 29);
    var rows = new List<DailySummary>();
    for (var i = 0; i < 28; i++)
    {
      var d = day.AddDays(-27 + i);
      if (i % 4 == 1 && i < 21) continue; // five gaps in the older part
      if (i is 22 or 25) continue; // two gaps in the last 7
      rows.Add(new DailySummary { PersonId = Guid.Empty, MetricKey = "steps", Day = d, Value = i < 21 ? 8000 : 4000, Count = 1 });
    }
    // Outside the window: must be ignored.
    rows.Add(new DailySummary { PersonId = Guid.Empty, MetricKey = "steps", Day = day.AddDays(-28), Value = 1, Count = 1 });
    rows.Add(new DailySummary { PersonId = Guid.Empty, MetricKey = "steps", Day = day.AddDays(1), Value = 1, Count = 1 });

    var baseline = BaselineService.Compute(Guid.Empty, "steps", day, rows, DateTime.UtcNow)!;

    baseline.N28.Should().Be(21);
    baseline.N7.Should().Be(5);
    baseline.Median7.Should().Be(4000);
    baseline.Median28.Should().Be(8000);
    baseline.ComputedFor.Should().Be(day);
    baseline.P10_28.Should().BeLessThan(baseline.P90_28!.Value);
    baseline.IsEstablished.Should().BeTrue();
  }

  [Fact]
  public void Compute_NothingInTheWindow_IsNull()
  {
    var day = new DateOnly(2026, 10, 29);
    var rows = new[] { new DailySummary { MetricKey = "steps", Day = day.AddDays(-40), Value = 1 } };
    BaselineService.Compute(Guid.Empty, "steps", day, rows, DateTime.UtcNow).Should().BeNull();
  }
}
