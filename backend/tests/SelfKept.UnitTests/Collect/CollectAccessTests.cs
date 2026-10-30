using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.UnitTests.Collect;

/// <summary>
/// The access rule on every data read, through the real repositories, the real share_grant
/// repository and the real policy on the InMemory provider: self ok, granted member ok,
/// ungranted member 404 (null), grant for a different category 404 (null).
/// </summary>
public class CollectAccessTests : IAsyncLifetime
{
  private readonly SelfKeptDbContext _context = new(
    new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  private readonly SeedCatalogue _catalogue = new();
  private readonly Guid _household = Guid.NewGuid();
  private readonly Guid _owner = Guid.NewGuid();
  private readonly Guid _granted = Guid.NewGuid();
  private readonly Guid _stranger = Guid.NewGuid();
  private readonly Guid _sourceId = Guid.NewGuid();
  private static readonly DateTime Noon = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

  private ShareGrantRepository Grants => new(_context, TimeProvider.System);
  private ShareGrantAccessPolicy Policy => new(Grants);
  private SampleRepository Samples => new(_context, Policy, _catalogue);
  private DailySummaryRepository Summaries => new(_context, Policy, _catalogue);
  private CheckinRepository Checkins => new(_context, Policy);
  private WorkoutRepository Workouts => new(_context, Policy);

  public async Task InitializeAsync()
  {
    // _granted may see the owner's activity, and nothing else.
    await Grants.ReplaceAsync(_household, _owner, _granted, [ShareCategory.Activity]);
    await Samples.InsertManyAsync(
    [
      Sample("s-1", "steps", Noon.AddHours(-3), 500, Noon.AddHours(-2)),
      Sample("s-2", "steps", Noon.AddHours(-1), 700, Noon),
      Sample("hr-1", "heart_rate_bpm", Noon.AddHours(-1), 62),
      // Same instant: the id breaks the tie, so the ids are fixed rather than left to the clock.
      Sample("hr-2", "heart_rate_bpm", Noon, 70, id: new Guid("00000000-0000-7000-8000-000000000002")),
      Sample("hr-3", "heart_rate_bpm", Noon, 71, id: new Guid("00000000-0000-7000-8000-000000000003")),
    ]);
    await Summaries.UpsertManyAsync(
    [
      new DailySummary { PersonId = _owner, MetricKey = "steps", Day = new DateOnly(2026, 10, 29), Value = 1200, Count = 2, ComputedAt = Noon },
      new DailySummary { PersonId = _owner, MetricKey = "heart_rate_bpm", Day = new DateOnly(2026, 10, 29), Value = 67, Min = 62, Max = 71, Count = 3, ComputedAt = Noon },
    ]);
    await Checkins.AddAsync(new Checkin { Id = Guid.CreateVersion7(), PersonId = _owner, At = Noon, Mood = 4, CreatedAt = Noon });
  }

  public Task DisposeAsync() => _context.DisposeAsync().AsTask();

  private Sample Sample(string uid, string metric, DateTime start, double value, DateTime? end = null, Guid? id = null) => new()
  {
    Id = id ?? Guid.CreateVersion7(), PersonId = _owner, SourceId = _sourceId, SourceUid = uid, MetricKey = metric,
    StartAt = start, EndAt = end, Value = value, Unit = _catalogue.Find(metric)!.Unit, CreatedAt = Noon,
  };

  // --- samples ---

  [Fact]
  public async Task GetPage_Self_ReturnsRows()
  {
    var page = await Samples.GetPageAsync(_owner, _owner, "steps", null, null, 10, null);

    page.Should().NotBeNull();
    page!.Items.Should().HaveCount(2);
    page.Items[0].SourceUid.Should().Be("s-2", "newest first");
  }

  [Fact]
  public async Task GetPage_GrantedMemberSameCategory_ReturnsRows()
  {
    (await Samples.GetPageAsync(_granted, _owner, "steps", null, null, 10, null))!.Items.Should().HaveCount(2);
  }

  [Fact]
  public async Task GetPage_UngrantedMember_IsNull()
  {
    (await Samples.GetPageAsync(_stranger, _owner, "steps", null, null, 10, null)).Should().BeNull();
  }

  [Fact]
  public async Task GetPage_GrantForAnotherCategory_IsNull()
  {
    (await Samples.GetPageAsync(_granted, _owner, "heart_rate_bpm", null, null, 10, null)).Should().BeNull("activity was granted, not vitals");
  }

  [Fact]
  public async Task GetPage_UnknownMetric_IsNull()
  {
    (await Samples.GetPageAsync(_owner, _owner, "bogus", null, null, 10, null)).Should().BeNull();
  }

  [Fact]
  public async Task GetPage_KeysetCursor_WalksEveryRowOnceNewestFirst()
  {
    var seen = new List<string>();
    var first = await Samples.GetPageAsync(_owner, _owner, "heart_rate_bpm", null, null, 2, null);
    first!.Items.Should().HaveCount(2);
    first.Next.Should().NotBeNull();
    seen.AddRange(first.Items.Select(s => s.SourceUid));

    var second = await Samples.GetPageAsync(_owner, _owner, "heart_rate_bpm", null, null, 2, first.Next);
    second!.Items.Should().HaveCount(1);
    second.Next.Should().BeNull();
    seen.AddRange(second.Items.Select(s => s.SourceUid));

    seen.Should().BeEquivalentTo(new[] { "hr-1", "hr-2", "hr-3" });
    seen[2].Should().Be("hr-1", "the oldest comes last");
  }

  [Fact]
  public async Task InsertMany_SameSourceUidAgain_IsCountedAsDuplicate()
  {
    var result = await Samples.InsertManyAsync([Sample("s-1", "steps", Noon.AddHours(-3), 500, Noon.AddHours(-2)), Sample("s-9", "steps", Noon, 1, Noon)]);

    result.Accepted.Should().Be(1);
    result.Duplicates.Should().Be(1);
    (await Samples.GetPageAsync(_owner, _owner, "steps", null, null, 10, null))!.Items.Should().HaveCount(3);
  }

  [Fact]
  public async Task DeleteBySourceUid_RemovesOnlyThatReading()
  {
    (await Samples.DeleteBySourceUidAsync(_sourceId, "hr-1")).Should().Be(1);
    (await Samples.DeleteBySourceUidAsync(_sourceId, "hr-1")).Should().Be(0);
    (await Samples.GetPageAsync(_owner, _owner, "heart_rate_bpm", null, null, 10, null))!.Items.Should().HaveCount(2);
  }

  [Fact]
  public async Task GetOwnInWindow_ByStartAndByEnd_RespectTheWindow()
  {
    var byStart = await Samples.GetOwnInWindowAsync(_owner, "steps", Noon.AddHours(-2), Noon.AddHours(1), byEnd: false);
    byStart.Select(s => s.SourceUid).Should().BeEquivalentTo(new[] { "s-2" });

    var byEnd = await Samples.GetOwnInWindowAsync(_owner, "steps", Noon.AddHours(-3), Noon, byEnd: true);
    byEnd.Select(s => s.SourceUid).Should().BeEquivalentTo(new[] { "s-1" }, "s-2 ends exactly at the exclusive upper bound");
  }

  // --- overview ---

  [Fact]
  public async Task GetOverview_Self_ListsEveryMetricWithCountsAndLatestValue()
  {
    var rows = await Samples.GetOverviewAsync(_owner, _owner);

    rows.Should().NotBeNull().And.HaveCount(2);
    var steps = rows!.Single(r => r.MetricKey == "steps");
    steps.Count.Should().Be(2);
    steps.LastValue.Should().Be(700);
    steps.FirstAt.Should().Be(Noon.AddHours(-3));
    steps.LastAt.Should().Be(Noon.AddHours(-1));
    rows!.Single(r => r.MetricKey == "heart_rate_bpm").LastValue.Should().Be(71, "same instant, higher id wins");
  }

  [Fact]
  public async Task GetOverview_GrantedMember_SeesOnlyGrantedCategories()
  {
    var rows = await Samples.GetOverviewAsync(_granted, _owner);

    rows!.Select(r => r.MetricKey).Should().BeEquivalentTo(new[] { "steps" });
  }

  [Fact]
  public async Task GetOverview_UngrantedMember_IsNull()
  {
    (await Samples.GetOverviewAsync(_stranger, _owner)).Should().BeNull();
  }

  // --- daily summaries ---

  [Fact]
  public async Task GetRange_SelfAndGranted_ReturnRows_UngrantedAndWrongCategory_AreNull()
  {
    var day = new DateOnly(2026, 10, 29);
    (await Summaries.GetRangeAsync(_owner, _owner, "steps", day, day))!.Should().ContainSingle(d => d.Value == 1200);
    (await Summaries.GetRangeAsync(_granted, _owner, "steps", day, day))!.Should().HaveCount(1);
    (await Summaries.GetRangeAsync(_stranger, _owner, "steps", day, day)).Should().BeNull();
    (await Summaries.GetRangeAsync(_granted, _owner, "heart_rate_bpm", day, day)).Should().BeNull();
    (await Summaries.GetRangeAsync(_owner, _owner, "nope", day, day)).Should().BeNull();
  }

  [Fact]
  public async Task UpsertMany_ExistingDay_IsReplacedNotDuplicated()
  {
    var day = new DateOnly(2026, 10, 29);
    await Summaries.UpsertManyAsync([new DailySummary { PersonId = _owner, MetricKey = "steps", Day = day, Value = 9999, Count = 3, ComputedAt = Noon }]);

    var rows = await Summaries.GetRangeAsync(_owner, _owner, "steps", day, day);
    rows!.Should().ContainSingle().Which.Value.Should().Be(9999);

    await Summaries.DeleteAsync(_owner, "steps", day);
    (await Summaries.GetRangeAsync(_owner, _owner, "steps", day, day))!.Should().BeEmpty();
  }

  // --- check-ins and workouts ---

  [Fact]
  public async Task Checkins_List_SelfOk_UngrantedNull_ActivityGrantIsNotCheckins()
  {
    var from = Noon.AddDays(-1);
    var to = Noon.AddDays(1);
    (await Checkins.ListAsync(_owner, _owner, from, to, 500))!.Should().HaveCount(1);
    (await Checkins.ListAsync(_stranger, _owner, from, to, 500)).Should().BeNull();
    (await Checkins.ListAsync(_granted, _owner, from, to, 500)).Should().BeNull("activity was granted, not checkins");

    await Grants.ReplaceAsync(_household, _owner, _granted, [ShareCategory.Activity, ShareCategory.Checkins]);
    (await Checkins.ListAsync(_granted, _owner, from, to, 500))!.Should().HaveCount(1);
  }

  [Fact]
  public async Task Checkins_GetOwn_SomeoneElsesId_IsNull()
  {
    var mine = (await Checkins.ListAsync(_owner, _owner, Noon.AddDays(-1), Noon.AddDays(1), 500))!.Single();

    (await Checkins.GetOwnAsync(_owner, mine.Id)).Should().NotBeNull();
    (await Checkins.GetOwnAsync(_granted, mine.Id)).Should().BeNull();
  }

  [Fact]
  public async Task Workouts_InsertDedupes_AndListFollowsTheActivityGrant()
  {
    Workout W(string uid) => new() { Id = Guid.CreateVersion7(), PersonId = _owner, SourceId = _sourceId, SourceUid = uid, Type = "running", StartAt = Noon.AddHours(-2), EndAt = Noon.AddHours(-1), CreatedAt = Noon };
    (await Workouts.InsertManyAsync([W("w-1"), W("w-2")])).Accepted.Should().Be(2);
    var again = await Workouts.InsertManyAsync([W("w-1"), W("w-3")]);
    again.Accepted.Should().Be(1);
    again.Duplicates.Should().Be(1);

    (await Workouts.ListAsync(_owner, _owner, Noon.AddDays(-1), Noon))!.Should().HaveCount(3);
    (await Workouts.ListAsync(_granted, _owner, Noon.AddDays(-1), Noon))!.Should().HaveCount(3);
    (await Workouts.ListAsync(_stranger, _owner, Noon.AddDays(-1), Noon)).Should().BeNull();
  }
}
