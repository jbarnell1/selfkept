using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure.Services.Interpret;
using SelfKept.UnitTests.Collect;
using Xunit;

namespace SelfKept.UnitTests.Interpret;

/// <summary>
/// The Today composition through the real repositories on the InMemory provider, with a fake
/// clock and a phraser that never phrases (so the templated sentence is what comes back).
/// </summary>
public class TodayServiceTests : IAsyncLifetime
{
  private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
  private static readonly DateOnly Today = new(2026, 10, 29);

  private readonly SelfKeptDbContext _context = new(
    new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  private readonly SeedCatalogue _catalogue = new();
  private readonly TestClock _clock = new(Now);
  private readonly Guid _household = Guid.NewGuid();
  private readonly Person _owner = new() { Id = Guid.NewGuid(), DisplayName = "Ada", TimeZone = "UTC" };
  private readonly Guid _granted = Guid.NewGuid();
  private readonly Guid _stranger = Guid.NewGuid();

  private sealed class NoPhraser : IInsightPhraser
  {
    public int Calls { get; private set; }
    public Task<string?> PhraseAsync(Guid personId, string purpose, string templated, CancellationToken ct = default)
    {
      Calls++;
      return Task.FromResult<string?>(null);
    }
  }

  private sealed class PlainEncryption : IEncryptionService
  {
    public string Encrypt(string plainText) => plainText;
    public string Decrypt(string cipherText) => cipherText;
    public bool IsCurrentFormat(string? cipherText) => true;
    public bool WritesCurrentFormat => true;
  }

  private readonly NoPhraser _phraser = new();

  private ShareGrantAccessPolicy Policy => new(new ShareGrantRepository(_context, _clock));
  private BaselineRepository Baselines => new(_context, Policy, _catalogue);
  private DailySummaryRepository Summaries => new(_context, Policy, _catalogue);
  private InsightRepository Insights => new(_context, Policy);
  private CheckinRepository Checkins => new(_context, Policy);
  private TodayCacheRepository Cache => new(_context);

  private TodayService Service => new(
    new PersonRepository(_context), Baselines, Summaries, Insights, Checkins, Cache, _phraser, new PlainEncryption(), _clock);

  public async Task InitializeAsync()
  {
    _context.Persons.Add(_owner);
    await _context.SaveChangesAsync();
    await new ShareGrantRepository(_context, _clock).ReplaceAsync(_household, _owner.Id, _granted, [Core.Enums.ShareCategory.Insights]);
  }

  public Task DisposeAsync() => _context.DisposeAsync().AsTask();

  /// <summary>28 days ending yesterday of HRV and resting HR, plus last night's sleep; baselines computed as the job would.</summary>
  private async Task SeedEstablishedAsync()
  {
    var rows = new List<DailySummary>();
    for (var i = 0; i < 28; i++)
    {
      var day = Today.AddDays(-28 + i);
      rows.Add(new DailySummary { PersonId = _owner.Id, MetricKey = "hrv_sdnn_ms", Day = day, Value = 50 + (i % 5), Count = 1, ComputedAt = Now.UtcDateTime });
      rows.Add(new DailySummary { PersonId = _owner.Id, MetricKey = "resting_hr_bpm", Day = day, Value = 55 + (i % 3), Count = 1, ComputedAt = Now.UtcDateTime });
      rows.Add(new DailySummary { PersonId = _owner.Id, MetricKey = "sleep_stage", Day = day, Value = 470 + (i % 4) * 5, Count = 4, ComputedAt = Now.UtcDateTime });
    }
    // Last night (credited to today): short sleep, low HRV, usual resting HR.
    rows.Add(new DailySummary { PersonId = _owner.Id, MetricKey = "sleep_stage", Day = Today, Value = 435, Count = 4, ComputedAt = Now.UtcDateTime });
    rows.Add(new DailySummary { PersonId = _owner.Id, MetricKey = "hrv_sdnn_ms", Day = Today, Value = 38, Count = 1, ComputedAt = Now.UtcDateTime });
    rows.Add(new DailySummary { PersonId = _owner.Id, MetricKey = "resting_hr_bpm", Day = Today, Value = 56, Count = 1, ComputedAt = Now.UtcDateTime });
    await Summaries.UpsertManyAsync(rows);

    var baselines = await new BaselineService(Summaries, _clock).ComputeAsync(_owner.Id, Today.AddDays(-1));
    await Baselines.UpsertManyAsync(baselines);
  }

  [Fact]
  public async Task Get_Self_MentionsSleepAndTheHrvBand_AndReadinessIsFilled()
  {
    await SeedEstablishedAsync();

    var today = await Service.GetAsync(_owner.Id, _owner.Id);

    today.Should().NotBeNull();
    today!.Day.Should().Be(Today);
    today.Learning.Should().BeFalse();
    today.SleepMinutes.Should().Be(435);
    today.Readiness.Sleep.Should().Be(BandPositions.Below);
    today.Readiness.Hrv.Should().Be(BandPositions.Below);
    today.Readiness.RestingHr.Should().Be(BandPositions.Inside);
    today.Sentence.Should().Contain("7 h 15 min").And.Contain("HRV is below your usual range");
    today.Generated.Should().BeFalse();
    today.Cards.Should().BeEmpty();
    today.LastCheckin.Should().BeNull();
  }

  [Fact]
  public async Task Get_StillLearning_SaysSo_AndBandsAreNull()
  {
    var rows = Enumerable.Range(0, 5).Select(i => new DailySummary
    {
      PersonId = _owner.Id, MetricKey = "hrv_sdnn_ms", Day = Today.AddDays(-5 + i), Value = 50, Count = 1, ComputedAt = Now.UtcDateTime,
    }).ToList();
    rows.Add(new DailySummary { PersonId = _owner.Id, MetricKey = "sleep_stage", Day = Today, Value = 480, Count = 1, ComputedAt = Now.UtcDateTime });
    await Summaries.UpsertManyAsync(rows);
    await Baselines.UpsertManyAsync(await new BaselineService(Summaries, _clock).ComputeAsync(_owner.Id, Today.AddDays(-1)));

    var today = await Service.GetAsync(_owner.Id, _owner.Id);

    today!.Learning.Should().BeTrue();
    today.Readiness.Hrv.Should().BeNull();
    today.Readiness.Sleep.Should().BeNull();
    today.SleepMinutes.Should().Be(480);
    today.Sentence.Should().Contain("You slept 8 h last night").And.Contain("Still learning your normal");
  }

  [Fact]
  public async Task Get_WithCards_ShowsTheBestTwo_MarksThemShown_AndTheSentencePointsAtTheTop()
  {
    await SeedEstablishedAsync();
    Insight Card(string kind, string severity, double distance, string templated) => new()
    {
      Id = Guid.CreateVersion7(), PersonId = _owner.Id, Day = Today.AddDays(-1), Kind = kind, Severity = severity, Templated = templated,
      Evidence = $"{{\"bandDistance\":{distance}}}", CreatedAt = Now.UtcDateTime,
    };
    await Insights.AddAsync(Card("activity_dip", "notice", 3, "A quieter week than your norm."));
    await Insights.AddAsync(Card("sleep_debt", "watch", 1, "Three short nights in four."));
    await Insights.AddAsync(Card("hrv_low_run", "watch", 2, "HRV has sat below your usual range for three nights."));

    var today = await Service.GetAsync(_owner.Id, _owner.Id);

    today!.Cards.Select(c => c.Kind).Should().Equal("hrv_low_run", "sleep_debt");
    today.Cards.Should().OnlyContain(c => c.ShownAt != null);
    today.Sentence.Should().EndWith("One thing is worth a look; it is below.", "the card sits right under the sentence, so the sentence points at it instead of repeating it");
    today.Sentence.Should().NotContain("HRV has sat below your usual range for three nights.");
    (await Insights.GetOwnByKindAsync(_owner.Id, "activity_dip", Today.AddDays(-1)))!.ShownAt.Should().BeNull("the third card lives in the archive");
  }

  [Fact]
  public async Task Get_KindMarkedWrongTwiceInThirtyDays_IsOffToday()
  {
    await SeedEstablishedAsync();
    for (var i = 1; i <= 2; i++)
    {
      await Insights.AddAsync(new Insight
      {
        Id = Guid.CreateVersion7(), PersonId = _owner.Id, Day = Today.AddDays(-i - 2), Kind = "sleep_debt", Severity = "watch", Templated = "x",
        Reaction = InsightReactions.Wrong, CreatedAt = Now.UtcDateTime,
      });
    }
    await Insights.AddAsync(new Insight { Id = Guid.CreateVersion7(), PersonId = _owner.Id, Day = Today.AddDays(-1), Kind = "sleep_debt", Severity = "watch", Templated = "Three short nights in four.", CreatedAt = Now.UtcDateTime });
    await Insights.AddAsync(new Insight { Id = Guid.CreateVersion7(), PersonId = _owner.Id, Day = Today.AddDays(-1), Kind = "activity_dip", Severity = "notice", Templated = "A quieter week.", CreatedAt = Now.UtcDateTime });

    var today = await Service.GetAsync(_owner.Id, _owner.Id);

    today!.Cards.Select(c => c.Kind).Should().Equal("activity_dip");
  }

  [Fact]
  public async Task Get_CachesTheSentenceForAnHour_AndRecomposesAfterInvalidation()
  {
    await SeedEstablishedAsync();

    await Service.GetAsync(_owner.Id, _owner.Id);
    await Service.GetAsync(_owner.Id, _owner.Id);
    _phraser.Calls.Should().Be(1, "the second read is served from today_cache");

    _clock.Advance(TimeSpan.FromMinutes(61));
    await Service.GetAsync(_owner.Id, _owner.Id);
    _phraser.Calls.Should().Be(2, "the hour passed");

    await Cache.InvalidateAsync(_owner.Id);
    await Service.GetAsync(_owner.Id, _owner.Id);
    _phraser.Calls.Should().Be(3, "a batch or check-in drops the cache");
  }

  [Fact]
  public async Task Get_GrantedMember_SeesTodayWithoutTheCheckin_UngrantedMember_IsNull()
  {
    await SeedEstablishedAsync();
    await Checkins.AddAsync(new Checkin { Id = Guid.CreateVersion7(), PersonId = _owner.Id, At = Now.UtcDateTime.AddHours(-1), Mood = 5, CreatedAt = Now.UtcDateTime });

    (await Service.GetAsync(_owner.Id, _owner.Id))!.LastCheckin!.Mood.Should().Be(5);
    var granted = await Service.GetAsync(_granted, _owner.Id);
    granted.Should().NotBeNull();
    granted!.Sentence.Should().NotBeEmpty();
    granted.LastCheckin.Should().BeNull("insights were shared, check-ins were not");
    (await Service.GetAsync(_stranger, _owner.Id)).Should().BeNull();
    (await Service.GetAsync(_owner.Id, Guid.NewGuid())).Should().BeNull();
  }
}

public class TodayComposerTests
{
  [Fact]
  public void Compose_SleepAndBothBands_ReadsAsOneCalmLine()
  {
    var text = TodayComposer.Compose(new TodayFacts(432, BandPositions.Below, BandPositions.Inside, BandPositions.Above, null, 4, false));
    text.Should().Be("You slept 7 h 12 min last night, a little short of your usual. HRV is inside your usual range and resting heart rate is a touch above it. Your last check-in was okay.");
  }

