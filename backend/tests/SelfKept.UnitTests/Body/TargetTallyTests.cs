using FluentAssertions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Body;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>
/// The tally's one rule: nothing logged is unknown, not a pass. Days are counted back from
/// <see cref="Today"/>; the week array is oldest first, so index 6 is today and index 5 yesterday.
/// </summary>
public class TargetTallyTests
{
  private static readonly DateOnly Today = new(2026, 11, 10);
  private static readonly Guid Person = Guid.NewGuid();

  private static DailyTarget Target(string kind, string comparator, double value, string period, string? metric = null, string? tag = null) => new()
  {
    Id = Guid.NewGuid(),
    PersonId = Person,
    Label = "Target",
    Kind = kind,
    MetricKey = metric,
    Tag = tag,
    Comparator = comparator,
    Value = value,
    Period = period,
    Active = true,
  };

  private static TargetTallyDto Compute(DailyTarget target, TallyData data) => TargetTally.Compute(
    target,
    new TargetDto(target.Id, null, target.Label, target.Kind, target.MetricKey, target.Tag, target.Comparator, target.Value, target.Period, true, 0, string.Empty),
    Today,
    data);

  private static TallyData Metric(string key, params (int DaysAgo, double Value)[] days) => new(
    new Dictionary<string, IReadOnlyDictionary<DateOnly, double>>
    {
      [key] = days.ToDictionary(d => Today.AddDays(-d.DaysAgo), d => d.Value),
    },
    null,
    null);

  private static TallyData Journal(params (int DaysAgo, string[] Tags)[] entries) => new(
    new Dictionary<string, IReadOnlyDictionary<DateOnly, double>>(),
    entries.Select(e => new JournalEntry
    {
      Id = Guid.NewGuid(),
      PersonId = Person,
      Day = Today.AddDays(-e.DaysAgo),
      Kind = JournalKinds.Food,
      Tags = e.Tags,
      Source = JournalSources.Chip,
    }).ToList(),
    null);

  private static TallyData Lifts(params int[] daysAgo) => new(
    new Dictionary<string, IReadOnlyDictionary<DateOnly, double>>(),
    null,
    daysAgo.Select(d => new StrengthSet { Id = Guid.NewGuid(), PersonId = Person, Day = Today.AddDays(-d), Lift = Guid.NewGuid().ToString("N"), WeightKg = 60, Reps = 5 }).ToList());

  private static readonly bool?[] AllUnknown = [null, null, null, null, null, null, null];

  // Metric targets, by the day.

