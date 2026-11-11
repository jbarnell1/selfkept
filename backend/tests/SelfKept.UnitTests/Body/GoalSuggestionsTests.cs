using FluentAssertions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Services.Body;
using Xunit;

namespace SelfKept.UnitTests.Body;

public class GoalSuggestionsTests
{
  private static BodySnapshotDto Snapshot(double? weightKg, double? trendWeightKg = null) =>
    new(weightKg, trendWeightKg, null, null, null, null, null, null, null, null, null, null, 180, 0, null);

  private static GoalSuggestionDto Suggestion(IReadOnlyList<GoalSuggestionDto> list, string kind) => list.Single(s => s.Kind == kind);

  [Fact]
  public void For_AnyBody_OffersTheFiveGoalsInOrder()
  {
    var list = GoalSuggestions.For(Snapshot(82.4), UnitSystem.Metric);

    list.Select(s => s.Kind).Should().Equal(
      GoalKinds.LoseFat, GoalKinds.GainMuscle, GoalKinds.KeepStrength, GoalKinds.SleepBetter, GoalKinds.Maintain);
    list.Select(s => s.Title).Should().Equal("Lose fat", "Gain muscle", "Keep strength", "Sleep better", "Hold steady");
    list.Should().OnlyContain(s => s.Why.Length > 0);
  }

  [Fact]
  public void For_KnownWeight_SetsAProteinTargetFromIt()
  {
    var loseFat = Suggestion(GoalSuggestions.For(Snapshot(82.4), UnitSystem.Metric), GoalKinds.LoseFat);

    loseFat.Targets.Select(t => t.Label).Should().Equal("Protein", "Lifting days", "Sleep");
    loseFat.Targets[0].Should().Be(new SuggestedTargetDto(
      "Protein", TargetKinds.Metric, MetricKeys.ProteinG, null, TargetComparators.AtLeast, 130, TargetPeriods.Day, "g"));
    loseFat.MetricKey.Should().Be(MetricKeys.WeightKg);
    loseFat.StartValue.Should().Be(82.4);
  }

  [Fact]
  public void For_TrendWeightKnown_PrefersItToTheLatestReading()
  {
    var loseFat = Suggestion(GoalSuggestions.For(Snapshot(82.4, trendWeightKg: 75.04), UnitSystem.Metric), GoalKinds.LoseFat);

    loseFat.StartValue.Should().Be(75.0, "the start value is rounded to one decimal");
    loseFat.Targets[0].Value.Should().Be(120);
  }

  [Fact]
  public void For_NoWeight_LeavesTheProteinTargetOut()
  {
    var list = GoalSuggestions.For(Snapshot(null), UnitSystem.Metric);

    Suggestion(list, GoalKinds.LoseFat).Targets.Select(t => t.Label).Should().Equal("Lifting days", "Sleep");
    Suggestion(list, GoalKinds.GainMuscle).Targets.Select(t => t.Label).Should().Equal("Lifting days", "Sleep");
    Suggestion(list, GoalKinds.KeepStrength).Targets.Select(t => t.Label).Should().Equal("Lifting days");
    Suggestion(list, GoalKinds.LoseFat).StartValue.Should().BeNull();
    Suggestion(list, GoalKinds.LoseFat).Why.Should().Contain("Add a weight reading to see what that is for you.");
  }

  [Fact]
  public void For_MetricReader_WordsTheWeeklyLossInKilograms()
  {
    var loseFat = Suggestion(GoalSuggestions.For(Snapshot(82.4), UnitSystem.Metric), GoalKinds.LoseFat);

    loseFat.Why.Should().Contain("For you that is 0.4 kg to 0.8 kg a week.");
  }

  [Fact]
  public void For_ImperialReader_WordsTheWeeklyLossInPounds()
  {
    var loseFat = Suggestion(GoalSuggestions.For(Snapshot(82.4), UnitSystem.Imperial), GoalKinds.LoseFat);

    loseFat.Why.Should().Contain("For you that is 0.9 lb to 1.8 lb a week.");
    loseFat.Why.Should().NotContain(" kg to ");
    loseFat.StartValue.Should().Be(82.4, "numbers on the wire stay metric whatever the wording");
  }

  [Fact]
  public void For_KeepStrength_PutsLiftingFirstAndHasNoNumber()
  {
    var keep = Suggestion(GoalSuggestions.For(Snapshot(82.4), UnitSystem.Metric), GoalKinds.KeepStrength);

    keep.Targets.Select(t => t.Label).Should().Equal("Lifting days", "Protein");
    keep.MetricKey.Should().BeNull();
    keep.StartValue.Should().BeNull();
    keep.Targets[0].Should().Be(new SuggestedTargetDto(
      "Lifting days", TargetKinds.Strength, null, null, TargetComparators.AtLeast, GoalSuggestions.LiftingDaysPerWeek, TargetPeriods.Week, "days"));
  }

  [Fact]
  public void For_SleepBetter_AsksForSevenHoursAndNoLateCaffeine()
  {
    var sleep = Suggestion(GoalSuggestions.For(Snapshot(82.4), UnitSystem.Metric), GoalKinds.SleepBetter);

    sleep.MetricKey.Should().Be(MetricKeys.SleepStage);
    sleep.StartValue.Should().BeNull();
    sleep.Targets.Should().Equal(
      new SuggestedTargetDto("Sleep", TargetKinds.Metric, MetricKeys.SleepStage, null, TargetComparators.AtLeast, 420, TargetPeriods.Day, "min"),
      new SuggestedTargetDto("No late caffeine", TargetKinds.Journal, null, JournalTags.LateCaffeine, TargetComparators.AtMost, 0, TargetPeriods.Week, "entries"));
  }

  [Fact]
  public void For_Maintain_HasNoProteinTarget()
  {
    var maintain = Suggestion(GoalSuggestions.For(Snapshot(82.4), UnitSystem.Metric), GoalKinds.Maintain);

    maintain.Targets.Select(t => t.Label).Should().Equal("Lifting days", "Sleep");
    maintain.MetricKey.Should().Be(MetricKeys.WeightKg);
  }

  [Fact]
  public void ProteinTarget_AnyWeight_RoundsToTheNearestFiveGrams()
  {
    GoalSuggestions.ProteinTarget(82).Should().Be(130, "131.2 g rounds down");
    GoalSuggestions.ProteinTarget(70).Should().Be(110, "112 g rounds down");
    GoalSuggestions.ProteinTarget(75).Should().Be(120);
    GoalSuggestions.ProteinTarget(77).Should().Be(125, "123.2 g rounds up");
    GoalSuggestions.ProteinTarget(100).Should().Be(160);
  }
}
