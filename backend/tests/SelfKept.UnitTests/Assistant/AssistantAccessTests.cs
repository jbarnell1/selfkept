using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure.Services.Connect;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

/// <summary>
/// Assistant rows are own-only: another household member reaches none of them, even holding
/// every share category the owner can grant.
/// </summary>
public class AssistantAccessTests : IAsyncLifetime
{
  private readonly string _database = Guid.NewGuid().ToString();
  private readonly SelfKeptDbContext _context;
  private readonly Guid _household = Guid.NewGuid();
  private readonly Guid _owner = Guid.NewGuid();
  private readonly Guid _member = Guid.NewGuid();
  private readonly Guid _conversationId = Guid.NewGuid();
  private readonly Guid _messageId = Guid.NewGuid();
  private static readonly DateTime Now = new(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
  private static readonly DateOnly Today = new(2026, 11, 10);

  public AssistantAccessTests()
  {
    _context = new SelfKeptDbContext(Options());
  }

  private DbContextOptions<SelfKeptDbContext> Options() =>
    new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(_database).Options;

  private AssistantRepository Assistant => new(_context);
  private NudgeRepository Nudges => new(_context);

  public async Task InitializeAsync()
  {
    // The member holds every grant there is: none of them opens assistant data.
    await new ShareGrantRepository(_context, TimeProvider.System).ReplaceAsync(_household, _owner, _member, Enum.GetValues<ShareCategory>());
    await Assistant.AddOwnConversationAsync(new AssistantConversation { Id = _conversationId, PersonId = _owner, CreatedAt = Now, UpdatedAt = Now });
    await Assistant.AddOwnConversationAsync(new AssistantConversation { Id = Guid.NewGuid(), PersonId = _owner, Kind = ConversationKinds.Checkin, Day = Today, CreatedAt = Now, UpdatedAt = Now });
    await Assistant.AddOwnMessageAsync(new AssistantMessage { Id = _messageId, ConversationId = _conversationId, PersonId = _owner, ContentCipher = "x", CreatedAt = Now });
    await Assistant.SaveOwnBriefAsync(new AssistantBrief { PersonId = _owner, Day = Today, FactsHash = "h", TextCipher = "x", ComputedAt = Now });
    await Nudges.SaveOwnAsync(new NudgeSetting { PersonId = _owner, Enabled = true, NtfyUrlCipher = "x", UpdatedAt = Now });
    await Nudges.TryAddOwnLogAsync(new NudgeLog { PersonId = _owner, Kind = NudgeKinds.Evening, Day = Today, SentAt = Now });
  }

  public Task DisposeAsync() => _context.DisposeAsync().AsTask();

  [Fact]
  public async Task GetOwnConversationAsync_Owner_ReturnsIt()
  {
    (await Assistant.GetOwnConversationAsync(_owner, _conversationId)).Should().NotBeNull();
  }

  [Fact]
  public async Task GetOwnConversationAsync_OtherMember_ReturnsNull()
  {
    (await Assistant.GetOwnConversationAsync(_member, _conversationId)).Should().BeNull();
  }

  [Fact]
  public async Task ListOwnConversationsAsync_OtherMember_IsEmpty()
  {
    (await Assistant.ListOwnConversationsAsync(_owner, 10)).Should().HaveCount(2);
    (await Assistant.ListOwnConversationsAsync(_member, 10)).Should().BeEmpty();
  }

  [Fact]
  public async Task GetOwnCheckinAsync_OtherMember_ReturnsNull()
  {
    (await Assistant.GetOwnCheckinAsync(_owner, Today)).Should().NotBeNull();
    (await Assistant.GetOwnCheckinAsync(_member, Today)).Should().BeNull();
  }

  [Fact]
  public async Task Messages_OtherMember_ReadsNothing()
  {
    (await Assistant.ListOwnMessagesAsync(_owner, _conversationId, 10)).Should().ContainSingle();
    (await Assistant.ListOwnMessagesAsync(_member, _conversationId, 10)).Should().BeEmpty();
    (await Assistant.GetOwnMessageAsync(_member, _messageId)).Should().BeNull();
  }

  [Fact]
  public async Task GetOwnBriefAsync_OtherMember_ReturnsNull()
  {
    (await Assistant.GetOwnBriefAsync(_owner, Today)).Should().NotBeNull();
    (await Assistant.GetOwnBriefAsync(_member, Today)).Should().BeNull();
  }

  [Fact]
  public async Task NudgeSettings_OtherMember_ReadsNothing()
  {
    (await Nudges.GetOwnAsync(_owner)).Should().NotBeNull();
    (await Nudges.GetOwnAsync(_member)).Should().BeNull();
    (await Nudges.HasOwnLogAsync(_member, NudgeKinds.Evening, Today)).Should().BeFalse();
    (await Nudges.LastOwnLogDayAsync(_member, NudgeKinds.Evening)).Should().BeNull();
  }

  [Fact]
  public async Task TryAddOwnLogAsync_SameDayTwice_RefusesTheSecond()
  {
    (await Nudges.TryAddOwnLogAsync(new NudgeLog { PersonId = _owner, Kind = NudgeKinds.Evening, Day = Today, SentAt = Now })).Should().BeFalse();
  }

  [Fact]
  public async Task Memory_OtherMember_CannotReadOrDelete()
  {
    var encryption = new EncryptionService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Encryption:Key"] = "unit-test-encryption-key-0a1b2c3d4e5f60718293a4b5c6d7e8f9",
    }).Build());
    var memory = new AiMemoryService(new Factory(Options()), encryption, TimeProvider.System);
    var fact = await memory.RememberAsync(_owner, AiMemoryTypes.Preference, "diet", "Eats a vegetarian diet", null, "test");

    (await memory.RecallAsync(_member, null)).Should().BeEmpty();
    (await memory.DeleteAsync(_member, fact.Id)).Should().BeFalse();
    (await memory.RecallAsync(_owner, null)).Should().ContainSingle();
  }

  private sealed class Factory(DbContextOptions<SelfKeptDbContext> options) : IDbContextFactory<SelfKeptDbContext>
  {
    public SelfKeptDbContext CreateDbContext() => new(options);
  }
}
