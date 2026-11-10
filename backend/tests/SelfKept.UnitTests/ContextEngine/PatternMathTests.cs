using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.ContextEngine;
using Xunit;

namespace SelfKept.UnitTests.ContextEngine;

/// <summary>The pattern maths: the floors, medians, ranges, overlap, the noise line and what counts as "without".</summary>
public class PatternMathTests
{
  private static readonly DateOnly End = new(2026, 11, 9);

  /// <summary>
  /// Days End-1 back to End-days, every one logged; the tag on the first <paramref name="tagged"/>
  /// of them. The night after a tagged day reads <paramref name="after"/>; other nights 56, 57, 58 in turn.
  /// </summary>
  private static (List<LoggedDay> Logged, Dictionary<string, IReadOnlyDictionary<DateOnly, double>> Nights) Data(
    int tagged, int untagged, double after = 62, string tag = JournalTags.Alcohol)
  {
    var logged = new List<LoggedDay>();
    var nights = new Dictionary<DateOnly, double>();
    for (var i = 0; i < tagged + untagged; i++)
    {
      var day = End.AddDays(-1 - i);
      var hasTag = i < tagged;
      logged.Add(new LoggedDay(day, hasTag ? [tag, JournalTags.Water] : [JournalTags.Water]));
      nights[day.AddDays(1)] = hasTag ? after : 56 + i % 3;
    }
    logged.Reverse();
    return (logged, new() { [MetricKeys.RestingHrBpm] = nights });
  }

  private static PatternStats Single(IReadOnlyList<PatternStats> stats, string tag = JournalTags.Alcohol) =>
    stats.Single(p => p.Tag == tag && p.MetricKey == MetricKeys.RestingHrBpm);

  [Fact]
  public void Compute_EightWithSixteenWithoutAndAClearDifference_IsEstablished()
  {
    var (logged, nights) = Data(8, 16);

    var p = Single(PatternMath.Compute(logged, nights, End));

    p.Established.Should().BeTrue();
    p.NightsWith.Should().Be(8);
    p.NightsWithout.Should().Be(16);
    p.MedianWith.Should().Be(62);
    p.MedianWithout.Should().Be(57);
    p.Difference.Should().Be(5);
    p.Overlap.Should().Be(0, "no tagged night sits inside the middle half of the rest");
  }

  [Fact]
  public void Compute_SevenNightsWith_IsNotEstablished() =>
    Single(PatternMath.Compute(Data(7, 20).Logged, Data(7, 20).Nights, End)).Established.Should().BeFalse();

  [Fact]
  public void Compute_FifteenNightsWithout_IsNotEstablished() =>
    Single(PatternMath.Compute(Data(10, 15).Logged, Data(10, 15).Nights, End)).Established.Should().BeFalse();

  [Fact]
  public void Compute_DifferenceInsideTheNoiseLine_IsNotEstablished()
  {
    var (logged, nights) = Data(10, 20, after: 57.4);

    var p = Single(PatternMath.Compute(logged, nights, End));

    p.NoiseLine.Should().Be(1, "half the 56 to 58 interquartile range");
    p.Established.Should().BeFalse();
  }

  [Fact]
  public void Compute_DayWithNothingLogged_IsNeverCountedAsWithout()
  {
    var (logged, nights) = Data(8, 16);
    // Ten more nights after days with nothing in the day log.
    for (var i = 30; i < 40; i++) ((Dictionary<DateOnly, double>)nights[MetricKeys.RestingHrBpm])[End.AddDays(-i)] = 56;

    Single(PatternMath.Compute(logged, nights, End)).NightsWithout.Should().Be(16);
  }

  [Fact]
  public void Compute_TagDayWithoutAFigureTheNightAfter_IsLeftOut()
  {
    var (logged, nights) = Data(9, 16);
    ((Dictionary<DateOnly, double>)nights[MetricKeys.RestingHrBpm]).Remove(End);

    Single(PatternMath.Compute(logged, nights, End)).NightsWith.Should().Be(8);
  }

