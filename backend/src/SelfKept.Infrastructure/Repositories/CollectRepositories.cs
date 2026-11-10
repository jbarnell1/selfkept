using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Repositories;

/// <summary>
/// Shared by every Collect repository: the one access check. A viewer always sees their own
/// rows; anyone else needs a grant for the category. Null from a read means "not for you" and
/// the controller answers 404 so the existence of data is not leaked.
/// </summary>
internal static class ViewerAccess
{
  public static async Task<bool> AllowedAsync(IAccessPolicy policy, Guid viewerPersonId, Guid targetPersonId, ShareCategory category, CancellationToken ct) =>
    viewerPersonId == targetPersonId || await policy.CanViewAsync(viewerPersonId, targetPersonId, category, ct);
}

public sealed class SourceRepository : ISourceRepository
{
  private readonly SelfKeptDbContext _context;

  public SourceRepository(SelfKeptDbContext context)
  {
    _context = context;
  }

  public async Task<IReadOnlyList<Source>> ListAsync(Guid personId, CancellationToken ct = default) =>
    await _context.Sources.AsNoTracking()
      .Where(s => s.PersonId == personId && s.DeletedAt == null)
      .OrderBy(s => s.CreatedAt)
      .ToListAsync(ct);

  public Task<Source?> GetAsync(Guid personId, Guid sourceId, CancellationToken ct = default) =>
    _context.Sources.FirstOrDefaultAsync(s => s.Id == sourceId && s.PersonId == personId && s.DeletedAt == null, ct);

  public Task<Source?> FindActiveAsync(Guid personId, string kind, string deviceName, string platform, CancellationToken ct = default) =>
    _context.Sources.FirstOrDefaultAsync(
      s => s.PersonId == personId && s.Kind == kind && s.DeviceName == deviceName && s.Platform == platform && s.DeletedAt == null, ct);

  public async Task<Source> AddAsync(Source source, CancellationToken ct = default)
  {
    _context.Sources.Add(source);
    await _context.SaveChangesAsync(ct);
    return source;
  }

  public async Task UpdateAsync(Source source, CancellationToken ct = default)
  {
    _context.Sources.Update(source);
    await _context.SaveChangesAsync(ct);
  }
}

