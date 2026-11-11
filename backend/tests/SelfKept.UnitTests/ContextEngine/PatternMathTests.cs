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
  public void Sentence_ForWristTemperature_UsesTheDegreeSign()
  {
    var p = new PatternStats(JournalTags.Alcohol, MetricKeys.WristTempDeltaC, 9, 31, 0.3, 0.12, 0.18, 0.2, 0.4, 0.0, 0.25, 0.3, 0.05, 0, 0, true);

    PatternMath.Sentence(p).Should().Contain("your wrist temperature change ran 0.18 °C higher").And.Contain("(0.2 to 0.4 °C on those nights");
  }

  [Theory]
  [InlineData(MetricKeys.RestingHrBpm, "resting heart rate")]
  [InlineData(MetricKeys.HrvRmssdMs, "HRV")]
  [InlineData(MetricKeys.Vo2Max, "VO2 max")]
  [InlineData(MetricKeys.SleepStage, "sleep")]
  public void MetricWords_InsideASentence_KeepsAcronyms(string metric, string expected)
  {
    PatternMath.MetricWords(metric).Should().Be(expected);
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

  // --- the sampling-noise check -----------------------------------------------------------------

  /// <summary>A normal reading around <paramref name="mean"/>, rounded to a whole unit like resting heart rate.</summary>
  private static double Reading(Random random, double mean, double spread)
  {
    // Box-Muller: two uniforms in (0, 1] to one standard normal.
    var u1 = 1.0 - random.NextDouble();
    var u2 = random.NextDouble();
    var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    return Math.Round(mean + spread * normal);
  }

  private static PatternStats Simulated(int seed, int nightsWith, int nightsWithout, double shift, double spread = 3)
  {
    var random = new Random(seed);
    var with = Enumerable.Range(0, nightsWith).Select(i => (End.AddDays(-1 - i), Reading(random, 55 + shift, spread))).ToList();
    var without = Enumerable.Range(0, nightsWithout).Select(_ => Reading(random, 55, spread)).ToList();
    return PatternMath.Stats(JournalTags.Fruit, MetricKeys.RestingHrBpm, with, without);
  }

  [Theory]
  [InlineData(8, 16)]
  [InlineData(15, 30)]
  [InlineData(35, 35)]
  public void Stats_NoEffectAcrossManySeeds_IsRarelyEstablished(int nightsWith, int nightsWithout)
  {
    // The same spread for both groups, nothing behind the tag. The half-IQR line alone let
    // 22.4 % (8/16), 11.8 % (15/30) and 6.2 % (35/35) of these seeds through; with the rank test
    // it is 0.5 %, 0.4 % and 0.9 %.
    const int Seeds = 1000;

    var established = Enumerable.Range(1, Seeds).Count(seed => Simulated(seed, nightsWith, nightsWithout, shift: 0).Established);

    ((double)established / Seeds).Should().BeLessThan(0.02, "a tag with no effect behind it should seldom read as a pattern");
  }

  [Theory]
  [InlineData(6)]
  [InlineData(-6)]
  public void Stats_ARealEffect_IsStillEstablished_InEitherDirection(double shift)
  {
    // Resting heart rate 6 bpm apart against a spread of 3, 15 nights with and 40 without.
    const int Seeds = 200;

    var stats = Enumerable.Range(1, Seeds).Select(seed => Simulated(seed, 15, 40, shift)).ToList();

    stats.Count(p => p.Established && Math.Sign(p.Difference) == Math.Sign(shift)).Should().BeGreaterThanOrEqualTo(196);
  }

  [Fact]
  public void Stats_EveryValueTheSame_IsNotEstablished()
  {
    var with = Enumerable.Range(0, 10).Select(i => (End.AddDays(-1 - i), 56.0)).ToList();
    var without = Enumerable.Repeat(56.0, 20).ToList();

    PatternMath.Stats(JournalTags.Fruit, MetricKeys.RestingHrBpm, with, without).Established.Should().BeFalse();
  }

  [Fact]
  public void Stats_HeavyTiesInTwoValues_DoesNotThrow_AndASplitStillCounts()
  {
    // Every "with" night 62, every "without" night 56: zero spread inside each group, but the
    // groups do not touch, so the rank test is still well defined.
    var with = Enumerable.Range(0, 8).Select(i => (End.AddDays(-1 - i), 62.0)).ToList();
    var without = Enumerable.Repeat(56.0, 16).ToList();
    // Two values shared across both groups in nearly equal shares.
    var mixedWith = Enumerable.Range(0, 9).Select(i => (End.AddDays(-1 - i), i % 2 == 0 ? 56.0 : 57.0)).ToList();
    var mixedWithout = Enumerable.Range(0, 18).Select(i => i % 2 == 0 ? 57.0 : 56.0).ToList();

    PatternMath.Stats(JournalTags.Fruit, MetricKeys.RestingHrBpm, with, without).Established.Should().BeTrue();
    var mixed = () => PatternMath.Stats(JournalTags.Fruit, MetricKeys.RestingHrBpm, mixedWith, mixedWithout);
    mixed.Should().NotThrow();
    mixed().Established.Should().BeFalse();
  }

  [Fact]
  public void MannWhitneyZ_EveryValueTheSame_IsNull() =>
    PatternMath.MannWhitneyZ([56, 56, 56], [56, 56, 56, 56]).Should().BeNull("no variance, nothing to test");

  [Fact]
  public void MannWhitneyZ_AnEmptyGroup_IsNull() =>
    PatternMath.MannWhitneyZ([], [56, 57]).Should().BeNull();

  [Fact]
  public void MannWhitneyZ_ThreeBelowThree_NoTies()
  {
    // With 1, 2, 3 against 4, 5, 6: the with ranks are 1, 2, 3, sum 6, so U = 6 - 3 * 4 / 2 = 0.
    // Mean n1 n2 / 2 = 4.5; variance 3 * 3 * 7 / 12 = 5.25. |0 - 4.5| less a half is 4, negative.
    // z = -4 / sqrt(5.25) = -1.74574.
    PatternMath.MannWhitneyZ([1, 2, 3], [4, 5, 6]).Should().BeApproximately(-1.74574, 1e-5);
  }

  [Fact]
  public void MannWhitneyZ_FourAboveFour_NoTies()
  {
    // With 5, 6, 7, 8 against 1, 2, 3, 4: with ranks 5 to 8, sum 26, U = 26 - 4 * 5 / 2 = 16.
    // Mean 8; variance 4 * 4 * 9 / 12 = 12. (16 - 8) less a half is 7.5. z = 7.5 / sqrt(12) = 2.16506.
    PatternMath.MannWhitneyZ([5, 6, 7, 8], [1, 2, 3, 4]).Should().BeApproximately(2.16506, 1e-5);
  }

  [Fact]
  public void MannWhitneyZ_WithTies_UsesMidranksAndTheTieCorrection()
  {
    // With 3, 4, 4, 5 against 1, 2, 2, 3, 4. Sorted: 1 | 2 2 | 3 3 | 4 4 4 | 5, ranks 1 | 2.5 | 4.5 | 7 | 9.
    // With rank sum 4.5 + 7 + 7 + 9 = 27.5, U = 27.5 - 4 * 5 / 2 = 17.5, mean 10.
    // Ties: (2^3 - 2) + (2^3 - 2) + (3^3 - 3) = 36. Variance 4 * 5 / 12 * (10 - 36 / (9 * 8)) = 15.8333.
    // (17.5 - 10) less a half is 7. z = 7 / sqrt(15.8333) = 1.75919.
    PatternMath.MannWhitneyZ([3, 4, 4, 5], [1, 2, 2, 3, 4]).Should().BeApproximately(1.75919, 1e-5);
  }

  [Fact]
  public void MannWhitneyZ_UWithinAHalfOfItsMean_IsZero()
  {
    // With 2, 3, 3 against 1, 2, 3, 4: ranks 1 | 2.5 2.5 | 5 5 5 | 7, with sum 2.5 + 5 + 5 = 12.5,
    // U = 12.5 - 3 * 4 / 2 = 6.5 against a mean of 6. The continuity correction takes the half.
    PatternMath.MannWhitneyZ([2, 3, 3], [1, 2, 3, 4]).Should().Be(0);
  }
}