  [Fact]
  public void Compute_RunningTotalBelowAtLeastToday_StaysOpen()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtLeast, 130, TargetPeriods.Day, MetricKeys.ProteinG);

    var tally = Compute(target, Metric(MetricKeys.ProteinG, (0, 90)));

    tally.Current.Should().Be(90);
    tally.Met.Should().BeNull("the day is not over, so protein can still reach the target");
    tally.Week[6].Should().BeNull();
  }

  [Fact]
  public void Compute_RunningTotalReachesAtLeastToday_IsMet()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtLeast, 130, TargetPeriods.Day, MetricKeys.ProteinG);

    var tally = Compute(target, Metric(MetricKeys.ProteinG, (0, 130)));

    tally.Met.Should().BeTrue();
    tally.MetDays.Should().Be(1);
    tally.KnownDays.Should().Be(1);
  }

  [Fact]
  public void Compute_RunningTotalBelowAtLeastYesterday_IsNotMet()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtLeast, 130, TargetPeriods.Day, MetricKeys.ProteinG);

    var tally = Compute(target, Metric(MetricKeys.ProteinG, (1, 90)));

    tally.Week[5].Should().BeFalse();
    tally.Current.Should().BeNull("nothing has arrived for today");
    tally.Met.Should().BeNull();
  }

  [Fact]
  public void Compute_RunningTotalUnderAtMostToday_StaysOpen()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtMost, 2200, TargetPeriods.Day, MetricKeys.EnergyIntakeKcal);

    var tally = Compute(target, Metric(MetricKeys.EnergyIntakeKcal, (0, 1500), (1, 2100)));

    tally.Met.Should().BeNull("more can still be eaten today");
    tally.Week[5].Should().BeTrue("yesterday is over and stayed under");
  }

  [Fact]
  public void Compute_RunningTotalOverAtMostToday_IsNotMetAtOnce()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtMost, 2200, TargetPeriods.Day, MetricKeys.EnergyIntakeKcal);

    var tally = Compute(target, Metric(MetricKeys.EnergyIntakeKcal, (0, 2400)));

    tally.Met.Should().BeFalse();
  }

  [Fact]
  public void Compute_SleepBelowAtLeastToday_IsDecidedOnArrival()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtLeast, 420, TargetPeriods.Day, MetricKeys.SleepStage);

    var tally = Compute(target, Metric(MetricKeys.SleepStage, (0, 380)));

    tally.Current.Should().Be(380);
    tally.Met.Should().BeFalse("last night is over when its figure arrives");
  }

  [Fact]
  public void Compute_WeightUnderAtMostToday_IsDecidedOnArrival()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtMost, 85, TargetPeriods.Day, MetricKeys.WeightKg);

    var tally = Compute(target, Metric(MetricKeys.WeightKg, (0, 84.2)));

    tally.Met.Should().BeTrue();
  }

  [Fact]
  public void Compute_MetricDaysWithoutAReading_AreUnknownInTheWeek()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtLeast, 420, TargetPeriods.Day, MetricKeys.SleepStage);

    var tally = Compute(target, Metric(MetricKeys.SleepStage, (6, 450), (4, 400), (0, 430)));

    tally.Week.Should().Equal(true, null, false, null, null, null, true);
    tally.MetDays.Should().Be(2);
    tally.KnownDays.Should().Be(3);
  }

  [Fact]
  public void Compute_MetricTheViewerMayNotRead_IsUnknownThroughout()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtLeast, 8000, TargetPeriods.Day, MetricKeys.Steps);

    var tally = Compute(target, Metric(MetricKeys.ProteinG, (0, 200)));

    tally.Current.Should().BeNull();
    tally.Met.Should().BeNull();
    tally.Week.Should().Equal(AllUnknown);
    tally.KnownDays.Should().Be(0);
    tally.MetDays.Should().Be(0);
  }

  // Metric targets, by the week.

  [Fact]
  public void Compute_WeeklyAverageOverThreeDays_IsJudged()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtLeast, 420, TargetPeriods.Week, MetricKeys.SleepStage);

    var tally = Compute(target, Metric(MetricKeys.SleepStage, (5, 400), (3, 430), (1, 460)));

    tally.Current.Should().BeApproximately(430, 1e-9);
    tally.Met.Should().BeTrue();
    tally.Week.Should().Equal(null, true, null, true, null, true, null);
    tally.KnownDays.Should().Be(TargetTally.WeekMinMetricDays);
  }

  [Fact]
  public void Compute_WeeklyAverageOverTwoDays_ShowsTheAverageButNoVerdict()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtLeast, 420, TargetPeriods.Week, MetricKeys.SleepStage);

    var tally = Compute(target, Metric(MetricKeys.SleepStage, (3, 480), (1, 500)));

    tally.Current.Should().BeApproximately(490, 1e-9);
    tally.Met.Should().BeNull("two nights are too few to speak for a week");
  }

  [Fact]
  public void Compute_WeeklyAverageWithNoReadings_IsUnknown()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtLeast, 420, TargetPeriods.Week, MetricKeys.SleepStage);

    var tally = Compute(target, Metric(MetricKeys.SleepStage));

    tally.Current.Should().BeNull();
    tally.Met.Should().BeNull();
    tally.Week.Should().Equal(AllUnknown);
  }

  [Fact]
  public void Compute_WeeklyAverageAboveAtMost_IsNotMet()
  {
    var target = Target(TargetKinds.Metric, TargetComparators.AtMost, 2200, TargetPeriods.Week, MetricKeys.EnergyIntakeKcal);

    var over = Compute(target, Metric(MetricKeys.EnergyIntakeKcal, (3, 2500), (2, 2300), (1, 2400)));
    var under = Compute(target, Metric(MetricKeys.EnergyIntakeKcal, (3, 2000), (2, 2300), (1, 2100)));

    over.Met.Should().BeFalse();
    under.Met.Should().BeTrue();
  }

  // Day log targets, by the day.

  [Fact]
  public void Compute_NoAlcoholOnALoggedDayWithoutIt_IsMet()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtMost, 0, TargetPeriods.Day, tag: JournalTags.Alcohol);

    var tally = Compute(target, Journal((1, [JournalTags.Vegetables])));

    tally.Week[5].Should().BeTrue();
  }

  [Fact]
  public void Compute_NoAlcoholOnADayWithNothingLogged_IsUnknownNotAPass()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtMost, 0, TargetPeriods.Day, tag: JournalTags.Alcohol);

    var tally = Compute(target, Journal((1, [JournalTags.Vegetables])));

    tally.Week[4].Should().BeNull();
    tally.Week[6].Should().BeNull();
    tally.KnownDays.Should().Be(1);
  }

  [Fact]
  public void Compute_NoAlcoholOnADayWithADrink_IsNotMet()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtMost, 0, TargetPeriods.Day, tag: JournalTags.Alcohol);

    var tally = Compute(target, Journal((2, [JournalTags.Alcohol]), (0, [JournalTags.Alcohol, JournalTags.LateMeal])));

    tally.Week[4].Should().BeFalse();
    tally.Current.Should().Be(1);
    tally.Met.Should().BeFalse("a drink today has already broken the target");
  }

  [Fact]
  public void Compute_NoAlcoholTodaySoFar_StaysOpen()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtMost, 0, TargetPeriods.Day, tag: JournalTags.Alcohol);

    var tally = Compute(target, Journal((0, [JournalTags.Vegetables])));

    tally.Current.Should().Be(0);
    tally.Met.Should().BeNull("the evening has not happened yet");
  }

  [Fact]
  public void Compute_TaggedEntryTodayForAtLeast_IsMetAtOnce()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtLeast, 2, TargetPeriods.Day, tag: JournalTags.HighProtein);

    var tally = Compute(target, Journal((0, [JournalTags.HighProtein]), (0, [JournalTags.HighProtein, JournalTags.Vegetables]), (1, [JournalTags.HighProtein])));

    tally.Current.Should().Be(2);
    tally.Met.Should().BeTrue();
    tally.Week[5].Should().BeFalse("one such meal yesterday was short of two");
  }

  [Fact]
  public void Compute_DayLogTheViewerMayNotRead_IsUnknownThroughout()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtMost, 0, TargetPeriods.Day, tag: JournalTags.Alcohol);

    var tally = Compute(target, Lifts(0, 1));

    tally.Current.Should().BeNull();
    tally.Met.Should().BeNull();
    tally.Week.Should().Equal(AllUnknown);
    tally.KnownDays.Should().Be(0);
  }

  // Day log targets, by the week.

  [Fact]
  public void Compute_WeeklyAtMostWithThreeLoggedDays_IsMet()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtMost, 0, TargetPeriods.Week, tag: JournalTags.LateCaffeine);

    var tally = Compute(target, Journal((4, [JournalTags.Caffeine]), (2, []), (1, [JournalTags.Vegetables])));

    tally.Current.Should().Be(0);
    tally.Met.Should().BeTrue();
    tally.Week.Should().Equal(null, null, false, null, false, false, null);
  }

  [Fact]
  public void Compute_WeeklyAtMostWithTwoLoggedDays_HasNoVerdictYet()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtMost, 0, TargetPeriods.Week, tag: JournalTags.LateCaffeine);

    var tally = Compute(target, Journal((2, []), (1, [JournalTags.Vegetables])));

    tally.Current.Should().Be(0);
    tally.Met.Should().BeNull("two logged days are too few to believe a week of none");
  }

  [Fact]
  public void Compute_WeeklyAtMostExceeded_IsNotMetHoweverFewDaysWereLogged()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtMost, 1, TargetPeriods.Week, tag: JournalTags.Alcohol);

    var tally = Compute(target, Journal((3, [JournalTags.Alcohol]), (3, [JournalTags.Alcohol])));

    tally.Current.Should().Be(2);
    tally.Met.Should().BeFalse();
    tally.Week[3].Should().BeTrue("the week array marks the days that added to the count");
  }

  [Fact]
  public void Compute_WeeklyAtLeastReached_IsMet()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtLeast, 3, TargetPeriods.Week, tag: JournalTags.Vegetables);

    var tally = Compute(target, Journal((5, [JournalTags.Vegetables]), (2, [JournalTags.Vegetables]), (0, [JournalTags.Vegetables])));

    tally.Current.Should().Be(3);
    tally.Met.Should().BeTrue();
    tally.MetDays.Should().Be(3);
  }

  [Fact]
  public void Compute_WeeklyAtLeastShortWithSomethingLogged_IsNotMet()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtLeast, 3, TargetPeriods.Week, tag: JournalTags.Vegetables);

    var tally = Compute(target, Journal((5, [JournalTags.Vegetables]), (2, [JournalTags.LateMeal])));

    tally.Current.Should().Be(1);
    tally.Met.Should().BeFalse();
  }

  [Fact]
  public void Compute_WeeklyAtLeastWithNothingLogged_IsUnknown()
  {
    var target = Target(TargetKinds.Journal, TargetComparators.AtLeast, 3, TargetPeriods.Week, tag: JournalTags.Vegetables);

    var tally = Compute(target, Journal());

    tally.Current.Should().Be(0);
    tally.Met.Should().BeNull();
    tally.Week.Should().Equal(AllUnknown);
  }

  // Strength targets.

  [Fact]
  public void Compute_LiftingDaysReached_IsMet()
  {
    var target = Target(TargetKinds.Strength, TargetComparators.AtLeast, 2, TargetPeriods.Week);

    var tally = Compute(target, Lifts(5, 5, 5, 1));

    tally.Current.Should().Be(2, "three sets on one day are one session");
    tally.Met.Should().BeTrue();
    tally.Week.Should().Equal(false, true, false, false, false, true, false);
    tally.MetDays.Should().Be(2);
    tally.KnownDays.Should().Be(7);
  }

  [Fact]
  public void Compute_LiftingDaysShort_IsNotMet()
  {
    var target = Target(TargetKinds.Strength, TargetComparators.AtLeast, 2, TargetPeriods.Week);

    var tally = Compute(target, Lifts(3));

    tally.Current.Should().Be(1);
    tally.Met.Should().BeFalse();
  }

  [Fact]
  public void Compute_LiftingDaysOverAtMost_IsNotMet()
  {
    var target = Target(TargetKinds.Strength, TargetComparators.AtMost, 3, TargetPeriods.Week);

    var tally = Compute(target, Lifts(6, 4, 2, 0));

    tally.Current.Should().Be(4);
    tally.Met.Should().BeFalse();
  }

  [Fact]
  public void Compute_LiftingDaysWithAnEmptyLog_IsUnknownNotAWeekOfRestDays()
  {
    var target = Target(TargetKinds.Strength, TargetComparators.AtLeast, 2, TargetPeriods.Week);

    var tally = Compute(target, Lifts());

    tally.Current.Should().Be(0);
    tally.Met.Should().BeNull();
    tally.Week.Should().Equal(AllUnknown);
    tally.KnownDays.Should().Be(0);
  }

  [Fact]
  public void Compute_DailyAtMostLiftsWithAnEmptyLog_IsNotAPass()
  {
    var target = Target(TargetKinds.Strength, TargetComparators.AtMost, 0, TargetPeriods.Day);

    var tally = Compute(target, Lifts());

    tally.Met.Should().BeNull();
    tally.Week.Should().Equal(AllUnknown);
    tally.MetDays.Should().Be(0);
  }

  [Fact]
  public void Compute_DailyLiftsToday_AreCountedAndOpenUntilReached()
  {
    var target = Target(TargetKinds.Strength, TargetComparators.AtLeast, 2, TargetPeriods.Day);

    var one = Compute(target, Lifts(0, 1, 1));
    var two = Compute(target, Lifts(0, 0));

    one.Current.Should().Be(1);
    one.Met.Should().BeNull("another lift can still be logged today");
    one.Week[5].Should().BeTrue();
    one.Week[4].Should().BeFalse("a day with no set is a rest day once a log is kept");
    two.Met.Should().BeTrue();
  }

  [Fact]
  public void Compute_StrengthLogTheViewerMayNotRead_IsUnknownThroughout()
  {
    var target = Target(TargetKinds.Strength, TargetComparators.AtLeast, 2, TargetPeriods.Week);

    var tally = Compute(target, Journal((0, [JournalTags.Lifted])));

    tally.Current.Should().BeNull();
    tally.Met.Should().BeNull();
    tally.Week.Should().Equal(AllUnknown);
    tally.KnownDays.Should().Be(0);
  }

  // Units.

  [Fact]
  public void UnitFor_EachKind_NamesWhatTheValueCounts()
  {
    TargetTally.UnitFor(Target(TargetKinds.Metric, TargetComparators.AtLeast, 1, TargetPeriods.Day, MetricKeys.ProteinG), "g").Should().Be("g");
    TargetTally.UnitFor(Target(TargetKinds.Metric, TargetComparators.AtLeast, 1, TargetPeriods.Day, MetricKeys.ProteinG), null).Should().BeEmpty();
    TargetTally.UnitFor(Target(TargetKinds.Journal, TargetComparators.AtMost, 0, TargetPeriods.Week, tag: JournalTags.Alcohol), null).Should().Be("entries");
    TargetTally.UnitFor(Target(TargetKinds.Strength, TargetComparators.AtLeast, 2, TargetPeriods.Week), null).Should().Be("days");
    TargetTally.UnitFor(Target(TargetKinds.Strength, TargetComparators.AtLeast, 2, TargetPeriods.Day), null).Should().Be("lifts");
  }
}
