using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Repositories;

public sealed class BaselineRepository : IBaselineRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;
  private readonly IMetricCatalogue _catalogue;

  public BaselineRepository(SelfKeptDbContext context, IAccessPolicy policy, IMetricCatalogue catalogue)
  {
    _context = context;
    _policy = policy;
    _catalogue = catalogue;
  }

  public async Task UpsertManyAsync(IReadOnlyList<Baseline> rows, CancellationToken ct = default)
  {
    foreach (var row in rows)
    {
      var existing = await _context.Baselines.FindAsync([row.PersonId, row.MetricKey, row.ComputedFor], ct);
      if (existing == null)
      {
        _context.Baselines.Add(row);
      }
      else
      {
        existing.Median7 = row.Median7;
        existing.Median28 = row.Median28;
        existing.P10_28 = row.P10_28;
        existing.P25_28 = row.P25_28;
        existing.P75_28 = row.P75_28;
        existing.P90_28 = row.P90_28;
        existing.Slope28 = row.Slope28;
        existing.Sd28 = row.Sd28;
        existing.N7 = row.N7;
        existing.N28 = row.N28;
        existing.ComputedAt = row.ComputedAt;
      }
    }
    await _context.SaveChangesAsync(ct);
  }

  public async Task<IReadOnlyList<Baseline>> GetOwnForDayAsync(Guid personId, DateOnly computedFor, CancellationToken ct = default) =>
    await _context.Baselines.AsNoTracking()
      .Where(b => b.PersonId == personId && b.ComputedFor == computedFor)
      .OrderBy(b => b.MetricKey)
      .ToListAsync(ct);

  public Task<bool> AnyForDayAsync(Guid personId, DateOnly computedFor, CancellationToken ct = default) =>
    _context.Baselines.AsNoTracking().AnyAsync(b => b.PersonId == personId && b.ComputedFor == computedFor, ct);

  public async Task<Baseline?> GetLatestAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, CancellationToken ct = default)
  {
    var metric = _catalogue.Find(metricKey);
    if (metric == null) return null;
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, metric.Category, ct)) return null;
    return await _context.Baselines.AsNoTracking()
      .Where(b => b.PersonId == targetPersonId && b.MetricKey == metric.Key)
      .OrderByDescending(b => b.ComputedFor)
      .FirstOrDefaultAsync(ct);
  }

  public async Task<IReadOnlyList<Baseline>?> GetLatestPerMetricAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var allowed = new HashSet<ShareCategory>();
    foreach (var category in Enum.GetValues<ShareCategory>())
    {
      if (await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, category, ct)) allowed.Add(category);
    }
    if (allowed.Count == 0) return null;

    var latest = await LatestPerMetricAsync(targetPersonId, ct);
    return latest.Where(b => _catalogue.Find(b.MetricKey) is { } m && allowed.Contains(m.Category)).ToList();
  }

  public async Task<IReadOnlyList<Baseline>?> GetLatestPerMetricForInsightsAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Insights, ct)) return null;
    return await LatestPerMetricAsync(targetPersonId, ct);
  }

  // Baselines are computed for every metric together, so the newest computed_for day holds the
  // newest row of each metric: one query for the day, one for its rows.
  private async Task<List<Baseline>> LatestPerMetricAsync(Guid personId, CancellationToken ct)
  {
    var latestDay = await _context.Baselines.AsNoTracking()
      .Where(b => b.PersonId == personId)
      .OrderByDescending(b => b.ComputedFor)
      .Select(b => (DateOnly?)b.ComputedFor)
      .FirstOrDefaultAsync(ct);
    if (latestDay == null) return [];
    return await _context.Baselines.AsNoTracking()
      .Where(b => b.PersonId == personId && b.ComputedFor == latestDay.Value)
      .OrderBy(b => b.MetricKey)
      .ToListAsync(ct);
  }
}

