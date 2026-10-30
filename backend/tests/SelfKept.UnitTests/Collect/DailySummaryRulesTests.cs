using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Collect;
using Xunit;

namespace SelfKept.UnitTests.Collect;

public class DailySummaryRulesTests
{
  // A fixed +02:00 zone: deterministic on every host, and far enough from UTC to prove the day shift.
  private static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
  private static readonly Guid Person = Guid.NewGuid();
  private static readonly DateOnly Day = new(2026, 10, 29);
  private static readonly DateTime Computed = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

  private static Sample At(string metric, string localStart, double value, string? localEnd = null) => new()
  {
    Id = Guid.CreateVersion7(),
    PersonId = Person,
    MetricKey = metric,
    StartAt = Local(localStart),
    EndAt = localEnd == null ? null : Local(localEnd),
    Value = value,
  };

  // "2026-10-29 23:30" in the +2 zone to UTC.
  private static DateTime Local(string text) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Unspecified), PlusTwo);

  private static Sample From(Sample sample, string originKey, string origin)
  {
    sample.Meta = $"{{\"{originKey}\":\"{origin}\"}}";
    return sample;
  }

  [Fact]
  public void Derive_StepsFromTwoDevices_TakesTheLargerDevice_NotTheSum()
  {
    // The iPhone and the Watch both count the same walk; Apple Health itself would not add them.
    var samples = new[]
    {
      From(At("steps", "2026-10-29 08:00", 3000, "2026-10-29 09:00"), "sourceBundle", "com.apple.health.iphone"),
      From(At("steps", "2026-10-29 12:00", 3000, "2026-10-29 13:00"), "sourceBundle", "com.apple.health.iphone"),
      From(At("steps", "2026-10-29 08:00", 3400, "2026-10-29 09:00"), "sourceBundle", "com.apple.health.watch"),
      From(At("steps", "2026-10-29 12:00", 3300, "2026-10-29 13:00"), "sourceBundle", "com.apple.health.watch"),
    };

    var row = DailySummaryRules.Derive(Person, "steps", Day, samples, Computed)!;

    row.Value.Should().Be(6700);
    row.Count.Should().Be(2, "the count is the chosen device's samples");
  }

  [Fact]
  public void Derive_SleepFromTheWatchAndTheRing_TakesOneNight_NotTwo()
  {
    var samples = new[]
    {
      From(At("sleep_stage", "2026-10-28 23:00", 2, "2026-10-29 03:00"), "sourceBundle", "com.apple.health.watch"),
      From(At("sleep_stage", "2026-10-29 03:00", 4, "2026-10-29 06:30"), "sourceBundle", "com.apple.health.watch"),
      From(At("sleep_stage", "2026-10-28 23:10", 3, "2026-10-29 06:40"), "sourceBundle", "com.ouraring.oura"),
      From(At("sleep_stage", "2026-10-28 23:15", 2, "2026-10-29 06:35"), "origin", "oura-cloud"),
    };

    var row = DailySummaryRules.Derive(Person, "sleep_stage", Day, samples, Computed)!;

    row.Value.Should().Be(450, "the ring's 7 h 30 is the longest single account of the night");
  }

  [Fact]
  public void Derive_SleepWithOverlappingSegmentsFromOneOrigin_CountsTheOverlapOnce()
  {
    var samples = new[]
    {
      From(At("sleep_stage", "2026-10-28 23:00", 5, "2026-10-29 07:00"), "sourcePackage", "com.fitbit.FitbitMobile"),
      From(At("sleep_stage", "2026-10-29 01:00", 3, "2026-10-29 02:00"), "sourcePackage", "com.fitbit.FitbitMobile"),
    };

    DailySummaryRules.Derive(Person, "sleep_stage", Day, samples, Computed)!.Value.Should().Be(480);
  }

  [Fact]
  public void OriginOf_ReadsTheCollectorsKeys_AndFallsBackToTheSource()
  {
    var source = Guid.NewGuid();
    DailySummaryRules.OriginOf(new Sample { SourceId = source, Meta = "{\"sourceBundle\":\"a\"}" }).Should().Be("a");
    DailySummaryRules.OriginOf(new Sample { SourceId = source, Meta = "{\"sourcePackage\":\"b\"}" }).Should().Be("b");
    DailySummaryRules.OriginOf(new Sample { SourceId = source, Meta = "{\"origin\":\"c\"}" }).Should().Be("c");
    DailySummaryRules.OriginOf(new Sample { SourceId = source, Meta = "not json" }).Should().Be(source.ToString());
    DailySummaryRules.OriginOf(new Sample { SourceId = source }).Should().Be(source.ToString());
  }

  [Fact]
  public void Derive_Steps_SumsTheDay()
  {
    var samples = new[] { At("steps", "2026-10-29 08:00", 1200, "2026-10-29 08:10"), At("steps", "2026-10-29 12:00", 800, "2026-10-29 12:10"), At("steps", "2026-10-29 20:00", 3000, "2026-10-29 20:10") };

    var row = DailySummaryRules.Derive(Person, "steps", Day, samples, Computed)!;

    row.Value.Should().Be(5000);
    row.Count.Should().Be(3);
    row.Min.Should().BeNull();
    row.Max.Should().BeNull();
    row.Day.Should().Be(Day);
  }

  [Fact]
  public void Derive_HeartRate_AveragesWithMinAndMax()
  {
    var samples = new[] { At("heart_rate_bpm", "2026-10-29 08:00", 55), At("heart_rate_bpm", "2026-10-29 12:00", 95), At("heart_rate_bpm", "2026-10-29 20:00", 60) };

    var row = DailySummaryRules.Derive(Person, "heart_rate_bpm", Day, samples, Computed)!;

    row.Value.Should().Be(70);
    row.Min.Should().Be(55);
    row.Max.Should().Be(95);
    row.Count.Should().Be(3);
  }

  [Fact]
  public void Derive_CheckinMood_Averages()
  {
    var samples = new[] { At("checkin_mood", "2026-10-29 08:00", 2), At("checkin_mood", "2026-10-29 13:00", 4), At("checkin_mood", "2026-10-29 21:00", 6) };

    var row = DailySummaryRules.Derive(Person, "checkin_mood", Day, samples, Computed)!;

    row.Value.Should().Be(4);
    row.Min.Should().Be(2);
    row.Max.Should().Be(6);
  }

  [Fact]
  public void Derive_SleepNightSpanningMidnight_SumsMinutesAsleepAndIgnoresAwakeAndInBed()
  {
    var night = new[]
    {
      At("sleep_stage", "2026-10-28 22:50", SleepStages.InBed, "2026-10-28 23:00"),
      At("sleep_stage", "2026-10-28 23:00", SleepStages.Light, "2026-10-29 01:00"), // 120, crosses midnight
      At("sleep_stage", "2026-10-29 01:00", SleepStages.Deep, "2026-10-29 03:00"), // 120
      At("sleep_stage", "2026-10-29 03:00", SleepStages.Awake, "2026-10-29 03:15"),
      At("sleep_stage", "2026-10-29 03:15", SleepStages.Rem, "2026-10-29 06:30"), // 195
    };

    var row = DailySummaryRules.Derive(Person, "sleep_stage", Day, night, Computed)!;

    row.Value.Should().Be(435);
    row.Count.Should().Be(3, "only the asleep segments count");
  }

  [Fact]
  public void DayFor_SleepSegment_IsTheLocalDayTheSegmentEnded()
  {
    var crossing = At("sleep_stage", "2026-10-28 23:00", SleepStages.Light, "2026-10-29 01:00");
    var steps = At("steps", "2026-10-28 23:30", 100, "2026-10-29 00:10");

    DailySummaryRules.DayFor(crossing, PlusTwo).Should().Be(Day);
    DailySummaryRules.DayFor(steps, PlusTwo).Should().Be(new DateOnly(2026, 10, 28), "non-sleep metrics attribute by start");
  }

  [Fact]
  public void Derive_OnlyAwakeSegments_IsNull()
  {
    var samples = new[] { At("sleep_stage", "2026-10-29 03:00", SleepStages.Awake, "2026-10-29 03:15") };

    DailySummaryRules.Derive(Person, "sleep_stage", Day, samples, Computed).Should().BeNull();
  }

  [Fact]
  public void Derive_MetricWithoutRule_IsNull()
  {
    DailySummaryRules.Derive(Person, "vo2max", Day, [At("vo2max", "2026-10-29 08:00", 45)], Computed).Should().BeNull();
    DailySummaryRules.DayFor(At("vo2max", "2026-10-29 08:00", 45), PlusTwo).Should().BeNull();
    DailySummaryRules.HasRule("vo2max").Should().BeFalse();
  }

  [Fact]
  public void Derive_NoSamples_IsNull()
  {
    DailySummaryRules.Derive(Person, "steps", Day, [], Computed).Should().BeNull();
  }
}