public sealed class SampleRepository : ISampleRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;
  private readonly IMetricCatalogue _catalogue;

  public SampleRepository(SelfKeptDbContext context, IAccessPolicy policy, IMetricCatalogue catalogue)
  {
    _context = context;
    _policy = policy;
    _catalogue = catalogue;
  }

  public async Task EnsurePartitionsAsync(IEnumerable<DateOnly> months, CancellationToken ct = default)
  {
    if (!_context.Database.IsRelational()) return;
    foreach (var month in months.Select(m => new DateOnly(m.Year, m.Month, 1)).Distinct())
    {
      await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT ensure_sample_partition({month})", ct);
    }
  }

  public async Task<BulkInsertResult> InsertManyAsync(IReadOnlyList<Sample> samples, CancellationToken ct = default)
  {
    if (samples.Count == 0) return new BulkInsertResult(0, 0);

    if (!_context.Database.IsRelational())
    {
      return await InsertManyTrackedAsync(samples, ct);
    }

    // One round trip for the whole batch: unnest the columns and let the unique index drop the
    // readings this source already sent. A dense reading whose source already has a folded minute
    // there is a resend of a reading retention folded (the unique index no longer sees it), so it
    // is left out too. Rows affected = genuinely new samples.
    const string sql = """
      INSERT INTO sample (id, person_id, source_id, source_uid, metric_key, start_at, end_at, "value", unit, meta, created_at)
      SELECT t.id, t.person_id, t.source_id, t.source_uid, t.metric_key, t.start_at, t.end_at, t.value, t.unit, t.meta::jsonb, t.created_at
      FROM unnest(@ids, @person_ids, @source_ids, @source_uids, @metric_keys, @start_ats, @end_ats, @values, @units, @metas, @created_ats)
        AS t(id, person_id, source_id, source_uid, metric_key, start_at, end_at, "value", unit, meta, created_at)
      WHERE t.metric_key <> ALL (@dense_keys)
        OR NOT EXISTS (
          SELECT 1 FROM sample_minute m
          WHERE m.person_id = t.person_id AND m.metric_key = t.metric_key
            AND m.minute_at = date_trunc('minute', t.start_at, 'UTC') AND m.source_id = t.source_id)
      ON CONFLICT (source_id, source_uid, start_at) DO NOTHING
      """;

    var accepted = await _context.Database.ExecuteSqlRawAsync(sql, new object[]
    {
      new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = samples.Select(s => s.Id).ToArray() },
      new NpgsqlParameter("person_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = samples.Select(s => s.PersonId).ToArray() },
      new NpgsqlParameter("source_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = samples.Select(s => s.SourceId).ToArray() },
      new NpgsqlParameter("source_uids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = samples.Select(s => s.SourceUid).ToArray() },
      new NpgsqlParameter("metric_keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = samples.Select(s => s.MetricKey).ToArray() },
      new NpgsqlParameter("start_ats", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = samples.Select(s => s.StartAt).ToArray() },
      new NpgsqlParameter("end_ats", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = samples.Select(s => s.EndAt).ToArray() },
      new NpgsqlParameter("values", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = samples.Select(s => s.Value).ToArray() },
      new NpgsqlParameter("units", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = samples.Select(s => s.Unit).ToArray() },
      new NpgsqlParameter("metas", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = samples.Select(s => s.Meta).ToArray() },
      new NpgsqlParameter("created_ats", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = samples.Select(s => s.CreatedAt).ToArray() },
      new NpgsqlParameter("dense_keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = DenseMetrics.Keys.ToArray() },
    }, ct);

    return new BulkInsertResult(accepted, samples.Count - accepted);
  }

  // The InMemory provider has no ON CONFLICT: look the keys up first.
  private async Task<BulkInsertResult> InsertManyTrackedAsync(IReadOnlyList<Sample> samples, CancellationToken ct)
  {
    var sourceIds = samples.Select(s => s.SourceId).Distinct().ToList();
    var uids = samples.Select(s => s.SourceUid).Distinct().ToList();
    var existing = (await _context.Samples.AsNoTracking()
        .Where(s => sourceIds.Contains(s.SourceId) && uids.Contains(s.SourceUid))
        .Select(s => new { s.SourceId, s.SourceUid })
        .ToListAsync(ct))
      .Select(x => (x.SourceId, x.SourceUid))
      .ToHashSet();

    var folded = new HashSet<(Guid, string, DateTime)>();
    var dense = samples.Where(s => DenseMetrics.Contains(s.MetricKey)).ToList();
    if (dense.Count > 0)
    {
      var personIds = dense.Select(s => s.PersonId).Distinct().ToList();
      var minutes = dense.Select(s => SampleMinutes.MinuteOf(s.StartAt)).Distinct().ToList();
      folded = (await _context.SampleMinutes.AsNoTracking()
          .Where(m => personIds.Contains(m.PersonId) && sourceIds.Contains(m.SourceId) && minutes.Contains(m.MinuteAt))
          .Select(m => new { m.SourceId, m.MetricKey, m.MinuteAt })
          .ToListAsync(ct))
        .Select(m => (m.SourceId, m.MetricKey, m.MinuteAt))
        .ToHashSet();
    }

    var fresh = samples
      .Where(s => !existing.Contains((s.SourceId, s.SourceUid)))
      .Where(s => !DenseMetrics.Contains(s.MetricKey) || !folded.Contains((s.SourceId, s.MetricKey, SampleMinutes.MinuteOf(s.StartAt))))
      .ToList();
    _context.Samples.AddRange(fresh);
    await _context.SaveChangesAsync(ct);
    return new BulkInsertResult(fresh.Count, samples.Count - fresh.Count);
  }

  public async Task<int> DeleteBySourceUidAsync(Guid sourceId, string sourceUid, CancellationToken ct = default)
  {
    var query = _context.Samples.Where(s => s.SourceId == sourceId && s.SourceUid == sourceUid);
    if (_context.Database.IsRelational())
    {
      return await query.ExecuteDeleteAsync(ct);
    }
    var rows = await query.ToListAsync(ct);
    _context.Samples.RemoveRange(rows);
    await _context.SaveChangesAsync(ct);
    return rows.Count;
  }

  public async Task<IReadOnlyList<Sample>> GetOwnInWindowAsync(Guid personId, string metricKey, DateTime fromUtc, DateTime toUtc, bool byEnd, CancellationToken ct = default)
  {
    var query = _context.Samples.AsNoTracking().Where(s => s.PersonId == personId && s.MetricKey == metricKey);
    if (byEnd)
    {
      // Bound start_at too so the (person, metric, start_at) index is used; no sleep segment spans two days.
      var earliestStart = fromUtc.AddDays(-2);
      query = query.Where(s => s.EndAt != null && s.EndAt >= fromUtc && s.EndAt < toUtc && s.StartAt >= earliestStart);
    }
    else
    {
      query = query.Where(s => s.StartAt >= fromUtc && s.StartAt < toUtc);
    }
    return await query.OrderBy(s => s.StartAt).ToListAsync(ct);
  }

  public async Task<IReadOnlyList<SampleMinute>> GetOwnMinutesInWindowAsync(Guid personId, string metricKey, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
    await _context.SampleMinutes.AsNoTracking()
      .Where(m => m.PersonId == personId && m.MetricKey == metricKey && m.MinuteAt >= fromUtc && m.MinuteAt < toUtc)
      .OrderBy(m => m.MinuteAt)
      .ToListAsync(ct);

  public async Task<SamplePage?> GetPageAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, DateTime? fromUtc, DateTime? toUtc, int limit, SampleCursor? after, CancellationToken ct = default)
  {
    var metric = _catalogue.Find(metricKey);
    if (metric == null) return null;
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, metric.Category, ct)) return null;

    var query = _context.Samples.AsNoTracking().Where(s => s.PersonId == targetPersonId && s.MetricKey == metric.Key);
    if (fromUtc is { } from) query = query.Where(s => s.StartAt >= from);
    if (toUtc is { } to) query = query.Where(s => s.StartAt < to);
    if (after is { } cursor)
    {
      // Keyset on (start_at desc, id desc): everything strictly after the last row of the previous page.
      query = query.Where(s => s.StartAt < cursor.StartAt || (s.StartAt == cursor.StartAt && s.Id.CompareTo(cursor.Id) < 0));
    }

    var rows = await query
      .OrderByDescending(s => s.StartAt).ThenByDescending(s => s.Id)
      .Take(limit + 1)
      .ToListAsync(ct);

    if (DenseMetrics.Contains(metric.Key))
    {
      // Folded minutes page with the same keyset. The two lists are merged in the database's uuid
      // order (the hex text), so a cursor taken from either kind of row stays valid.
      var minutes = _context.SampleMinutes.AsNoTracking().Where(m => m.PersonId == targetPersonId && m.MetricKey == metric.Key);
      if (fromUtc is { } minuteFrom) minutes = minutes.Where(m => m.MinuteAt >= minuteFrom);
      if (toUtc is { } minuteTo) minutes = minutes.Where(m => m.MinuteAt < minuteTo);
      if (after is { } minuteCursor)
      {
        minutes = minutes.Where(m => m.MinuteAt < minuteCursor.StartAt || (m.MinuteAt == minuteCursor.StartAt && m.Id.CompareTo(minuteCursor.Id) < 0));
      }
      var folded = await minutes
        .OrderByDescending(m => m.MinuteAt).ThenByDescending(m => m.Id)
        .Take(limit + 1)
        .ToListAsync(ct);
      if (folded.Count > 0)
      {
        rows = rows.Concat(folded.Select(m => SampleMinutes.AsSample(m, metric.Unit)))
          .OrderByDescending(s => s.StartAt).ThenByDescending(s => s.Id.ToString("N"), StringComparer.Ordinal)
          .Take(limit + 1)
          .ToList();
      }
    }

    SampleCursor? next = null;
    if (rows.Count > limit)
    {
      rows.RemoveAt(rows.Count - 1);
      var last = rows[^1];
      next = new SampleCursor(last.StartAt, last.Id);
    }
    return new SamplePage(rows, next);
  }

  public async Task<IReadOnlyList<MetricOverview>?> GetOverviewAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var allowed = new HashSet<ShareCategory>();
    foreach (var category in Enum.GetValues<ShareCategory>())
    {
      if (await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, category, ct)) allowed.Add(category);
    }
    if (allowed.Count == 0) return null;

    var raw = (await _context.Samples.AsNoTracking()
        .Where(s => s.PersonId == targetPersonId)
        .GroupBy(s => s.MetricKey)
        .Select(g => new { MetricKey = g.Key, FirstAt = g.Min(s => s.StartAt), LastAt = g.Max(s => s.StartAt), Count = g.LongCount() })
        .ToListAsync(ct))
      .ToDictionary(g => g.MetricKey, g => (g.FirstAt, g.LastAt, g.Count), StringComparer.Ordinal);
    // Folded minutes count as the readings they stand for, so history does not seem to start at the window.
    var folded = (await _context.SampleMinutes.AsNoTracking()
        .Where(m => m.PersonId == targetPersonId)
        .GroupBy(m => m.MetricKey)
        .Select(g => new { MetricKey = g.Key, FirstAt = g.Min(m => m.MinuteAt), LastAt = g.Max(m => m.MinuteAt), Count = g.Sum(m => (long)m.Count) })
        .ToListAsync(ct))
      .ToDictionary(g => g.MetricKey, g => (g.FirstAt, g.LastAt, g.Count), StringComparer.Ordinal);

    var result = new List<MetricOverview>();
    foreach (var key in raw.Keys.Union(folded.Keys).OrderBy(k => k, StringComparer.Ordinal))
    {
      var metric = _catalogue.Find(key);
      if (metric == null || !allowed.Contains(metric.Category)) continue;
      var hasRaw = raw.TryGetValue(key, out var r);
      var hasFolded = folded.TryGetValue(key, out var f);
      var firstAt = !hasFolded || (hasRaw && r.FirstAt <= f.FirstAt) ? r.FirstAt : f.FirstAt;
      var lastAt = !hasFolded || (hasRaw && r.LastAt >= f.LastAt) ? r.LastAt : f.LastAt;
      var lastValue = hasRaw && r.LastAt == lastAt
        ? await _context.Samples.AsNoTracking()
            .Where(s => s.PersonId == targetPersonId && s.MetricKey == key)
            .OrderByDescending(s => s.StartAt).ThenByDescending(s => s.Id)
            .Select(s => s.Value)
            .FirstAsync(ct)
        : await _context.SampleMinutes.AsNoTracking()
            .Where(m => m.PersonId == targetPersonId && m.MetricKey == key)
            .OrderByDescending(m => m.MinuteAt)
            .Select(m => m.Avg)
            .FirstAsync(ct);
      result.Add(new MetricOverview(key, firstAt, lastAt, (hasRaw ? r.Count : 0) + (hasFolded ? f.Count : 0), lastValue));
    }
    return result;
  }
}

