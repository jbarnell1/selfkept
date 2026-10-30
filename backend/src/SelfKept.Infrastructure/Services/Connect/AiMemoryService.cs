using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Services.Connect;

/// <summary>
/// Notes an assistant keeps about a person (the remember tool) and reads back (recall_memory).
/// One item per (type, key): remembering the same key again replaces it. Values are encrypted at
/// rest because they are health facts ("takes 50 mg sertraline"). The app lists and deletes them.
/// </summary>
public sealed class AiMemoryService : IAiMemoryService
{
  public const int MaxItemsPerPerson = 200;
  public const int MaxValueLength = 1000;
  public const int MaxKeyLength = 80;
  public const int MaxDisplayLength = 200;

  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly IEncryptionService _encryption;
  private readonly TimeProvider _clock;

  public AiMemoryService(IDbContextFactory<SelfKeptDbContext> contextFactory, IEncryptionService encryption, TimeProvider clock)
  {
    _contextFactory = contextFactory;
    _encryption = encryption;
    _clock = clock;
  }

  public async Task<IReadOnlyList<AiMemoryDto>> RecallAsync(Guid personId, string? topic, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var rows = await context.AiMemories.AsNoTracking()
      .Where(m => m.PersonId == personId)
      .OrderByDescending(m => m.UpdatedAt)
      .ToListAsync(ct);
    var items = rows.Select(ToDto).ToList();
    if (string.IsNullOrWhiteSpace(topic))
      return items;

    // The value is encrypted, so the topic filter runs here, after decryption.
    var needle = topic.Trim();
    return items.Where(m =>
        m.Type.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || m.Key.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || m.Value.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || (m.DisplayText?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
      .ToList();
  }

  public async Task<AiMemoryDto> RememberAsync(Guid personId, string type, string key, string value, string? displayText, string? source, CancellationToken ct = default)
  {
    type = (type ?? string.Empty).Trim().ToLowerInvariant();
    key = (key ?? string.Empty).Trim();
    value = (value ?? string.Empty).Trim();
    if (!AiMemoryTypes.All.Contains(type))
      throw new DomainValidationException($"type must be one of: {string.Join(", ", AiMemoryTypes.All)}.");
    if (key.Length is 0 or > MaxKeyLength)
      throw new DomainValidationException($"key must be 1 to {MaxKeyLength} characters.");
    if (value.Length is 0 or > MaxValueLength)
      throw new DomainValidationException($"value must be 1 to {MaxValueLength} characters.");
    if (displayText is { Length: > MaxDisplayLength })
      displayText = displayText[..MaxDisplayLength];

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var now = _clock.GetUtcNow().UtcDateTime;
    var existing = await context.AiMemories.FirstOrDefaultAsync(m => m.PersonId == personId && m.MemoryType == type && m.MemoryKey == key, ct);
    if (existing == null)
    {
      if (await context.AiMemories.CountAsync(m => m.PersonId == personId, ct) >= MaxItemsPerPerson)
        throw new DomainValidationException($"Memory is full ({MaxItemsPerPerson} items). The person can remove old items in the app.");
      existing = new AiMemory { Id = Guid.CreateVersion7(), PersonId = personId, MemoryType = type, MemoryKey = key, CreatedAt = now };
      context.AiMemories.Add(existing);
    }
    existing.ValueCipher = _encryption.Encrypt(value);
    existing.DisplayText = string.IsNullOrWhiteSpace(displayText) ? null : displayText.Trim();
    existing.Source = source is { Length: > 200 } ? source[..200] : source;
    existing.UpdatedAt = now;
    await context.SaveChangesAsync(ct);
    return ToDto(existing);
  }

  public async Task<bool> DeleteAsync(Guid personId, Guid id, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var row = await context.AiMemories.FirstOrDefaultAsync(m => m.Id == id && m.PersonId == personId, ct);
    if (row == null)
      return false;
    context.AiMemories.Remove(row);
    await context.SaveChangesAsync(ct);
    return true;
  }

  private AiMemoryDto ToDto(AiMemory m) => new(
    m.Id,
    m.MemoryType,
    m.MemoryKey,
    _encryption.TryDecrypt(m.ValueCipher) ?? string.Empty,
    m.DisplayText,
    m.Source,
    m.CreatedAt,
    m.UpdatedAt);
}
