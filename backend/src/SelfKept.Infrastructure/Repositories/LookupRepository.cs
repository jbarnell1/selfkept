using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Repositories;

/// <summary>
/// The cloud lookup switch and the lookups sent. Own-only: every read is filtered on the caller's
/// own person id and there is no share category, so another household member never reaches
/// these rows, whatever they have been granted.
/// </summary>
public sealed class LookupRepository : ILookupRepository
{
  private readonly SelfKeptDbContext _context;

  public LookupRepository(SelfKeptDbContext context) => _context = context;

  public Task<LookupSetting?> GetOwnSettingAsync(Guid personId, CancellationToken ct = default) =>
    _context.LookupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.PersonId == personId, ct);

  public async Task SaveOwnSettingAsync(LookupSetting setting, CancellationToken ct = default)
  {
    var existing = await _context.LookupSettings.FirstOrDefaultAsync(s => s.PersonId == setting.PersonId, ct);
    if (existing == null)
    {
      _context.LookupSettings.Add(setting);
    }
    else
    {
      existing.Enabled = setting.Enabled;
      existing.EnabledAt = setting.EnabledAt;
      existing.UpdatedAt = setting.UpdatedAt;
    }
    await _context.SaveChangesAsync(ct);
  }

  public async Task<AssistantLookup> AddOwnAsync(AssistantLookup lookup, CancellationToken ct = default)
  {
    _context.AssistantLookups.Add(lookup);
    await _context.SaveChangesAsync(ct);
    return lookup;
  }

  public Task<AssistantLookup?> GetOwnAsync(Guid personId, Guid lookupId, CancellationToken ct = default) =>
    _context.AssistantLookups.FirstOrDefaultAsync(l => l.Id == lookupId && l.PersonId == personId, ct);

  public async Task<IReadOnlyList<AssistantLookup>> ListOwnAsync(Guid personId, int limit, CancellationToken ct = default) =>
    await _context.AssistantLookups.AsNoTracking()
      .Where(l => l.PersonId == personId)
      .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
      .Take(limit)
      .ToListAsync(ct);

  public async Task<IReadOnlyList<AssistantLookup>> ListOwnByIdsAsync(Guid personId, IReadOnlyCollection<Guid> lookupIds, CancellationToken ct = default)
  {
    if (lookupIds.Count == 0) return [];
    var ids = lookupIds.ToList();
    return await _context.AssistantLookups.AsNoTracking()
      .Where(l => l.PersonId == personId && ids.Contains(l.Id))
      .ToListAsync(ct);
  }

  public async Task UpdateOwnAsync(AssistantLookup lookup, CancellationToken ct = default)
  {
    if (_context.Entry(lookup).State == EntityState.Detached) _context.AssistantLookups.Update(lookup);
    await _context.SaveChangesAsync(ct);
  }

  public async Task<bool> RemoveOwnAsync(Guid personId, Guid lookupId, CancellationToken ct = default)
  {
    var lookup = await _context.AssistantLookups.FirstOrDefaultAsync(l => l.Id == lookupId && l.PersonId == personId, ct);
    if (lookup == null) return false;
    _context.AssistantLookups.Remove(lookup);
    await _context.SaveChangesAsync(ct);
    return true;
  }
}
