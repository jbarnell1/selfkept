using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.UnitTests.Labs;

/// <summary>
/// The body share rule on every trial read, through the real repository and policy on InMemory:
/// an ungranted member and a member with a grant for another category are refused, a member with
/// the body grant and the owner are not.
/// </summary>
public class TrialAccessTests : IAsyncLifetime
{
  private readonly SelfKeptDbContext _context = new(
    new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  private readonly Guid _household = Guid.NewGuid();
  private readonly Guid _owner = Guid.NewGuid();
  private readonly Guid _bodyViewer = Guid.NewGuid();
  private readonly Guid _labsViewer = Guid.NewGuid();
  private readonly Guid _stranger = Guid.NewGuid();
  private readonly Guid _activeId = Guid.NewGuid();
  private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

  private ShareGrantRepository Grants => new(_context, TimeProvider.System);
  private TrialRepository Trials => new(_context, new ShareGrantAccessPolicy(Grants));

  public async Task InitializeAsync()
  {
    await Grants.ReplaceAsync(_household, _owner, _bodyViewer, [ShareCategory.Body]);
    await Grants.ReplaceAsync(_household, _owner, _labsViewer, [ShareCategory.Labs]);
    await Trials.AddAsync(Trial(_activeId, TrialStatuses.Active, new DateOnly(2026, 9, 1)));
    await Trials.AddAsync(Trial(Guid.NewGuid(), TrialStatuses.Done, new DateOnly(2026, 6, 1)));
  }

  private Trial Trial(Guid id, string status, DateOnly started) => new()
  {
    Id = id, PersonId = _owner, TitleCipher = "cipher", StartedOn = started, ReadOn = started.AddDays(90),
    MetricKeys = ["lab.ldl"], Status = status, CreatedAt = Now, UpdatedAt = Now,
  };

  public Task DisposeAsync() => _context.DisposeAsync().AsTask();

  [Fact]
  public async Task List_UngrantedMember_IsNull()
  {
    (await Trials.ListAsync(_stranger, _owner, includeEnded: true)).Should().BeNull();
  }

  [Fact]
  public async Task List_GrantForAnotherCategory_IsNull()
  {
    (await Trials.ListAsync(_labsViewer, _owner, includeEnded: true)).Should().BeNull("labs was granted, trials are read under body");
  }

  [Fact]
  public async Task List_MemberWithBodyGrant_SeesActiveOnly_UnlessEndedAsked()
  {
    (await Trials.ListAsync(_bodyViewer, _owner, includeEnded: false))!.Should().ContainSingle().Which.Id.Should().Be(_activeId);
    (await Trials.ListAsync(_bodyViewer, _owner, includeEnded: true))!.Should().HaveCount(2);
  }

  [Fact]
  public async Task List_Self_NewestStartFirst()
  {
    var rows = await Trials.ListAsync(_owner, _owner, includeEnded: true);

    rows!.Select(t => t.StartedOn).Should().BeInDescendingOrder();
  }

  [Fact]
  public async Task GetOwn_IsTheOwnersOnly_EvenWithAGrant()
  {
    (await Trials.GetOwnAsync(_owner, _activeId)).Should().NotBeNull();
    (await Trials.GetOwnAsync(_bodyViewer, _activeId)).Should().BeNull("a grant lets you read, not edit");
  }

  [Fact]
  public async Task ListOwnActive_IsActiveOnly_OldestFirst()
  {
    (await Trials.ListOwnActiveAsync(_owner)).Should().ContainSingle().Which.Id.Should().Be(_activeId);
    (await Trials.ListOwnActiveAsync(_bodyViewer)).Should().BeEmpty();
  }

  [Fact]
  public async Task Remove_DeletesTheRow()
  {
    var trial = await Trials.GetOwnAsync(_owner, _activeId);
    await Trials.RemoveAsync(trial!);

    (await Trials.ListAsync(_owner, _owner, includeEnded: true))!.Should().ContainSingle();
  }
}