public sealed class InsightRepository : IInsightRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public InsightRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<Insight> AddAsync(Insight insight, CancellationToken ct = default)
  {
    _context.Insights.Add(insight);
    await _context.SaveChangesAsync(ct);
    return insight;
  }

  public async Task UpdateAsync(Insight insight, CancellationToken ct = default)
  {
    // Rows read with AsNoTracking are detached; if the same row is already tracked (a rerun in
    // the same scope), copy the values onto it instead of attaching a second instance.
    var tracked = _context.ChangeTracker.Entries<Insight>().FirstOrDefault(e => e.Entity.Id == insight.Id);
    if (tracked != null && !ReferenceEquals(tracked.Entity, insight))
    {
      tracked.CurrentValues.SetValues(insight);
    }
    else
    {
      _context.Insights.Update(insight);
    }
    await _context.SaveChangesAsync(ct);
  }

  public Task<Insight?> GetOwnByKindAsync(Guid personId, string kind, DateOnly day, CancellationToken ct = default) =>
    _context.Insights.FirstOrDefaultAsync(i => i.PersonId == personId && i.Kind == kind && i.Day == day, ct);

  public Task<Insight?> GetOwnAsync(Guid personId, Guid insightId, CancellationToken ct = default) =>
    _context.Insights.FirstOrDefaultAsync(i => i.Id == insightId && i.PersonId == personId, ct);

  public Task<bool> AnyForDayAsync(Guid personId, DateOnly day, CancellationToken ct = default) =>
    _context.Insights.AsNoTracking().AnyAsync(i => i.PersonId == personId && i.Day == day, ct);

  public Task<int> CountReactionsAsync(Guid personId, string kind, string reaction, DateOnly sinceDay, CancellationToken ct = default) =>
    _context.Insights.AsNoTracking().CountAsync(i => i.PersonId == personId && i.Kind == kind && i.Reaction == reaction && i.Day >= sinceDay, ct);

  public async Task<IReadOnlyList<Insight>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly from, DateOnly to, bool includeDismissed, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Insights, ct)) return null;
    var query = _context.Insights.AsNoTracking().Where(i => i.PersonId == targetPersonId && i.Day >= from && i.Day <= to);
    if (!includeDismissed) query = query.Where(i => i.DismissedAt == null);
    return await query.OrderByDescending(i => i.Day).ThenByDescending(i => i.CreatedAt).ToListAsync(ct);
  }
}

/// <summary>Like the audit log: its own short-lived context, and a failed write is logged, never thrown.</summary>
public sealed class AiCallRepository : IAiCallRepository
{
  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly ILogger<AiCallRepository> _logger;

  public AiCallRepository(IDbContextFactory<SelfKeptDbContext> contextFactory, ILogger<AiCallRepository> logger)
  {
    _contextFactory = contextFactory;
    _logger = logger;
  }

  public async Task RecordAsync(AiCall call, CancellationToken ct = default)
  {
    try
    {
      await using var context = await _contextFactory.CreateDbContextAsync(ct);
      context.AiCalls.Add(call);
      await context.SaveChangesAsync(ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "ai_call write failed for {Purpose}", call.Purpose);
    }
  }
}

public sealed class TodayCacheRepository : ITodayCacheRepository
{
  private readonly SelfKeptDbContext _context;

  public TodayCacheRepository(SelfKeptDbContext context)
  {
    _context = context;
  }

  public Task<TodayCache?> GetAsync(Guid personId, CancellationToken ct = default) =>
    _context.TodayCaches.AsNoTracking().FirstOrDefaultAsync(c => c.PersonId == personId, ct);

  public async Task SetAsync(TodayCache cache, CancellationToken ct = default)
  {
    var existing = await _context.TodayCaches.FindAsync([cache.PersonId], ct);
    if (existing == null)
    {
      _context.TodayCaches.Add(cache);
    }
    else
    {
      existing.ComputedAt = cache.ComputedAt;
      existing.Sentence = cache.Sentence;
      existing.Payload = cache.Payload;
    }
    await _context.SaveChangesAsync(ct);
  }

  public async Task InvalidateAsync(Guid personId, CancellationToken ct = default)
  {
    var existing = await _context.TodayCaches.FindAsync([personId], ct);
    if (existing == null) return;
    _context.TodayCaches.Remove(existing);
    await _context.SaveChangesAsync(ct);
  }
}
