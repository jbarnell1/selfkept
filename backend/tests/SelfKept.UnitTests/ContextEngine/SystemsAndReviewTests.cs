using FluentAssertions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Infrastructure.Services.ContextEngine;
using Xunit;

namespace SelfKept.UnitTests.ContextEngine;

/// <summary>Systems, read-backs and the weekly review, assembled through the real repositories with full, partial and missing grants.</summary>
public class SystemsAndReviewTests
{
  // --- systems ---

  [Fact]
  public async Task Systems_Owner_SeesReadingsLabsAndCards_WithAStateLine()
  {
    await using var f = await RecordFixture.SeededAsync();

    var dto = await f.Systems.ListAsync(f.Owner, f.Owner);

    dto.Should().NotBeNull();
    var heart = dto!.Systems.Single(s => s.Key == "heart");
    var rhr = heart.Readings.Single(r => r.Metric == MetricKeys.RestingHrBpm);
    rhr.LatestValue.Should().Be(66);
    rhr.Band.Should().Be(BandPositions.Above);
    rhr.Usual!.Median28.Should().Be(57.5);
    rhr.Recent.Should().BeNull("the list leaves the series to the detail page");
    heart.Labs.Should().ContainSingle().Which.Metric.Should().Be("lab.ldl");
    heart.Insights.Should().ContainSingle().Which.Kind.Should().Be(InsightKinds.RhrElevated);
    heart.State.Should().Be("One card worth a look. Resting heart rate is above your usual range.");
    dto.Systems.Single(s => s.Key == "hormones").State.Should().Be("Not enough days yet for a usual range.");
    dto.Systems.Single(s => s.Key == "body").Readings.Single().Metric.Should().Be(MetricKeys.WeightKg);
    dto.Labs.Should().BeEquivalentTo(new LabsEntryDto(RecordFixture.Today.AddDays(-20), 1, 2, 1));
  }

  [Fact]
  public async Task Systems_StrangerWithoutAGrant_IsNull()
  {
    await using var f = await RecordFixture.SeededAsync();

    (await f.Systems.ListAsync(f.Stranger, f.Owner)).Should().BeNull();
    (await f.Systems.GetAsync(f.Stranger, f.Owner, "sleep")).Should().BeNull();
  }

  [Fact]
  public async Task Systems_SleepOnlyMember_SeesOnlySleep_WithoutLabsOrCards()
  {
    await using var f = await RecordFixture.SeededAsync();
    var member = await f.MemberWithAsync(ShareCategory.Sleep);

    var dto = (await f.Systems.ListAsync(member, f.Owner))!;

    dto.Systems.Select(s => s.Key).Should().Equal("sleep");
    dto.Systems[0].Insights.Should().BeEmpty("insights were not shared");
    dto.Labs.Should().BeNull("labs were not shared");
    (await f.Systems.GetAsync(member, f.Owner, "heart")).Should().BeNull("nothing in heart and fitness was shared");
  }

  [Fact]
  public async Task Systems_InsightsOnlyMember_SeesTheCards_WithoutTheReadings()
  {
    await using var f = await RecordFixture.SeededAsync();
    var member = await f.MemberWithAsync(ShareCategory.Insights);

    var dto = (await f.Systems.ListAsync(member, f.Owner))!;

    var heart = dto.Systems.Single(s => s.Key == "heart");
    heart.Readings.Should().BeEmpty();
    heart.Labs.Should().BeEmpty();
    heart.Insights.Should().ContainSingle();
  }

  [Fact]
  public async Task Systems_LabsOnlyMember_SeesHormoneResults()
  {
    await using var f = await RecordFixture.SeededAsync();
    var member = await f.MemberWithAsync(ShareCategory.Labs);

    var dto = (await f.Systems.ListAsync(member, f.Owner))!;

    dto.Systems.Single(s => s.Key == "hormones").Labs.Single().Metric.Should().Be("lab.tsh");
    dto.Systems.Single(s => s.Key == "heart").Readings.Should().BeEmpty();
  }