public sealed class WorkoutRepository : IWorkoutRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public WorkoutRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<BulkInsertResult> InsertManyAsync(IReadOnlyList<Workout> workouts, CancellationToken ct = default)
  {
    if (workouts.Count == 0) return new BulkInsertResult(0, 0);
    var sourceIds = workouts.Select(w => w.SourceId).Distinct().ToList();
    var uids = workouts.Select(w => w.SourceUid).Distinct().ToList();
    var existing = (await _context.Workouts.AsNoTracking()
        .Where(w => sourceIds.Contains(w.SourceId) && uids.Contains(w.SourceUid))
        .Select(w => new { w.SourceId, w.SourceUid })
        .ToListAsync(ct))
      .Select(x => (x.SourceId, x.SourceUid))
      .ToHashSet();

    var fresh = workouts.Where(w => !existing.Contains((w.SourceId, w.SourceUid))).ToList();
    _context.Workouts.AddRange(fresh);
    await _context.SaveChangesAsync(ct);
    return new BulkInsertResult(fresh.Count, workouts.Count - fresh.Count);
  }

  public async Task<IReadOnlyList<Workout>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Activity, ct)) return null;
    return await _context.Workouts.AsNoTracking()
      .Where(w => w.PersonId == targetPersonId && w.StartAt >= fromUtc && w.StartAt < toUtc)
      .OrderByDescending(w => w.StartAt)
      .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<Workout>> ListOwnAsync(Guid personId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
    await _context.Workouts.AsNoTracking()
      .Where(w => w.PersonId == personId && w.StartAt >= fromUtc && w.StartAt < toUtc)
      .OrderByDescending(w => w.StartAt)
      .ToListAsync(ct);
}

