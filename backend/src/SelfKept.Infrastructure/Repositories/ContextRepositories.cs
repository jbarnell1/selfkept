using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Repositories;

// Context, patterns and read-backs (docs/context-design.md). Each viewer read runs the one access
// check (ViewerAccess) for its category and answers null on a denial.

public sealed class ContextEventRepository : IContextEventRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public ContextEventRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<IReadOnlyList<ContextEvent>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly from, DateOnly to, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Insights, ct)) return null;
    return await ListOwnAsync(targetPersonId, from, to, ct);
  }

  public async Task<IReadOnlyList<ContextEvent>> ListOwnAsync(Guid personId, DateOnly from, DateOnly to, CancellationToken ct = default)
  {
    // An open event counts for OpenEndedDays from its start, so it overlaps the range when it
    // started no more than that long before the range begins.
    var openFrom = from.AddDays(-(ContextKinds.OpenEndedDays - 1));
    return await _context.ContextEvents.AsNoTracking()
      .Where(e => e.PersonId == personId && e.StartedOn <= to
        && (e.EndedOn == null ? e.StartedOn >= openFrom : e.EndedOn >= from))
      .OrderByDescending(e => e.StartedOn)
      .ThenByDescending(e => e.CreatedAt)
      .ToListAsync(ct);
  }

  public Task<ContextEvent?> GetOwnAsync(Guid personId, Guid eventId, CancellationToken ct = default) =>
    _context.ContextEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.PersonId == personId, ct);

  public Task<int> CountOwnAsync(Guid personId, CancellationToken ct = default) =>
    _context.ContextEvents.AsNoTracking().CountAsync(e => e.PersonId == personId, ct);

  public async Task<ContextEvent> AddAsync(ContextEvent contextEvent, CancellationToken ct = default)
  {
    _context.ContextEvents.Add(contextEvent);
    await _context.SaveChangesAsync(ct);
    return contextEvent;
  }

  public async Task UpdateAsync(ContextEvent contextEvent, CancellationToken ct = default)
  {
    _context.ContextEvents.Update(contextEvent);
    await _context.SaveChangesAsync(ct);
  }

  public async Task RemoveAsync(ContextEvent contextEvent, CancellationToken ct = default)
  {
    _context.ContextEvents.Remove(contextEvent);
    await _context.SaveChangesAsync(ct);
  }
}

public sealed class ReadBackRepository : IReadBackRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public ReadBackRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<PatternInputs?> GetPatternInputsAsync(Guid viewerPersonId, Guid targetPersonId, IReadOnlyCollection<string> metricKeys, DateOnly from, DateOnly to, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Insights, ct)) return null;
    // Only the day and the tags of each entry are read, never the text.
    var entries = await _context.JournalEntries.AsNoTracking()
      .Where(j => j.PersonId == targetPersonId && j.Day >= from && j.Day <= to)
      .Select(j => new { j.Day, j.Tags })
      .ToListAsync(ct);
    var days = entries
      .GroupBy(e => e.Day)
      .OrderBy(g => g.Key)
      .Select(g => new LoggedDay(g.Key, g.SelectMany(e => e.Tags).Distinct(StringComparer.Ordinal).ToList()))
      .ToList();
    var keys = metricKeys.ToList();
    // Nights are the day after a logged day, so the figures run one day past the range.
    var last = to.AddDays(1);
    var nights = await _context.DailySummaries.AsNoTracking()
      .Where(d => d.PersonId == targetPersonId && keys.Contains(d.MetricKey) && d.Day >= from && d.Day <= last)
      .OrderBy(d => d.MetricKey).ThenBy(d => d.Day)
      .ToListAsync(ct);
    return new PatternInputs(days, nights);
  }

  public async Task<IReadOnlyList<Sample>?> GetSleepSamplesAsync(Guid viewerPersonId, Guid targetPersonId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Sleep, ct)) return null;
    return await _context.Samples.AsNoTracking()
      // By end, like the daily summary; start is bounded too so the (person, metric, start_at) index is used.
      .Where(s => s.PersonId == targetPersonId && s.MetricKey == MetricKeys.SleepStage
        && s.EndAt != null && s.EndAt >= fromUtc && s.EndAt < toUtc && s.StartAt >= fromUtc.AddDays(-2))
      .OrderBy(s => s.StartAt)
      .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<Workout>> ListOwnWorkoutsAsync(Guid personId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
    await _context.Workouts.AsNoTracking()
      .Where(w => w.PersonId == personId && w.StartAt >= fromUtc && w.StartAt < toUtc)
      .OrderBy(w => w.StartAt)
      .ToListAsync(ct);

  public async Task<IReadOnlySet<ShareCategory>> AllowedCategoriesAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var allowed = new HashSet<ShareCategory>();
    foreach (var category in Enum.GetValues<ShareCategory>())
    {
      if (await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, category, ct)) allowed.Add(category);
    }
    return allowed;
  }
}