  [Fact]
  public async Task SystemDetail_Owner_HasTheRecentSeriesAndLabHistory()
  {
    await using var f = await RecordFixture.SeededAsync();

    var heart = (await f.Systems.GetAsync(f.Owner, f.Owner, "HEART"))!;

    heart.Readings.Single(r => r.Metric == MetricKeys.RestingHrBpm).Recent.Should().HaveCount(28);
    heart.Labs.Single().History.Should().ContainSingle();
  }

  [Fact]
  public async Task SystemDetail_UnknownKey_IsNull()
  {
    await using var f = await RecordFixture.SeededAsync();

    (await f.Systems.GetAsync(f.Owner, f.Owner, "kidneys")).Should().BeNull();
  }

  [Fact]
  public void State_LabFlagWithoutOutsideReadings_NamesTheMarker()
  {
    var labs = new[] { new SystemLabDto("lab.ldl", "LDL cholesterol", "mg/dL", 160, new DateOnly(2026, 10, 21), LabFlags.High, null, null, null) };

    SystemsService.State([], labs, []).Should().Be("LDL cholesterol came back high on Oct 21, 2026.");
  }

  // --- read-backs ---

  private static Sample Stage(string start, string end, int stage, string origin = "watch") => new()
  {
    MetricKey = MetricKeys.SleepStage,
    StartAt = DateTime.SpecifyKind(DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc),
    EndAt = DateTime.SpecifyKind(DateTime.Parse(end, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc),
    Value = stage,
    Meta = $"{{\"origin\":\"{origin}\"}}",
  };

  [Fact]
  public void Nights_OneNightWithStages_GivesTimesAndStageMinutes()
  {
    var samples = new[]
    {
      Stage("2026-11-08 23:00", "2026-11-09 01:00", SleepStages.Light),
      Stage("2026-11-09 01:00", "2026-11-09 03:00", SleepStages.Deep),
      Stage("2026-11-09 03:00", "2026-11-09 03:15", SleepStages.Awake),
      Stage("2026-11-09 03:15", "2026-11-09 06:30", SleepStages.Rem),
    };

    var night = ReadBackService.Nights(samples, TimeZoneInfo.Utc, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9)).Should().ContainSingle().Subject;

    night.Day.Should().Be(new DateOnly(2026, 11, 9), "a night belongs to the day it ended");
    night.AsleepMinutes.Should().Be(435);
    night.InBedMinutes.Should().Be(450);
    night.LightMinutes.Should().Be(120);
    night.DeepMinutes.Should().Be(120);
    night.RemMinutes.Should().Be(195);
    night.AwakeMinutes.Should().Be(15);
    night.HasStages.Should().BeTrue();
    night.Bedtime.Should().Be(new DateTime(2026, 11, 8, 23, 0, 0, DateTimeKind.Utc));
  }

  [Fact]
  public void Nights_TwoDevices_UsesTheOneWithMoreSleep_AndNoStagesMeansNullStageMinutes()
  {
    var samples = new[]
    {
      Stage("2026-11-08 23:00", "2026-11-09 05:00", SleepStages.Unspecified, "phone"),
      Stage("2026-11-08 23:30", "2026-11-09 07:00", SleepStages.Unspecified, "ring"),
    };

    var night = ReadBackService.Nights(samples, TimeZoneInfo.Utc, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 9)).Single();