  [Theory]
  [InlineData("clinician", "One result is worth raising with a clinician; the details are below.")]
  [InlineData("watch", "One thing is worth a look; it is below.")]
  [InlineData("notice", "There is a note for you below.")]
  public void Compose_WithACard_PointsAtItInsteadOfRepeatingIt(string severity, string expected)
  {
    var text = TodayComposer.Compose(new TodayFacts(375, BandPositions.Below, BandPositions.Below, BandPositions.Inside, severity, 5, false));

    text.Should().Be($"You slept 6 h 15 min last night, a little short of your usual. HRV is below your usual range and resting heart rate is where it usually sits. {expected}");
  }

  [Fact]
  public void Compose_Learning_WithoutSleep_IsTheLearningLine()
  {
    TodayComposer.Compose(new TodayFacts(null, null, null, null, null, null, true))
      .Should().Be("Still learning your normal. A couple more weeks of data and the comparisons start.");
  }

  [Fact]
  public void Compose_NothingAtAll_StillSaysSomething()
  {
    TodayComposer.Compose(new TodayFacts(null, null, null, null, null, null, false))
      .Should().Be("No sleep recorded for last night.");
    TodayComposer.Compose(new TodayFacts(null, null, null, null, null, 3, false))
      .Should().Be("No sleep recorded for last night. Your last check-in was meh.");
  }

