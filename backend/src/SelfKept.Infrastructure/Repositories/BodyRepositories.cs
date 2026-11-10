using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Repositories;

// Body, goals and the day log. Each viewer read runs the one access check (ViewerAccess) for its
// category and answers null on a denial, exactly like the Collect repositories.

public sealed class GoalRepository : IGoalRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public GoalRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<IReadOnlyList<Goal>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, bool includeEnded, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Body, ct)) return null;
    return await _context.Goals.AsNoTracking()
      .Where(g => g.PersonId == targetPersonId && (includeEnded || g.Status == GoalStatuses.Active))
      .OrderByDescending(g => g.Status == GoalStatuses.Active)
      .ThenBy(g => g.Slot)
      .ThenByDescending(g => g.StartedOn)
      .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<Goal>> ListOwnActiveAsync(Guid personId, CancellationToken ct = default) =>
    await _context.Goals.AsNoTracking()
      .Where(g => g.PersonId == personId && g.Status == GoalStatuses.Active)
      .OrderBy(g => g.Slot)
      .ToListAsync(ct);

  public Task<Goal?> GetOwnAsync(Guid personId, Guid goalId, CancellationToken ct = default) =>
    _context.Goals.FirstOrDefaultAsync(g => g.Id == goalId && g.PersonId == personId, ct);

  public async Task<Goal> AddAsync(Goal goal, CancellationToken ct = default)
  {
    _context.Goals.Add(goal);
    await _context.SaveChangesAsync(ct);
    return goal;
  }

  public async Task UpdateAsync(Goal goal, CancellationToken ct = default)
  {
    _context.Goals.Update(goal);
    await _context.SaveChangesAsync(ct);
  }
}

public sealed class DailyTargetRepository : IDailyTargetRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public DailyTargetRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<IReadOnlyList<DailyTarget>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Body, ct)) return null;
    return await ListOwnAsync(targetPersonId, ct);
  }

  public async Task<IReadOnlyList<DailyTarget>> ListOwnAsync(Guid personId, CancellationToken ct = default) =>
    await _context.DailyTargets.AsNoTracking()
      .Where(t => t.PersonId == personId)
      .OrderBy(t => t.SortOrder)
      .ThenBy(t => t.CreatedAt)
      .ToListAsync(ct);

  public Task<DailyTarget?> GetOwnAsync(Guid personId, Guid targetId, CancellationToken ct = default) =>
    _context.DailyTargets.FirstOrDefaultAsync(t => t.Id == targetId && t.PersonId == personId, ct);

  public async Task<DailyTarget> AddAsync(DailyTarget target, CancellationToken ct = default)
  {
    _context.DailyTargets.Add(target);
    await _context.SaveChangesAsync(ct);
    return target;
  }

  public async Task UpdateAsync(DailyTarget target, CancellationToken ct = default)
  {
    _context.DailyTargets.Update(target);
    await _context.SaveChangesAsync(ct);
  }

  public async Task RemoveAsync(DailyTarget target, CancellationToken ct = default)
  {
    _context.DailyTargets.Remove(target);
    await _context.SaveChangesAsync(ct);
  }
}

