using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Services;

/// <summary>
/// The settings an admin saved from the app (table server_setting). Every value is encrypted with
/// <see cref="IEncryptionService"/>, secret or not, so nothing in the table is readable without the
/// server's key. Callers decide precedence; the environment always wins over a stored value.
/// </summary>
public sealed class ServerSettingStore
{
  private readonly IDbContextFactory<SelfKeptDbContext> _contexts;
  private readonly IEncryptionService _encryption;
  private readonly TimeProvider _clock;

  public ServerSettingStore(IDbContextFactory<SelfKeptDbContext> contexts, IEncryptionService encryption, TimeProvider clock)
  {
    _contexts = contexts;
    _encryption = encryption;
    _clock = clock;
  }

  /// <summary>The readable values among <paramref name="keys"/>. A row the key cannot decrypt is left out.</summary>
  public async Task<IReadOnlyDictionary<string, string>> GetAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
  {
    await using var context = await _contexts.CreateDbContextAsync(ct);
    var rows = await context.ServerSettings.AsNoTracking().Where(s => keys.Contains(s.Key)).ToListAsync(ct);
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var row in rows)
    {
      if (_encryption.TryDecrypt(row.Value) is { } value)
        values[row.Key] = value;
    }
    return values;
  }

  /// <summary>Writes every pair in one transaction, replacing what was there.</summary>
  public async Task SetAsync(IReadOnlyDictionary<string, string> values, Guid updatedBy, CancellationToken ct = default)
  {
    await using var context = await _contexts.CreateDbContextAsync(ct);
    var keys = values.Keys.ToList();
    var existing = await context.ServerSettings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
    var now = _clock.GetUtcNow().UtcDateTime;
    foreach (var (key, value) in values)
    {
      if (!existing.TryGetValue(key, out var row))
      {
        row = new ServerSetting { Key = key };
        context.ServerSettings.Add(row);
      }
      row.Value = _encryption.Encrypt(value);
      row.UpdatedAt = now;
      row.UpdatedBy = updatedBy;
    }
    await context.SaveChangesAsync(ct);
  }

  /// <summary>Removes the keys. True when anything was there.</summary>
  public async Task<bool> RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
  {
    await using var context = await _contexts.CreateDbContextAsync(ct);
    var rows = await context.ServerSettings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);
    if (rows.Count == 0) return false;
    context.ServerSettings.RemoveRange(rows);
    await context.SaveChangesAsync(ct);
    return true;
  }
}
