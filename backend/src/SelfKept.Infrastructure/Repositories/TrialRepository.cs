using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Repositories;

/// <summary>Trials, read under the body grant.</summary>
public sealed class TrialRepository : ITrialRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public TrialRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<IReadOnlyList<Trial>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, bool includeEnded, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Body, ct)) return null;
    return await _context.Trials.AsNoTracking()
      .Where(t => t.PersonId == targetPersonId && (includeEnded || t.Status == TrialStatuses.Active))
      .OrderByDescending(t => t.StartedOn).ThenByDescending(t => t.CreatedAt)
      .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<Trial>> ListOwnActiveAsync(Guid personId, CancellationToken ct = default) =>
    await _context.Trials.AsNoTracking()
      .Where(t => t.PersonId == personId && t.Status == TrialStatuses.Active)
      .OrderBy(t => t.StartedOn).ThenBy(t => t.CreatedAt)
      .ToListAsync(ct);

  public Task<Trial?> GetOwnAsync(Guid personId, Guid trialId, CancellationToken ct = default) =>
    _context.Trials.FirstOrDefaultAsync(t => t.Id == trialId && t.PersonId == personId, ct);

  public async Task<Trial> AddAsync(Trial trial, CancellationToken ct = default)
  {
    _context.Trials.Add(trial);
    await _context.SaveChangesAsync(ct);
    return trial;
  }

  public async Task UpdateAsync(Trial trial, CancellationToken ct = default)
  {
    _context.Trials.Update(trial);
    await _context.SaveChangesAsync(ct);
  }

  public async Task RemoveAsync(Trial trial, CancellationToken ct = default)
  {
    _context.Trials.Remove(trial);
    await _context.SaveChangesAsync(ct);
  }
}