public class DayBoundariesTests
{
  private static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

  [Fact]
  public void LocalDay_LateEveningUtc_IsTheNextLocalDay()
  {
    DayBoundaries.LocalDay(new DateTime(2026, 10, 28, 23, 30, 0, DateTimeKind.Utc), PlusTwo).Should().Be(new DateOnly(2026, 10, 29));
    DayBoundaries.LocalDay(new DateTime(2026, 10, 28, 21, 30, 0, DateTimeKind.Utc), PlusTwo).Should().Be(new DateOnly(2026, 10, 28));
  }

  [Fact]
  public void UtcWindow_IsLocalMidnightToLocalMidnight()
  {
    var (start, end) = DayBoundaries.UtcWindow(new DateOnly(2026, 10, 29), PlusTwo);

    start.Should().Be(new DateTime(2026, 10, 28, 22, 0, 0, DateTimeKind.Utc));
    end.Should().Be(new DateTime(2026, 10, 29, 22, 0, 0, DateTimeKind.Utc));
    start.Kind.Should().Be(DateTimeKind.Utc);
  }

  [Fact]
  public void Resolve_UnknownZone_FallsBackToUtc()
  {
    DayBoundaries.Resolve("Nowhere/Nothing").Should().Be(TimeZoneInfo.Utc);
    DayBoundaries.Resolve(null).Should().Be(TimeZoneInfo.Utc);
    DayBoundaries.Resolve("UTC").BaseUtcOffset.Should().Be(TimeSpan.Zero);
  }

  [Fact]
  public void AsUtc_UnspecifiedKind_IsTreatedAsUtc()
  {
    var value = new DateTime(2026, 10, 29, 6, 0, 0, DateTimeKind.Unspecified);

    var utc = DayBoundaries.AsUtc(value);

    utc.Kind.Should().Be(DateTimeKind.Utc);
    utc.Hour.Should().Be(6);
  }
}
