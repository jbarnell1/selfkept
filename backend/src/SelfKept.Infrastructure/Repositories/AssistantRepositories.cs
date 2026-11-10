using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Repositories;

/// <summary>
/// Conversations, messages and the morning brief. Own-only: every read is filtered on the
/// caller's own person id and there is no share category, so another household member never
/// reaches these rows, whatever they have been granted.
/// </summary>
public sealed class AssistantRepository : IAssistantRepository
{
  private readonly SelfKeptDbContext _context;

  public AssistantRepository(SelfKeptDbContext context) => _context = context;

  public async Task<AssistantConversation> AddOwnConversationAsync(AssistantConversation conversation, CancellationToken ct = default)
  {
    _context.AssistantConversations.Add(conversation);
    await _context.SaveChangesAsync(ct);
    return conversation;
  }

  public Task<AssistantConversation?> GetOwnConversationAsync(Guid personId, Guid conversationId, CancellationToken ct = default) =>
    _context.AssistantConversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.PersonId == personId, ct);

  public async Task<IReadOnlyList<AssistantConversation>> ListOwnConversationsAsync(Guid personId, int limit, CancellationToken ct = default) =>
    await _context.AssistantConversations.AsNoTracking()
      .Where(c => c.PersonId == personId)
      .OrderByDescending(c => c.UpdatedAt)
      .Take(limit)
      .ToListAsync(ct);

  public Task<AssistantConversation?> GetOwnCheckinAsync(Guid personId, DateOnly day, CancellationToken ct = default) =>
    _context.AssistantConversations.FirstOrDefaultAsync(c => c.PersonId == personId && c.Kind == ConversationKinds.Checkin && c.Day == day, ct);

  public async Task UpdateOwnConversationAsync(AssistantConversation conversation, CancellationToken ct = default)
  {
    if (_context.Entry(conversation).State == EntityState.Detached) _context.AssistantConversations.Update(conversation);
    await _context.SaveChangesAsync(ct);
  }

  public async Task RemoveOwnConversationAsync(AssistantConversation conversation, CancellationToken ct = default)
  {
    // The foreign key cascades in Postgres; removing the messages here keeps the InMemory provider in step.
    var messages = await _context.AssistantMessages.Where(m => m.ConversationId == conversation.Id && m.PersonId == conversation.PersonId).ToListAsync(ct);
    _context.AssistantMessages.RemoveRange(messages);
    _context.AssistantConversations.Remove(conversation);
    await _context.SaveChangesAsync(ct);
  }

  public async Task<AssistantMessage> AddOwnMessageAsync(AssistantMessage message, CancellationToken ct = default)
  {
    _context.AssistantMessages.Add(message);
    await _context.SaveChangesAsync(ct);
    return message;
  }

  public Task<AssistantMessage?> GetOwnMessageAsync(Guid personId, Guid messageId, CancellationToken ct = default) =>
    _context.AssistantMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.PersonId == personId, ct);

  public async Task<IReadOnlyList<AssistantMessage>> ListOwnMessagesAsync(Guid personId, Guid conversationId, int limit, CancellationToken ct = default)
  {
    var newest = await _context.AssistantMessages.AsNoTracking()
      .Where(m => m.ConversationId == conversationId && m.PersonId == personId)
      .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
      .Take(limit)
      .ToListAsync(ct);
    newest.Reverse();
    return newest;
  }

  public async Task UpdateOwnMessageAsync(AssistantMessage message, CancellationToken ct = default)
  {
    if (_context.Entry(message).State == EntityState.Detached) _context.AssistantMessages.Update(message);
    await _context.SaveChangesAsync(ct);
  }

  public Task<AssistantBrief?> GetOwnBriefAsync(Guid personId, DateOnly day, CancellationToken ct = default) =>
    _context.AssistantBriefs.AsNoTracking().FirstOrDefaultAsync(b => b.PersonId == personId && b.Day == day, ct);

  public async Task SaveOwnBriefAsync(AssistantBrief brief, CancellationToken ct = default)
  {
    var existing = await _context.AssistantBriefs.FirstOrDefaultAsync(b => b.PersonId == brief.PersonId && b.Day == brief.Day, ct);
    if (existing == null)
    {
      _context.AssistantBriefs.Add(brief);
    }
    else
    {
      existing.FactsHash = brief.FactsHash;
      existing.TextCipher = brief.TextCipher;
      existing.Generated = brief.Generated;
      existing.ComputedAt = brief.ComputedAt;
    }
    await _context.SaveChangesAsync(ct);
  }
}

/// <summary>Nudge settings and the sent log. Own-only, like the conversations.</summary>
public sealed class NudgeRepository : INudgeRepository
{
  private readonly SelfKeptDbContext _context;

  public NudgeRepository(SelfKeptDbContext context) => _context = context;

  public Task<NudgeSetting?> GetOwnAsync(Guid personId, CancellationToken ct = default) =>
    _context.NudgeSettings.AsNoTracking().FirstOrDefaultAsync(n => n.PersonId == personId, ct);

  public async Task SaveOwnAsync(NudgeSetting setting, CancellationToken ct = default)
  {
    var existing = await _context.NudgeSettings.FirstOrDefaultAsync(n => n.PersonId == setting.PersonId, ct);
    if (existing == null)
    {
      _context.NudgeSettings.Add(setting);
    }
    else
    {
      existing.Enabled = setting.Enabled;
      existing.NtfyUrlCipher = setting.NtfyUrlCipher;
      existing.NtfyTokenCipher = setting.NtfyTokenCipher;
      existing.EveningTime = setting.EveningTime;
      existing.UpdatedAt = setting.UpdatedAt;
    }
    await _context.SaveChangesAsync(ct);
  }

  public async Task<IReadOnlyList<NudgeSetting>> ListEnabledForJobAsync(CancellationToken ct = default) =>
    await _context.NudgeSettings.AsNoTracking().Where(n => n.Enabled && n.NtfyUrlCipher != null).ToListAsync(ct);

  public Task<bool> HasOwnLogAsync(Guid personId, string kind, DateOnly day, CancellationToken ct = default) =>
    _context.NudgeLogs.AnyAsync(l => l.PersonId == personId && l.Kind == kind && l.Day == day, ct);

  public async Task<bool> TryAddOwnLogAsync(NudgeLog log, CancellationToken ct = default)
  {
    if (await HasOwnLogAsync(log.PersonId, log.Kind, log.Day, ct)) return false;
    _context.NudgeLogs.Add(log);
    try
    {
      await _context.SaveChangesAsync(ct);
      return true;
    }
    catch (DbUpdateException)
    {
      // Another writer logged the same (person, kind, day) first: the primary key refused ours.
      _context.Entry(log).State = EntityState.Detached;
      return false;
    }
  }

  public Task<DateOnly?> LastOwnLogDayAsync(Guid personId, string kind, CancellationToken ct = default) =>
    _context.NudgeLogs.AsNoTracking()
      .Where(l => l.PersonId == personId && l.Kind == kind)
      .OrderByDescending(l => l.Day)
      .Select(l => (DateOnly?)l.Day)
      .FirstOrDefaultAsync(ct);
}
