using FluentAssertions;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Services.Body;
using Xunit;

namespace SelfKept.UnitTests.Body;

public class BodyUnitsTests
{
  [Fact]
  public void Format_KilogramsForAMetricReader_StayKilograms()
  {
    BodyUnits.Format(82.4, "kg", UnitSystem.Metric).Should().Be("82.4 kg");
  }

  [Fact]
  public void Format_KilogramsForAnImperialReader_BecomePounds()
  {
    BodyUnits.Format(82.4, "kg", UnitSystem.Imperial).Should().Be("181.7 lb");
  }

  [Fact]
  public void Format_CentimetresForAMetricReader_StayCentimetres()
  {
    BodyUnits.Format(90, "cm", UnitSystem.Metric).Should().Be("90 cm");
  }

  [Fact]
  public void Format_CentimetresForAnImperialReader_BecomeInches()
  {
    BodyUnits.Format(90, "cm", UnitSystem.Imperial).Should().Be("35.4 in");
  }

  [Fact]
  public void Format_WholeNumber_HasNoDecimals()
  {
    BodyUnits.Format(82.0, "kg", UnitSystem.Metric).Should().Be("82 kg");
    BodyUnits.Format(82.04, "kg", UnitSystem.Metric).Should().Be("82 kg", "it is whole once rounded to one decimal");
  }

  [Fact]
  public void Format_Thousands_AreGrouped()
  {
    BodyUnits.Format(1250, "kcal", UnitSystem.Metric).Should().Be("1,250 kcal");
  }

  [Fact]
  public void Format_MoreDecimalsAskedFor_AreKept()
  {
    BodyUnits.Format(0.375, "kg", UnitSystem.Metric, 2).Should().Be("0.38 kg");
  }

  [Fact]
  public void Format_MinutesUnderAnHour_AreMinutes()
  {
    BodyUnits.Format(45, "min", UnitSystem.Metric).Should().Be("45 min");
  }

  [Fact]
  public void Format_MinutesOverAnHour_AreHoursAndMinutes()
  {
    BodyUnits.Format(450, "min", UnitSystem.Metric).Should().Be("7 h 30 min");
    BodyUnits.Format(450, "min", UnitSystem.Imperial).Should().Be("7 h 30 min");
  }

  [Fact]
  public void Format_WholeHours_LeaveTheMinutesOut()
  {
    BodyUnits.Format(420, "min", UnitSystem.Metric).Should().Be("7 h");
    BodyUnits.Format(59.6, "min", UnitSystem.Metric).Should().Be("1 h");
  }

  [Fact]
  public void Format_Percent_IsTheSameForBothSystems()
  {
    BodyUnits.Format(18.5, "%", UnitSystem.Metric).Should().Be("18.5 %");
    BodyUnits.Format(18.5, "%", UnitSystem.Imperial).Should().Be("18.5 %");
  }

  [Fact]
  public void Format_NoUnit_IsTheNumberAlone()
  {
    BodyUnits.Format(7.25, null, UnitSystem.Metric).Should().Be("7.3");
    BodyUnits.Format(5, string.Empty, UnitSystem.Metric).Should().Be("5");
  }

  [Fact]
  public void Format_Count_IsAWholeNumberWithoutAUnit()
  {
    BodyUnits.Format(8432, "count", UnitSystem.Metric).Should().Be("8,432");
    BodyUnits.Format(12.6, "count", UnitSystem.Metric).Should().Be("13");
  }

  [Fact]
  public void Change_Loss_ReadsDown()
  {
    BodyUnits.Change(-0.4, "kg", UnitSystem.Metric).Should().Be("0.4 kg down");
  }

  [Fact]
  public void Change_Gain_ReadsUp()
  {
    BodyUnits.Change(0.4, "kg", UnitSystem.Metric).Should().Be("0.4 kg up");
  }

  [Fact]
  public void Change_ForAnImperialReader_IsInPounds()
  {
    BodyUnits.Change(-0.5, "kg", UnitSystem.Imperial).Should().Be("1.1 lb down");
    BodyUnits.Change(-0.5, "kg", UnitSystem.Imperial, 2).Should().Be("1.10 lb down");
  }
}
