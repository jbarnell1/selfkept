using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

/// <summary>
/// Cloud lookup rows are own-only: another household member reaches none of them, even holding
/// every share category the owner can grant.
/// </summary>
public class LookupAccessTests : IAsyncLifetime
{
  private readonly SelfKeptDbContext _context = new(new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  private readonly Guid _household = Guid.NewGuid();
  private readonly Guid _owner = Guid.NewGuid();
  private readonly Guid _member = Guid.NewGuid();
  private readonly Guid _conversationId = Guid.NewGuid();
  private readonly Guid _lookupId = Guid.NewGuid();
  private static readonly DateTime Now = new(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);

  private LookupRepository Lookups => new(_context);

  public async Task InitializeAsync()
  {
    await new ShareGrantRepository(_context, TimeProvider.System).ReplaceAsync(_household, _owner, _member, Enum.GetValues<ShareCategory>());
    await new AssistantRepository(_context).AddOwnConversationAsync(new AssistantConversation { Id = _conversationId, PersonId = _owner, CreatedAt = Now, UpdatedAt = Now });
    await Lookups.SaveOwnSettingAsync(new LookupSetting { PersonId = _owner, Enabled = true, EnabledAt = Now, UpdatedAt = Now });
    await Lookups.AddOwnAsync(new AssistantLookup
    {
      Id = _lookupId, PersonId = _owner, ConversationId = _conversationId, Mode = LookupModes.Anonymized, QuestionCipher = "x",
      Provider = "Anthropic", Model = "claude-opus-5-5", CreatedAt = Now,
    });
  }

  public Task DisposeAsync() => _context.DisposeAsync().AsTask();

  [Fact]
  public async Task GetOwnSettingAsync_OtherMember_ReadsNothing()
  {
    (await Lookups.GetOwnSettingAsync(_owner))!.Enabled.Should().BeTrue();
    (await Lookups.GetOwnSettingAsync(_member)).Should().BeNull();
  }

  [Fact]
  public async Task SaveOwnSettingAsync_OtherMembersOwnRow_LeavesTheOwnersAlone()
  {
    await Lookups.SaveOwnSettingAsync(new LookupSetting { PersonId = _member, Enabled = false, UpdatedAt = Now });

    (await Lookups.GetOwnSettingAsync(_owner))!.Enabled.Should().BeTrue();
    (await Lookups.GetOwnSettingAsync(_member))!.Enabled.Should().BeFalse();
  }

  [Fact]
  public async Task GetOwnAsync_OtherMember_ReturnsNull()
  {
    (await Lookups.GetOwnAsync(_owner, _lookupId)).Should().NotBeNull();
    (await Lookups.GetOwnAsync(_member, _lookupId)).Should().BeNull();
  }

  [Fact]
  public async Task ListOwnAsync_OtherMember_IsEmpty()
  {
    (await Lookups.ListOwnAsync(_owner, 10)).Should().ContainSingle();
    (await Lookups.ListOwnAsync(_member, 10)).Should().BeEmpty();
  }

  [Fact]
  public async Task ListOwnByIdsAsync_OtherMember_IsEmpty()
  {
    (await Lookups.ListOwnByIdsAsync(_owner, [_lookupId])).Should().ContainSingle();
    (await Lookups.ListOwnByIdsAsync(_member, [_lookupId])).Should().BeEmpty();
  }

  [Fact]
  public async Task RemoveOwnAsync_OtherMember_RemovesNothing()
  {
    (await Lookups.RemoveOwnAsync(_member, _lookupId)).Should().BeFalse();
    (await Lookups.GetOwnAsync(_owner, _lookupId)).Should().NotBeNull();
    (await Lookups.RemoveOwnAsync(_owner, _lookupId)).Should().BeTrue();
    (await Lookups.GetOwnAsync(_owner, _lookupId)).Should().BeNull();
  }

  [Fact]
  public async Task UpdateOwnAsync_Owner_SavesTheAnswer()
  {
    var lookup = (await Lookups.GetOwnAsync(_owner, _lookupId))!;
    lookup.Status = LookupStatuses.Answered;

    await Lookups.UpdateOwnAsync(lookup);

    (await Lookups.ListOwnAsync(_owner, 10))[0].Status.Should().Be(LookupStatuses.Answered);
  }

  [Fact]
  public async Task RemoveOwnConversationAsync_RemovesItsLookups()
  {
    var assistant = new AssistantRepository(_context);
    await assistant.RemoveOwnConversationAsync((await assistant.GetOwnConversationAsync(_owner, _conversationId))!);

    (await Lookups.ListOwnAsync(_owner, 10)).Should().BeEmpty();
  }
}