  [Fact]
  public void Compute_OnlyTheWindowCounts()
  {
    var (logged, nights) = Data(8, 16);

    PatternMath.Compute(logged, nights, End.AddDays(PatternMath.WindowDays + 30)).Should().BeEmpty();
  }

  [Fact]
  public void Stats_MediansRangesOverlapAndWeekends()
  {
    // 6 Nov 2026 is a Friday, 7 Nov a Saturday.
    var with = new List<(DateOnly, double)> { (new(2026, 11, 6), 60), (new(2026, 11, 7), 62), (new(2026, 11, 3), 64), (new(2026, 11, 4), 66) };
    var without = new List<double> { 54, 56, 58, 60, 62 };

    var p = PatternMath.Stats(JournalTags.Alcohol, MetricKeys.RestingHrBpm, with, without, new HashSet<DateOnly> { new(2026, 11, 5) });

    p.MedianWith.Should().Be(63);
    p.WithLow.Should().Be(61.5);
    p.WithHigh.Should().Be(64.5);
    p.MedianWithout.Should().Be(58);
    p.WithoutLow.Should().Be(56);
    p.WithoutHigh.Should().Be(60);
    p.Overlap.Should().Be(0.25, "one of the four tagged nights, 60, sits inside 56 to 60");
    p.NoiseLine.Should().Be(2);
    p.WeekendNights.Should().Be(2);
    p.ContextNights.Should().Be(1);
  }

  [Fact]
  public void NewlyEstablished_TheDayTheEighthTaggedNightArrives_ButNotTheDayAfter()
  {
    // Seven tagged days and twenty plain ones end on End-1; an eighth tagged day is End.
    var (logged, nights) = Data(7, 20);
    var map = (Dictionary<DateOnly, double>)nights[MetricKeys.RestingHrBpm];
    logged.Add(new LoggedDay(End, [JournalTags.Alcohol]));
    map[End.AddDays(1)] = 62;
    logged.Add(new LoggedDay(End.AddDays(1), [JournalTags.Water]));
    map[End.AddDays(2)] = 57;

    PatternMath.NewlyEstablished(logged, nights, End).Should().BeEmpty("only seven tagged nights so far");
    PatternMath.NewlyEstablished(logged, nights, End.AddDays(1)).Should().ContainSingle().Which.Tag.Should().Be(JournalTags.Alcohol);
    PatternMath.NewlyEstablished(logged, nights, End.AddDays(2)).Should().BeEmpty("it was already established the day before");
  }

  [Fact]
  public void Sentence_ForSleep_SaysMinutesLessAndNotProof()
  {
    var p = new PatternStats(JournalTags.LateCaffeine, MetricKeys.SleepStage, 9, 31, 400, 422, -22, 380, 410, 405, 440, 0.3, 17.5, 0, 2, true);

    PatternMath.Sentence(p).Should().Be(
      "On 9 nights after late caffeine, against 31 without, you slept 22 min less (380 to 410 min on those nights, 405 to 440 min on the rest). "
      + "2 fell while you were unwell or travelling. A pattern in your own data, not proof.");
  }

  [Fact]
  public void Build_ListsEstablishedPatterns_AndTagsStillBuilding()
  {
    var (logged, nights) = Data(8, 16);
    logged.Add(new LoggedDay(End.AddDays(-100), [JournalTags.LateMeal]));
    ((Dictionary<DateOnly, double>)nights[MetricKeys.RestingHrBpm])[End.AddDays(-99)] = 60;

    var dto = PatternService.Build(End, End.AddDays(-PatternMath.WindowDays), PatternMath.Compute(logged, nights, End));

    dto.Patterns.Should().ContainSingle().Which.Tag.Should().Be(JournalTags.Alcohol);
    dto.Building.Select(b => b.Tag).Should().BeEquivalentTo([JournalTags.LateMeal, JournalTags.Water], "water is on every day, so it has no nights without");
    dto.Note.Should().Be("A pattern in your own data, not proof.");
    dto.Patterns[0].Sentence.Should().EndWith("not proof.");
  }
}
