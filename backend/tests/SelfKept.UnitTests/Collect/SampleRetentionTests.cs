using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Services.Collect;
using Xunit;

namespace SelfKept.UnitTests.Collect;

/// <summary>The pure parts of sample retention: the window, the schedule, the fold and what daily summaries make of it.</summary>
public class SampleRetentionTests
{
  private static readonly Guid Person = Guid.NewGuid();
  private static readonly Guid Watch = Guid.NewGuid();
  private static readonly Guid Phone = Guid.NewGuid();

  private static Sample Hr(string at, double value, Guid? source = null) => new()
  {
    Id = Guid.NewGuid(), PersonId = Person, SourceId = source ?? Watch, SourceUid = Guid.NewGuid().ToString("N"),
    MetricKey = MetricKeys.HeartRateBpm, StartAt = DateTime.Parse(at, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
    Value = value, Unit = "bpm",
  };

  [Fact]
  public void RawSince_TwelveMonthsInNovember_KeepsFromTheSameMonthLastYear()
  {
    RetentionWindow.RawSince(new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc), 12).Should().Be(new DateOnly(2025, 11, 1));
  }

  [Fact]
  public void RawSince_OnTheFirstOfTheMonth_CountsThatMonth()
  {
    RetentionWindow.RawSince(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), 4).Should().Be(new DateOnly(2025, 11, 1));
  }

  [Fact]
  public void RawSince_Zero_KeepsEverything()
  {
    RetentionWindow.RawSince(DateTime.UtcNow, 0).Should().BeNull();
  }

  [Theory]
  [InlineData(0, true)]
  [InlineData(1, false)]
  [InlineData(3, false)]
  [InlineData(4, true)]
  [InlineData(12, true)]
  [InlineData(600, true)]
  [InlineData(601, false)]
  [InlineData(-1, false)]
  public void IsAllowed_KeepsTheFloorAboveTheFirstPhoneRead(int months, bool allowed)
  {
    RetentionWindow.IsAllowed(months).Should().Be(allowed);
  }

  [Fact]
  public void MinimumMonths_AlwaysCoversNinetyDays()
  {
    // The shortest window starts on the first of a month, so the newest folded day is at least
    // (MinimumMonths - 1) full months back: 3 months of 28 to 31 days is at least 89 days, plus
    // the days already gone this month. Check every day of a leap year.
    for (var day = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc); day.Year == 2028; day = day.AddDays(1))
    {
      var rawSince = RetentionWindow.RawSince(day, RetentionWindow.MinimumMonths)!.Value;
      (day - DayBoundaries.MonthStartUtc(rawSince)).TotalDays.Should().BeGreaterThan(90, $"on {day:yyyy-MM-dd}");
    }
  }

  [Fact]
  public void PartitionName_RoundTripsThroughMonthOf()
  {
    var month = new DateOnly(2025, 10, 1);
    RetentionWindow.PartitionName(month).Should().Be("sample_202510");
    RetentionWindow.MonthOf("sample_202510").Should().Be(month);
  }

  [Theory]
  [InlineData("sample_default")]
  [InlineData("sample_20251")]
  [InlineData("sample_202513")]
  [InlineData("samples_202510")]
  [InlineData("sample_202510x")]
  public void MonthOf_AnyOtherName_IsNull(string name)
  {
    RetentionWindow.MonthOf(name).Should().BeNull();
  }

  [Fact]
  public void DueDay_Before0400Local_IsNull_AndAfterIsToday()
  {
    var zone = DayBoundaries.Resolve("America/New_York");
    // 08:30 UTC is 03:30 in New York in November (UTC-5); 09:05 UTC is 04:05.
    RetentionSchedule.DueDay(new DateTime(2026, 11, 4, 8, 30, 0, DateTimeKind.Utc), zone).Should().BeNull();
    RetentionSchedule.DueDay(new DateTime(2026, 11, 4, 9, 5, 0, DateTimeKind.Utc), zone).Should().Be(new DateOnly(2026, 11, 4));
  }

  [Fact]
  public void Fold_GroupsByMinuteAndSource_WithMeanMinMaxCount()
  {
    var minutes = SampleRetention.Fold([
      Hr("2025-03-10T12:00:00Z", 60), Hr("2025-03-10T12:00:20Z", 63), Hr("2025-03-10T12:00:59.999Z", 66),
      Hr("2025-03-10T12:01:00Z", 70),
      Hr("2025-03-10T12:00:30Z", 80, Phone),
    ]);

    minutes.Should().HaveCount(3);
    var watch = minutes.Single(m => m.SourceId == Watch && m.MinuteAt == new DateTime(2025, 3, 10, 12, 0, 0, DateTimeKind.Utc));
    watch.Should().BeEquivalentTo(new { Avg = 63.0, Min = 60.0, Max = 66.0, Count = 3, MetricKey = MetricKeys.HeartRateBpm, PersonId = Person });
    minutes.Single(m => m.SourceId == Watch && m.MinuteAt.Minute == 1).Count.Should().Be(1);
    minutes.Single(m => m.SourceId == Phone).Avg.Should().Be(80);
  }

  [Fact]
  public void Merge_WeightsTheMeanByCount_AndWidensTheRange()
  {
    var existing = new SampleMinute { Avg = 60, Min = 55, Max = 65, Count = 3 };
    SampleRetention.Merge(existing, new SampleMinute { Avg = 80, Min = 80, Max = 80, Count = 1 });
    existing.Should().BeEquivalentTo(new { Avg = 65.0, Min = 55.0, Max = 80.0, Count = 4 });
  }

  [Fact]
  public void Derive_FromFoldedMinutes_MatchesTheRawDay()
  {
    var day = new DateOnly(2025, 3, 10);
    var raw = new[] { Hr("2025-03-10T12:00:00Z", 60), Hr("2025-03-10T12:00:20Z", 63), Hr("2025-03-10T12:01:00Z", 71), Hr("2025-03-10T18:00:00Z", 90) };
    var fromRaw = DailySummaryRules.Derive(Person, MetricKeys.HeartRateBpm, day, raw, DateTime.UtcNow)!;

    var fromMinutes = DailySummaryRules.Derive(Person, MetricKeys.HeartRateBpm, day, [], DateTime.UtcNow, SampleRetention.Fold(raw))!;

    fromMinutes.Value.Should().BeApproximately(fromRaw.Value, 1e-9);
    fromMinutes.Min.Should().Be(fromRaw.Min);
    fromMinutes.Max.Should().Be(fromRaw.Max);
    fromMinutes.Count.Should().Be(fromRaw.Count);
  }

  [Fact]
  public void Derive_ALateReadingOnAFoldedDay_CountsTheMinutesToo()
  {
    var day = new DateOnly(2025, 3, 10);
    var folded = SampleRetention.Fold([Hr("2025-03-10T12:00:00Z", 60), Hr("2025-03-10T12:00:20Z", 70)]);

    var row = DailySummaryRules.Derive(Person, MetricKeys.HeartRateBpm, day, [Hr("2025-03-10T12:00:30Z", 95, Phone)], DateTime.UtcNow, folded)!;

    row.Should().BeEquivalentTo(new { Value = 75.0, Min = 60.0, Max = 95.0, Count = 3 });
  }

  [Fact]
  public void Derive_NoSamplesAndNoMinutes_IsNull()
  {
    DailySummaryRules.Derive(Person, MetricKeys.HeartRateBpm, new DateOnly(2025, 3, 10), [], DateTime.UtcNow, []).Should().BeNull();
  }

  [Fact]
  public void DenseMetrics_AreInstantAndAveraged_SoTheirDaysRebuildExactly()
  {
    foreach (var key in DenseMetrics.Keys)
    {
      MetricSeed.All.Single(m => m.Key == key).Kind.Should().Be("instant", key);
      DailySummaryRules.RuleFor(key).Should().Be(SummaryRule.Average, key);
    }
    DenseMetrics.Contains(MetricKeys.SleepStage).Should().BeFalse();
    DenseMetrics.Contains(MetricKeys.HrvRmssdMs).Should().BeFalse();
    DenseMetrics.Contains(MetricKeys.RestingHrBpm).Should().BeFalse();
  }

  [Fact]
  public void AsSample_ShowsAMinuteAsAOneMinuteReadingWithItsRange()
  {
    var minute = new SampleMinute { Id = Guid.NewGuid(), PersonId = Person, SourceId = Watch, MetricKey = MetricKeys.HeartRateBpm, MinuteAt = new DateTime(2025, 3, 10, 12, 0, 0, DateTimeKind.Utc), Avg = 63, Min = 60, Max = 66, Count = 3 };

    var sample = SampleMinutes.AsSample(minute, "bpm");

    sample.EndAt.Should().Be(minute.MinuteAt.AddMinutes(1));
    sample.Value.Should().Be(63);
    sample.Meta.Should().Be("""{"rollup":{"min":60,"max":66,"count":3}}""");
  }

  [Fact]
  public void FromConfiguration_BlankOrMissing_IsUnset()
  {
    RetentionPolicy.FromConfiguration(Config(null)).Should().BeNull();
    RetentionPolicy.FromConfiguration(Config("  ")).Should().BeNull();
    RetentionPolicy.FromConfiguration(Config("24")).Should().Be(24);
    RetentionPolicy.FromConfiguration(Config("0")).Should().Be(0);
  }

  [Theory]
  [InlineData("2")]
  [InlineData("twelve")]
  [InlineData("-12")]
  public void FromConfiguration_OutsideTheAllowedValues_Throws(string value)
  {
    var act = () => RetentionPolicy.FromConfiguration(Config(value));
    act.Should().Throw<InvalidOperationException>().WithMessage("*Retention:RawMonths*");
  }

  private static IConfiguration Config(string? months) =>
    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [RetentionPolicy.ConfigurationKey] = months }).Build();
}
