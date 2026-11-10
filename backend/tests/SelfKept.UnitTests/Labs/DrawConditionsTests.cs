using FluentAssertions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services.Labs;
using Xunit;

namespace SelfKept.UnitTests.Labs;

/// <summary>Draw conditions: the Rythm pre-fill, validation, replacement, and the cystatin C age split.</summary>
public class DrawConditionsTests
{
  private static LabReport Report(string provider) => new() { Id = Guid.NewGuid(), Provider = provider };

  [Fact]
  public void ApplyOnCreate_RythmWithNothingGiven_IsAMorningFastOfEightHours_Defaulted()
  {
    var report = Report(LabProviders.Rythm);

    DrawConditions.ApplyOnCreate(report, null);

    report.Fasting.Should().Be(FastingStates.Yes);
    report.FastingHours.Should().Be(DrawConditions.RythmFastingHours);
    report.DrawPeriod.Should().Be(DrawPeriods.Morning);
    report.DrawTime.Should().BeNull();
    report.ConditionsDefaulted.Should().BeTrue();
    DrawConditions.ToDto(report).Should().Be(new DrawConditionsDto("yes", 8, null, "morning", null, null, true));
  }

  [Fact]
  public void ApplyOnCreate_RythmWithAnAfternoonTime_KeepsTheFastDefault_ButIsNotDefaulted()
  {
    var report = Report(LabProviders.Rythm);

    DrawConditions.ApplyOnCreate(report, new DrawConditionsInput(null, null, "15:40", null, null, null));

    report.DrawPeriod.Should().Be(DrawPeriods.Afternoon, "the time decides the period");
    report.DrawTime.Should().Be(new TimeOnly(15, 40));
    report.Fasting.Should().Be(FastingStates.Yes);
    report.ConditionsDefaulted.Should().BeFalse("the person changed something");
  }

  [Fact]
  public void ApplyOnCreate_RythmNotFasting_HasNoFastingHours()
  {
    var report = Report(LabProviders.Rythm);

    DrawConditions.ApplyOnCreate(report, new DrawConditionsInput("no", null, null, null, null, null));

    report.Fasting.Should().Be(FastingStates.No);
    report.FastingHours.Should().BeNull();
  }

  [Fact]
  public void ApplyOnCreate_ManualWithNothingGiven_KnowsNothing()
  {
    var report = Report(LabProviders.Manual);

    DrawConditions.ApplyOnCreate(report, null);

    DrawConditions.ToDto(report).Should().Be(new DrawConditionsDto("unknown", null, null, "unknown", null, null, false));
  }

  [Fact]
  public void Replace_ClearsTheDefaults_AndTakesExactlyWhatWasGiven()
  {
    var report = Report(LabProviders.Rythm);
    DrawConditions.ApplyOnCreate(report, null);

    DrawConditions.Replace(report, new DrawConditionsInput("unknown", null, null, "evening", true, false));

    DrawConditions.ToDto(report).Should().Be(new DrawConditionsDto("unknown", null, null, "evening", true, false, false));
  }

  [Theory]
  [InlineData("sometimes", null, null, null)]
  [InlineData(null, 80, null, null)]
  [InlineData("no", 10, null, null)]
  [InlineData(null, null, "25:00", null)]
  [InlineData(null, null, "noon", null)]
  [InlineData(null, null, null, "night")]
  public void Replace_BadValues_AreValidationErrors(string? fasting, int? hours, string? time, string? period)
  {
    var act = () => DrawConditions.Replace(Report(LabProviders.Manual), new DrawConditionsInput(fasting, hours, time, period, null, null));

    act.Should().Throw<DomainValidationException>();
  }

  [Theory]
  [InlineData(7, 59, "morning")]
  [InlineData(11, 59, "morning")]
  [InlineData(12, 0, "afternoon")]
  [InlineData(17, 59, "afternoon")]
  [InlineData(18, 0, "evening")]
  public void DrawPeriod_For_SplitsTheDay(int hour, int minute, string expected)
  {
    DrawPeriods.For(new TimeOnly(hour, minute)).Should().Be(expected);
  }

  [Fact]
  public void ForAge_CystatinC_YoungerOlderAndUnknown()
  {
    var row = new LabReference { MetricKey = LabMetricKeys.CystatinC, Low = 0.63, High = 1.03, AgeSplitAt = 50, OlderLow = 0.67, OlderHigh = 1.21 };

    LabReferenceLookup.ForAge(row, 49).Should().BeSameAs(row);
    var older = LabReferenceLookup.ForAge(row, 50)!;
    (older.Low, older.High).Should().Be((0.67, 1.21));
    var unknown = LabReferenceLookup.ForAge(row, null)!;
    (unknown.Low, unknown.High).Should().Be((0.63, 1.21), "without an age only a value outside both intervals is flagged");
  }

  [Fact]
  public void ForAge_RowWithoutASplit_IsUnchanged()
  {
    var row = new LabReference { MetricKey = LabMetricKeys.Alt, Low = 7, High = 55 };

    LabReferenceLookup.ForAge(row, 70).Should().BeSameAs(row);
    LabReferenceLookup.ForAge(null, 70).Should().BeNull();
  }

  [Theory]
  [InlineData("1976-11-10", "2026-11-10", 50)]
  [InlineData("1976-11-11", "2026-11-10", 49)]
  [InlineData(null, "2026-11-10", null)]
  public void AgeOn_CountsWholeYears(string? birth, string day, int? expected)
  {
    LabReferenceLookup.AgeOn(birth == null ? null : DateOnly.Parse(birth), DateOnly.Parse(day)).Should().Be(expected);
  }
}