  [Fact]
  public void FormatMinutes_RoundsAndDropsEmptyParts()
  {
    TodayComposer.FormatMinutes(480).Should().Be("8 h");
    TodayComposer.FormatMinutes(45).Should().Be("45 min");
    TodayComposer.FormatMinutes(434.6).Should().Be("7 h 15 min");
  }
}

public class InterpretScheduleTests
{
  private static readonly TimeZoneInfo Brisbane = TimeZoneInfo.CreateCustomTimeZone("Test+10", TimeSpan.FromHours(10), "Test+10", "Test+10");

  [Fact]
  public void DueDay_BeforeHalfPastThreeLocal_IsNull()
  {
    // 17:00Z on 28 Oct is 03:00 on 29 Oct in +10.
    InterpretSchedule.DueDay(new DateTime(2026, 10, 28, 17, 0, 0, DateTimeKind.Utc), Brisbane).Should().BeNull();
  }

  [Fact]
  public void DueDay_FromHalfPastThreeLocal_IsYesterdayLocal()
  {
    // 17:30Z on 28 Oct is 03:30 on 29 Oct in +10: the 28th is due.
    InterpretSchedule.DueDay(new DateTime(2026, 10, 28, 17, 30, 0, DateTimeKind.Utc), Brisbane).Should().Be(new DateOnly(2026, 10, 28));
    InterpretSchedule.DueDay(new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc).Should().Be(new DateOnly(2026, 10, 28));
  }
}