public sealed class CheckinRepository : ICheckinRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public CheckinRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<Checkin> AddAsync(Checkin checkin, CancellationToken ct = default)
  {
    _context.Checkins.Add(checkin);
    await _context.SaveChangesAsync(ct);
    return checkin;
  }

  public Task<Checkin?> GetOwnAsync(Guid personId, Guid checkinId, CancellationToken ct = default) =>
    _context.Checkins.FirstOrDefaultAsync(c => c.Id == checkinId && c.PersonId == personId, ct);

  public async Task<IReadOnlyList<Checkin>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Checkins, ct)) return null;
    return await _context.Checkins.AsNoTracking()
      .Where(c => c.PersonId == targetPersonId && c.At >= fromUtc && c.At < toUtc)
      .OrderByDescending(c => c.At)
      .Take(limit)
      .ToListAsync(ct);
  }

  public async Task RemoveAsync(Checkin checkin, CancellationToken ct = default)
  {
    _context.Checkins.Remove(checkin);
    await _context.SaveChangesAsync(ct);
  }
}

public sealed class DailySummaryRepository : IDailySummaryRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;
  private readonly IMetricCatalogue _catalogue;

  public DailySummaryRepository(SelfKeptDbContext context, IAccessPolicy policy, IMetricCatalogue catalogue)
  {
    _context = context;
    _policy = policy;
    _catalogue = catalogue;
  }

  public async Task UpsertManyAsync(IReadOnlyList<DailySummary> rows, CancellationToken ct = default)
  {
    foreach (var row in rows)
    {
      var existing = await _context.DailySummaries.FindAsync([row.PersonId, row.MetricKey, row.Day], ct);
      if (existing == null)
      {
        _context.DailySummaries.Add(row);
      }
      else
      {
        existing.Value = row.Value;
        existing.Min = row.Min;
        existing.Max = row.Max;
        existing.Count = row.Count;
        existing.ComputedAt = row.ComputedAt;
      }
    }
    await _context.SaveChangesAsync(ct);
  }

  public async Task DeleteAsync(Guid personId, string metricKey, DateOnly day, CancellationToken ct = default)
  {
    var existing = await _context.DailySummaries.FindAsync([personId, metricKey, day], ct);
    if (existing == null) return;
    _context.DailySummaries.Remove(existing);
    await _context.SaveChangesAsync(ct);
  }

  public async Task<IReadOnlyList<DailySummary>?> GetRangeAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, DateOnly from, DateOnly to, CancellationToken ct = default)
  {
    var metric = _catalogue.Find(metricKey);
    if (metric == null) return null;
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, metric.Category, ct)) return null;

    return await _context.DailySummaries.AsNoTracking()
      .Where(d => d.PersonId == targetPersonId && d.MetricKey == metric.Key && d.Day >= from && d.Day <= to)
      .OrderBy(d => d.Day)
      .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<DailySummary>> GetOwnRangeAsync(Guid personId, IReadOnlyCollection<string> metricKeys, DateOnly from, DateOnly to, CancellationToken ct = default)
  {
    var keys = metricKeys.ToList();
    return await _context.DailySummaries.AsNoTracking()
      .Where(d => d.PersonId == personId && keys.Contains(d.MetricKey) && d.Day >= from && d.Day <= to)
      .OrderBy(d => d.MetricKey).ThenBy(d => d.Day)
      .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<DailySummary>?> GetRangeForInsightsAsync(Guid viewerPersonId, Guid targetPersonId, IReadOnlyCollection<string> metricKeys, DateOnly from, DateOnly to, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Insights, ct)) return null;
    return await GetOwnRangeAsync(targetPersonId, metricKeys, from, to, ct);
  }
}