    night.AsleepMinutes.Should().Be(450);
    night.HasStages.Should().BeFalse();
    night.DeepMinutes.Should().BeNull();
    night.UnspecifiedMinutes.Should().Be(450);
  }

  [Theory]
  [InlineData("traditional_strength_training", "strength")]
  [InlineData("running", "cardio")]
  [InlineData("high_intensity_interval_training", "mixed")]
  public void Group_FromTheWorkoutType(string type, string group) => ReadBackService.Group(type).Should().Be(group);

  [Fact]
  public async Task Workouts_ActivityMemberReads_SleepMemberIsRefused_AndWeeksAddUp()
  {
    await using var f = await RecordFixture.SeededAsync();
    f.Db.Workouts.Add(new Workout { Id = Guid.NewGuid(), PersonId = f.Owner, SourceId = Guid.NewGuid(), SourceUid = "w1", Type = "running", StartAt = RecordFixture.Now.AddDays(-1).AddHours(-3), EndAt = RecordFixture.Now.AddDays(-1).AddHours(-3).AddMinutes(45), AvgHr = 150, CreatedAt = RecordFixture.Now });
    f.Db.Workouts.Add(new Workout { Id = Guid.NewGuid(), PersonId = f.Owner, SourceId = Guid.NewGuid(), SourceUid = "w2", Type = "traditional_strength_training", StartAt = RecordFixture.Now.AddDays(-1), EndAt = RecordFixture.Now.AddDays(-1).AddMinutes(60), CreatedAt = RecordFixture.Now });
    await f.Db.SaveChangesAsync();
    var activity = await f.MemberWithAsync(ShareCategory.Activity);
    var sleep = await f.MemberWithAsync(ShareCategory.Sleep);

    var dto = await f.ReadBacks.GetWorkoutsAsync(activity, f.Owner, null, null);

    dto!.Workouts.Should().HaveCount(2);
    dto.Workouts[0].DurationMinutes.Should().Be(45);
    dto.Workouts[0].AvgHr.Should().Be(150);
    dto.Weeks.Should().ContainSingle().Which.Should().BeEquivalentTo(new WorkoutWeekDto(new DateOnly(2026, 11, 9), 2, 105, 1, 1, 0));
    (await f.ReadBacks.GetWorkoutsAsync(sleep, f.Owner, null, null)).Should().BeNull();
  }

  [Fact]
  public async Task Sleep_StrangerIsRefused_SleepMemberReads()
  {
    await using var f = await RecordFixture.SeededAsync();
    var sleep = await f.MemberWithAsync(ShareCategory.Sleep);

    (await f.ReadBacks.GetSleepAsync(f.Stranger, f.Owner, null, null)).Should().BeNull();
    (await f.ReadBacks.GetSleepAsync(sleep, f.Owner, null, null))!.Nights.Should().BeEmpty("no raw sleep samples are stored here");
  }

  [Fact]
  public async Task Sleep_FromAfterTo_IsAValidationError()
  {
    await using var f = await RecordFixture.SeededAsync();

    var act = () => f.ReadBacks.GetSleepAsync(f.Owner, f.Owner, RecordFixture.Today, RecordFixture.Today.AddDays(-1));

    await act.Should().ThrowAsync<DomainValidationException>().WithMessage("from must not be after to.");
  }

  // --- weekly review ---

  [Fact]
  public async Task Review_Owner_HasEverySection_ForLastWeek()
  {
    await using var f = await RecordFixture.SeededAsync();

    var dto = (await f.Review.GetAsync(f.Owner, f.Owner, null))!;

    dto.WeekOf.Should().Be(new DateOnly(2026, 11, 2));
    dto.WeekEnd.Should().Be(new DateOnly(2026, 11, 8));
    dto.Complete.Should().BeTrue();
    dto.Targets!.Kept.Select(t => t.Label).Should().Equal("Sleep");
    dto.Targets.Missed.Select(t => t.Label).Should().Equal("Lots of sleep");
    dto.Goals!.Should().ContainSingle().Which.Summary.Should().Be("82.4 kg to 81.9 kg over 2 readings, 0.5 kg down.");
    dto.Sleep!.Nights.Should().Be(7);
    dto.Sleep.NightsSevenHoursOrMore.Should().Be(7);
    dto.Sleep.Summary.Should().Contain("7 or more hours");
    dto.Insights!.Select(i => i.Kind).Should().Equal(InsightKinds.SleepGoodWeek);
    dto.Patterns.Should().NotBeNull().And.BeEmpty();
    dto.Standouts.Should().NotBeNull();
  }

  [Fact]
  public async Task Review_SleepOnlyMember_GetsOnlyTheSleepSections()
  {
    await using var f = await RecordFixture.SeededAsync();
    var member = await f.MemberWithAsync(ShareCategory.Sleep);

    var dto = (await f.Review.GetAsync(member, f.Owner, new DateOnly(2026, 11, 4)))!;

    dto.Sleep.Should().NotBeNull();
    dto.Standouts.Should().NotBeNull("sleep is one of the standout readings");
    dto.Targets.Should().BeNull();
    dto.Goals.Should().BeNull();
    dto.Insights.Should().BeNull();
    dto.Patterns.Should().BeNull();
  }

  [Fact]
  public async Task Review_BodyOnlyMember_SeesTargetsButNotTheSleepBehindThem()
  {
    await using var f = await RecordFixture.SeededAsync();
    var member = await f.MemberWithAsync(ShareCategory.Body);

    var dto = (await f.Review.GetAsync(member, f.Owner, null))!;

    dto.Targets!.Unknown.Should().HaveCount(2, "a target whose input is not shared is unknown, never missed");
    dto.Goals.Should().ContainSingle();
    dto.Sleep.Should().BeNull();
  }

  [Fact]
  public async Task Review_Stranger_IsNull()
  {
    await using var f = await RecordFixture.SeededAsync();

    (await f.Review.GetAsync(f.Stranger, f.Owner, null)).Should().BeNull();
  }

  [Fact]
  public async Task Review_FutureWeek_IsAValidationError()
  {
    await using var f = await RecordFixture.SeededAsync();

    var act = () => f.Review.GetAsync(f.Owner, f.Owner, RecordFixture.Today.AddDays(7));

    await act.Should().ThrowAsync<DomainValidationException>().WithMessage("weekOf cannot be in the future.");
  }

  [Theory]
  [InlineData("2026-11-08", "2026-11-08")]
  [InlineData("2026-11-10", "2026-11-08")]
  [InlineData("2026-11-09", "2026-11-08")]
  public void DefaultWeekDay_IsTodayOnASunday_ElseLastSunday(string today, string expected) =>
    WeeklyReviewService.DefaultWeekDay(DateOnly.Parse(today)).Should().Be(DateOnly.Parse(expected));

  [Fact]
  public void Standout_WeekWellAboveTheFourWeeksBefore_IsUp()
  {
    var monday = new DateOnly(2026, 11, 2);
    var rows = Enumerable.Range(1, 28).Select(i => new DailySummary { MetricKey = MetricKeys.RestingHrBpm, Day = monday.AddDays(-i), Value = 56 + i % 3 })
      .Concat(Enumerable.Range(0, 7).Select(i => new DailySummary { MetricKey = MetricKeys.RestingHrBpm, Day = monday.AddDays(i), Value = 63 }))
      .OrderBy(r => r.Day).ToList();

    var standout = WeeklyReviewService.Standout(MetricKeys.RestingHrBpm, rows, monday, UnitSystem.Metric);

    standout.Should().NotBeNull();
    standout!.Direction.Should().Be("up");
    standout.Difference.Should().Be(6);
    standout.Summary.Should().Be("Resting heart rate ran higher than the four weeks before: a median of 63 bpm against 57 bpm.");
  }

  [Fact]
  public void Standout_OrdinaryWeek_IsNull()
  {
    var monday = new DateOnly(2026, 11, 2);
    var rows = Enumerable.Range(-28, 35).Select(i => new DailySummary { MetricKey = MetricKeys.RestingHrBpm, Day = monday.AddDays(i), Value = 56 + Math.Abs(i) % 3 }).ToList();

    WeeklyReviewService.Standout(MetricKeys.RestingHrBpm, rows, monday, UnitSystem.Metric).Should().BeNull();
  }
}
