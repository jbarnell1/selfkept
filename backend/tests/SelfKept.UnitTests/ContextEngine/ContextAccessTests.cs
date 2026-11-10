using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.UnitTests.ContextEngine;

/// <summary>
/// The access rule on every new viewer read, through the real repositories and the real policy
/// on the InMemory provider: no grant is refused (null), a grant for another category is refused,
/// the right grant reads, and a person always reads their own.
/// </summary>
public class ContextAccessTests : IAsyncLifetime
{
  private readonly SelfKeptDbContext _context = new(
    new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  private readonly Guid _household = Guid.NewGuid();
  private readonly Guid _owner = Guid.NewGuid();
  private readonly Guid _insights = Guid.NewGuid();
  private readonly Guid _sleep = Guid.NewGuid();
  private readonly Guid _stranger = Guid.NewGuid();
  private static readonly DateOnly Day = new(2026, 11, 9);
  private static readonly DateTime Noon = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

  private ShareGrantRepository Grants => new(_context, TimeProvider.System);
  private ShareGrantAccessPolicy Policy => new(Grants);
  private ContextEventRepository Events => new(_context, Policy);
  private ReadBackRepository ReadBack => new(_context, Policy);

  public async Task InitializeAsync()
  {
    await Grants.ReplaceAsync(_household, _owner, _insights, [ShareCategory.Insights]);
    await Grants.ReplaceAsync(_household, _owner, _sleep, [ShareCategory.Sleep]);
    _context.ContextEvents.Add(new ContextEvent { Id = Guid.NewGuid(), PersonId = _owner, Kind = ContextKinds.Illness, StartedOn = Day.AddDays(-2), CreatedAt = Noon, UpdatedAt = Noon });
    _context.JournalEntries.Add(new JournalEntry { Id = Guid.NewGuid(), PersonId = _owner, At = Noon, Day = Day, Kind = JournalKinds.Drink, Tags = [JournalTags.Alcohol], CreatedAt = Noon });
    _context.DailySummaries.Add(new DailySummary { PersonId = _owner, MetricKey = MetricKeys.SleepStage, Day = Day.AddDays(1), Value = 400, Count = 1, ComputedAt = Noon });
    _context.Samples.Add(new Sample { Id = Guid.NewGuid(), PersonId = _owner, SourceId = Guid.NewGuid(), SourceUid = "s1", MetricKey = MetricKeys.SleepStage, StartAt = Noon.AddHours(-10), EndAt = Noon.AddHours(-9), Value = SleepStages.Deep, Unit = "stage", CreatedAt = Noon });
    await _context.SaveChangesAsync();
  }

  public Task DisposeAsync() => _context.DisposeAsync().AsTask();

  // --- context events: insights ---

  [Fact]
  public async Task ContextList_StrangerWithoutAGrant_IsNull() =>
    (await Events.ListAsync(_stranger, _owner, Day.AddDays(-7), Day)).Should().BeNull();

  [Fact]
  public async Task ContextList_GrantForAnotherCategory_IsNull() =>
    (await Events.ListAsync(_sleep, _owner, Day.AddDays(-7), Day)).Should().BeNull();

  [Fact]
  public async Task ContextList_InsightsGrant_ReturnsTheEvents() =>
    (await Events.ListAsync(_insights, _owner, Day.AddDays(-7), Day))!.Should().ContainSingle().Which.Kind.Should().Be(ContextKinds.Illness);

  [Fact]
  public async Task ContextList_Self_ReturnsTheEvents() =>
    (await Events.ListAsync(_owner, _owner, Day.AddDays(-7), Day))!.Should().HaveCount(1);

  [Fact]
  public async Task ContextList_OpenEventOlderThanItsAllowance_IsLeftOut() =>
    (await Events.ListAsync(_owner, _owner, Day.AddDays(20), Day.AddDays(25)))!.Should().BeEmpty("an open event counts for fourteen days");

  // --- pattern inputs: insights ---

  [Fact]
  public async Task PatternInputs_StrangerWithoutAGrant_IsNull() =>
    (await ReadBack.GetPatternInputsAsync(_stranger, _owner, [MetricKeys.SleepStage], Day.AddDays(-7), Day)).Should().BeNull();

  [Fact]
  public async Task PatternInputs_GrantForAnotherCategory_IsNull() =>
    (await ReadBack.GetPatternInputsAsync(_sleep, _owner, [MetricKeys.SleepStage], Day.AddDays(-7), Day)).Should().BeNull();

  [Fact]
  public async Task PatternInputs_InsightsGrant_ReturnsTagsAndTheNightAfter()
  {
    var inputs = await ReadBack.GetPatternInputsAsync(_insights, _owner, [MetricKeys.SleepStage], Day.AddDays(-7), Day);

    inputs.Should().NotBeNull();
    inputs!.Days.Should().ContainSingle().Which.Tags.Should().Equal(JournalTags.Alcohol);
    inputs.Nights.Should().ContainSingle().Which.Day.Should().Be(Day.AddDays(1), "the night after the last day in the range is read too");
  }

  // --- sleep samples: sleep ---

  [Fact]
  public async Task SleepSamples_StrangerWithoutAGrant_IsNull() =>
    (await ReadBack.GetSleepSamplesAsync(_stranger, _owner, Noon.AddDays(-1), Noon)).Should().BeNull();

  [Fact]
  public async Task SleepSamples_GrantForAnotherCategory_IsNull() =>
    (await ReadBack.GetSleepSamplesAsync(_insights, _owner, Noon.AddDays(-1), Noon)).Should().BeNull();

  [Fact]
  public async Task SleepSamples_SleepGrant_ReturnsTheSamples() =>
    (await ReadBack.GetSleepSamplesAsync(_sleep, _owner, Noon.AddDays(-1), Noon))!.Should().ContainSingle();

  // --- allowed categories ---

  [Fact]
  public async Task AllowedCategories_StrangerIsEmpty_GrantedIsTheGrant_SelfIsEverything()
  {
    (await ReadBack.AllowedCategoriesAsync(_stranger, _owner)).Should().BeEmpty();
    (await ReadBack.AllowedCategoriesAsync(_sleep, _owner)).Should().BeEquivalentTo([ShareCategory.Sleep]);
    (await ReadBack.AllowedCategoriesAsync(_owner, _owner)).Should().HaveCount(Enum.GetValues<ShareCategory>().Length);
  }
}