public sealed class JournalRepository : IJournalRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public JournalRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<IReadOnlyList<JournalEntry>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly from, DateOnly to, int limit, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Checkins, ct)) return null;
    return await _context.JournalEntries.AsNoTracking()
      .Where(j => j.PersonId == targetPersonId && j.Day >= from && j.Day <= to)
      .OrderByDescending(j => j.At)
      .Take(limit)
      .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<JournalEntry>> ListOwnAsync(Guid personId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
    await _context.JournalEntries.AsNoTracking()
      .Where(j => j.PersonId == personId && j.Day >= from && j.Day <= to)
      .OrderBy(j => j.At)
      .ToListAsync(ct);

  public Task<JournalEntry?> GetOwnAsync(Guid personId, Guid entryId, CancellationToken ct = default) =>
    _context.JournalEntries.FirstOrDefaultAsync(j => j.Id == entryId && j.PersonId == personId, ct);

  public async Task<IReadOnlyList<JournalEntry>> ListOwnByMessageAsync(Guid personId, Guid messageId, CancellationToken ct = default) =>
    await _context.JournalEntries
      .Where(j => j.PersonId == personId && j.MessageId == messageId)
      .ToListAsync(ct);

  public async Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken ct = default)
  {
    _context.JournalEntries.Add(entry);
    await _context.SaveChangesAsync(ct);
    return entry;
  }

  public async Task RemoveAsync(JournalEntry entry, CancellationToken ct = default)
  {
    _context.JournalEntries.Remove(entry);
    await _context.SaveChangesAsync(ct);
  }
}

public sealed class MedicationRepository : IMedicationRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public MedicationRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<IReadOnlyList<Medication>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Labs, ct)) return null;
    return await ListOwnAsync(targetPersonId, ct);
  }

  public async Task<IReadOnlyList<Medication>> ListOwnAsync(Guid personId, CancellationToken ct = default) =>
    await _context.Medications.AsNoTracking()
      .Where(m => m.PersonId == personId)
      .OrderBy(m => m.StoppedOn != null)
      .ThenByDescending(m => m.CreatedAt)
      .ToListAsync(ct);

  public Task<Medication?> GetOwnAsync(Guid personId, Guid medicationId, CancellationToken ct = default) =>
    _context.Medications.FirstOrDefaultAsync(m => m.Id == medicationId && m.PersonId == personId, ct);

  public async Task<Medication> AddAsync(Medication medication, CancellationToken ct = default)
  {
    _context.Medications.Add(medication);
    await _context.SaveChangesAsync(ct);
    return medication;
  }

  public async Task UpdateAsync(Medication medication, CancellationToken ct = default)
  {
    _context.Medications.Update(medication);
    await _context.SaveChangesAsync(ct);
  }

  public async Task RemoveAsync(Medication medication, CancellationToken ct = default)
  {
    _context.Medications.Remove(medication);
    await _context.SaveChangesAsync(ct);
  }
}

public sealed class StrengthRepository : IStrengthRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public StrengthRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<IReadOnlyList<StrengthSet>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly from, DateOnly to, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Activity, ct)) return null;
    return await ListOwnAsync(targetPersonId, from, to, ct);
  }

  public async Task<IReadOnlyList<StrengthSet>> ListOwnAsync(Guid personId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
    await _context.StrengthSets.AsNoTracking()
      .Where(s => s.PersonId == personId && s.Day >= from && s.Day <= to)
      .OrderBy(s => s.Day)
      .ThenBy(s => s.Lift)
      .ToListAsync(ct);

  public Task<StrengthSet?> GetOwnAsync(Guid personId, Guid setId, CancellationToken ct = default) =>
    _context.StrengthSets.FirstOrDefaultAsync(s => s.Id == setId && s.PersonId == personId, ct);

  public Task<StrengthSet?> FindOwnAsync(Guid personId, DateOnly day, string lift, CancellationToken ct = default) =>
    _context.StrengthSets.FirstOrDefaultAsync(s => s.PersonId == personId && s.Day == day && s.Lift == lift, ct);

  public async Task<StrengthSet> AddAsync(StrengthSet set, CancellationToken ct = default)
  {
    _context.StrengthSets.Add(set);
    await _context.SaveChangesAsync(ct);
    return set;
  }

  public async Task UpdateAsync(StrengthSet set, CancellationToken ct = default)
  {
    _context.StrengthSets.Update(set);
    await _context.SaveChangesAsync(ct);
  }

  public async Task RemoveAsync(StrengthSet set, CancellationToken ct = default)
  {
    _context.StrengthSets.Remove(set);
    await _context.SaveChangesAsync(ct);
  }
}
