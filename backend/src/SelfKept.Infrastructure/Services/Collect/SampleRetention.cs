using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>How long raw readings are kept, as month arithmetic. Pure so it is tested without a database.</summary>
public static class RetentionWindow
{
  public const int DefaultMonths = 12;

  /// <summary>Four calendar months always cover the phone's 90-day first read, so a re-read never meets a folded month.</summary>
  public const int MinimumMonths = 4;

  public const int MaximumMonths = 600;

  /// <summary>0 keeps raw readings for ever; otherwise 4 to 600.</summary>
  public static bool IsAllowed(int months) => months == 0 || months is >= MinimumMonths and <= MaximumMonths;

  /// <summary>
  /// The first day kept raw: the first of the current UTC month, less the window. Every month
  /// before it is folded. Null when raw readings are kept for ever.
  /// </summary>
  public static DateOnly? RawSince(DateTime utcNow, int rawMonths)
  {
    if (rawMonths == 0) return null;
    var now = DayBoundaries.AsUtc(utcNow);
    return new DateOnly(now.Year, now.Month, 1).AddMonths(-rawMonths);
  }

  /// <summary>The partition that holds a month, the same name ensure_sample_partition gives it.</summary>
  public static string PartitionName(DateOnly month) => "sample_" + month.ToString("yyyyMM", CultureInfo.InvariantCulture);

  /// <summary>The month a partition holds, or null for a name that is not sample_YYYYMM.</summary>
  public static DateOnly? MonthOf(string partitionName) =>
    partitionName.Length == 13 && partitionName.StartsWith("sample_", StringComparison.Ordinal)
      && DateOnly.TryParseExact(partitionName[7..] + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
      ? month
      : null;

  /// <summary>yyyy-MM, how a month is named in the audit log and the API.</summary>
  public static string Label(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

/// <summary>When the nightly pass runs: once a day, from 04:00 in the zone of the server's first person.</summary>
public static class RetentionSchedule
{
  public static readonly TimeOnly RunAt = new(4, 0);

  /// <summary>Today's local date once it is past 04:00 local; null before that.</summary>
  public static DateOnly? DueDay(DateTime utcNow, TimeZoneInfo zone)
  {
    var local = TimeZoneInfo.ConvertTimeFromUtc(DayBoundaries.AsUtc(utcNow), zone);
    return TimeOnly.FromDateTime(local) < RunAt ? null : DateOnly.FromDateTime(local);
  }
}

/// <summary>
/// The retention window: Retention:RawMonths from the environment when set, otherwise the value
/// an admin saved from the app (server_setting, encrypted like every stored setting), else 12.
/// </summary>
public sealed class RetentionPolicy
{
  public const string ConfigurationKey = "Retention:RawMonths";
  public const string StoreKey = "retention.raw_months";

  private readonly int? _environment;
  private readonly ServerSettingStore _store;

  public RetentionPolicy(IConfiguration configuration, ServerSettingStore store)
  {
    _environment = FromConfiguration(configuration);
    _store = store;
  }

  /// <summary>Null when unset or blank. A value that is not 0 or 4 to 600 stops the run with a clear message.</summary>
  public static int? FromConfiguration(IConfiguration configuration)
  {
    var text = configuration[ConfigurationKey];
    if (string.IsNullOrWhiteSpace(text)) return null;
    if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var months) && RetentionWindow.IsAllowed(months))
      return months;
    throw new InvalidOperationException(
      $"{ConfigurationKey} is '{text}'. Use 0 to keep raw readings for ever, or a number of months from {RetentionWindow.MinimumMonths} to {RetentionWindow.MaximumMonths}.");
  }

  public async Task<(int Months, SettingSource Source)> ResolveAsync(CancellationToken ct = default)
  {
    if (_environment is { } fromEnvironment) return (fromEnvironment, SettingSource.Environment);
    var stored = await _store.GetAsync([StoreKey], ct);
    return stored.TryGetValue(StoreKey, out var text)
      && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var months)
      && RetentionWindow.IsAllowed(months)
      ? (months, SettingSource.App)
      : (RetentionWindow.DefaultMonths, SettingSource.Default);
  }

  public Task SaveAsync(int months, Guid updatedBy, CancellationToken ct = default) =>
    _store.SetAsync(new Dictionary<string, string> { [StoreKey] = months.ToString(CultureInfo.InvariantCulture) }, updatedBy, ct);
}

/// <summary>What one retention run did. <see cref="AlreadyRunning"/> when another run held the lock and this one did nothing.</summary>
public sealed record RetentionRunResult(DateOnly? RawSince, IReadOnlyList<RetentionFoldedDto> Folded, IReadOnlyList<DateOnly> Compacted, bool AlreadyRunning = false);

/// <summary>
/// Folds dense readings older than the window into one row per minute per source, then rewrites
/// each folded month's partition so the disk space comes back (docs/retention-design.md).
/// Idempotent and resumable: a fold is one statement in one transaction, a failed compaction is
/// retried next run, and a run with nothing to do changes nothing. Each folded month is audited.
/// </summary>
public sealed class SampleRetention
{
  /// <summary>A month of one person's heart rate is about 1.2 million rows; the fold may take a while.</summary>
  public static readonly TimeSpan StatementTimeout = TimeSpan.FromMinutes(30);

  /// <summary>How long compaction waits for the partition before leaving it to the next run.</summary>
  public const string CompactLockTimeout = "10s";

  private const string LockKey = "sample_retention";

  private readonly IDbContextFactory<SelfKeptDbContext> _contexts;
  private readonly RetentionPolicy _policy;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;
  private readonly ILogger<SampleRetention> _logger;

  public SampleRetention(IDbContextFactory<SelfKeptDbContext> contexts, RetentionPolicy policy, IAuditService audit, TimeProvider clock, ILogger<SampleRetention> logger)
  {
    _contexts = contexts;
    _policy = policy;
    _audit = audit;
    _clock = clock;
    _logger = logger;
  }

  /// <summary>One pass. The actor is the admin who asked, or null for the nightly job.</summary>
  public async Task<RetentionRunResult> RunAsync(Guid? actorUserId, Guid? householdId, RequestInfo? info, CancellationToken ct = default)
  {
    var now = _clock.GetUtcNow().UtcDateTime;
    var (months, _) = await _policy.ResolveAsync(ct);
    if (RetentionWindow.RawSince(now, months) is not { } rawSince) return new RetentionRunResult(null, [], []);

    await using var context = await _contexts.CreateDbContextAsync(ct);
    var result = context.Database.IsRelational()
      ? await RunOnPostgresAsync(context, rawSince, now, ct)
      : await RunInMemoryAsync(context, rawSince, now, ct);

    foreach (var month in result.Folded)
    {
      _logger.LogInformation("Retention folded {Month}: {RawRows} readings into {MinuteRows} minutes, compacted {Compacted}",
        RetentionWindow.Label(month.Month), month.RawRows, month.MinuteRows, month.Compacted);
      await _audit.RecordAsync(new AuditEvent(AuditActions.RetentionRollup, actorUserId, householdId, "sample_month", RetentionWindow.Label(month.Month),
        new { month = RetentionWindow.Label(month.Month), rawRows = month.RawRows, minuteRows = month.MinuteRows, compacted = month.Compacted }, info?.IpAddress), ct);
    }
    return result;
  }

  private async Task<RetentionRunResult> RunOnPostgresAsync(SelfKeptDbContext context, DateOnly rawSince, DateTime now, CancellationToken ct)
  {
    context.Database.SetCommandTimeout(StatementTimeout);
    // One connection for the whole run: the advisory lock is held by this session, and VACUUM
    // must run outside any transaction on it.
    await context.Database.OpenConnectionAsync(ct);
    try
    {
      var connection = context.Database.GetDbConnection();
      if (!await ScalarAsync<bool>(connection, null, "SELECT pg_try_advisory_lock(hashtext(@key))", ct, ("key", LockKey)))
        return new RetentionRunResult(rawSince, [], [], AlreadyRunning: true);
      try
      {
        var folded = new List<RetentionFoldedDto>();
        foreach (var month in await OldMonthsAsync(connection, rawSince, ct))
        {
          if (await FoldAsync(context, month, now, ct) is { } done) folded.Add(done);
        }

        var compacted = new List<DateOnly>();
        var pending = await context.SampleRollupMonths.AsNoTracking()
          .Where(m => m.CompactedAt == null && m.Month < rawSince)
          .OrderBy(m => m.Month)
          .Select(m => m.Month)
          .ToListAsync(ct);
        foreach (var month in pending)
        {
          if (await CompactAsync(connection, month, now, ct)) compacted.Add(month);
        }

        return new RetentionRunResult(rawSince, folded.Select(f => f with { Compacted = compacted.Contains(f.Month) }).ToList(), compacted);
      }
      finally
      {
        await ScalarAsync<bool>(connection, null, "SELECT pg_advisory_unlock(hashtext(@key))", CancellationToken.None, ("key", LockKey));
      }
    }
    finally
    {
      await context.Database.CloseConnectionAsync();
    }
  }

  /// <summary>The months older than the window that have a partition, oldest first.</summary>
  private static async Task<IReadOnlyList<DateOnly>> OldMonthsAsync(DbConnection connection, DateOnly rawSince, CancellationToken ct)
  {
    var months = new List<DateOnly>();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT c.relname FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid WHERE i.inhparent = 'sample'::regclass";
    await using (var reader = await cmd.ExecuteReaderAsync(ct))
    {
      while (await reader.ReadAsync(ct))
      {
        if (RetentionWindow.MonthOf(reader.GetString(0)) is { } month && month < rawSince) months.Add(month);
      }
    }
    months.Sort();
    return months;
  }

  // One statement: the dense rows deleted are exactly the rows folded, even while a batch arrives.
  // A minute folded before (a later run for the month) is merged: counts add, the mean is weighted.
  private const string FoldSql = """
    WITH gone AS (
      DELETE FROM sample
      WHERE metric_key = ANY (@keys) AND start_at >= @from AND start_at < @to
      RETURNING person_id, source_id, metric_key, start_at, "value"
    ), minutes AS (
      SELECT person_id, source_id, metric_key, date_trunc('minute', start_at, 'UTC') AS minute_at,
             avg("value") AS avg, min("value") AS min, max("value") AS max, count(*)::integer AS count
      FROM gone
      GROUP BY person_id, source_id, metric_key, date_trunc('minute', start_at, 'UTC')
    ), written AS (
      INSERT INTO sample_minute AS m (person_id, source_id, metric_key, minute_at, avg, min, max, count)
      SELECT person_id, source_id, metric_key, minute_at, avg, min, max, count FROM minutes
      ON CONFLICT (person_id, metric_key, minute_at, source_id) DO UPDATE SET
        avg = (m.avg * m.count + EXCLUDED.avg * EXCLUDED.count) / (m.count + EXCLUDED.count),
        min = LEAST(m.min, EXCLUDED.min),
        max = GREATEST(m.max, EXCLUDED.max),
        count = m.count + EXCLUDED.count
      RETURNING (xmax = 0) AS inserted
    )
    SELECT (SELECT count(*) FROM gone), (SELECT count(*) FROM written WHERE inserted)
    """;

  private const string RecordMonthSql = """
    INSERT INTO sample_rollup_month (month, rolled_at, raw_rows, minute_rows, compacted_at)
    VALUES (@month, @now, @raw, @minutes, NULL)
    ON CONFLICT (month) DO UPDATE SET
      rolled_at = EXCLUDED.rolled_at,
      raw_rows = sample_rollup_month.raw_rows + EXCLUDED.raw_rows,
      minute_rows = sample_rollup_month.minute_rows + EXCLUDED.minute_rows,
      compacted_at = NULL
    """;

  /// <summary>Folds one month in one transaction; null when it held no dense rows.</summary>
  private static async Task<RetentionFoldedDto?> FoldAsync(SelfKeptDbContext context, DateOnly month, DateTime now, CancellationToken ct)
  {
    var from = DayBoundaries.MonthStartUtc(month);
    var to = DayBoundaries.MonthStartUtc(month.AddMonths(1));
    var connection = context.Database.GetDbConnection();
    var keys = DenseMetrics.Keys.ToArray();

    // Cheap check first: after its first fold a month holds only the sparse metrics.
    if (!await ScalarAsync<bool>(connection, null,
          "SELECT EXISTS (SELECT 1 FROM sample WHERE metric_key = ANY (@keys) AND start_at >= @from AND start_at < @to)", ct,
          ("keys", keys), ("from", from), ("to", to)))
      return null;

    await using var tx = await context.Database.BeginTransactionAsync(ct);
    var transaction = tx.GetDbTransaction();
    long rawRows, minuteRows;
    await using (var cmd = Command(connection, transaction, FoldSql, ("keys", keys), ("from", from), ("to", to)))
    await using (var reader = await cmd.ExecuteReaderAsync(ct))
    {
      await reader.ReadAsync(ct);
      rawRows = reader.GetInt64(0);
      minuteRows = reader.GetInt64(1);
    }
    await using (var cmd = Command(connection, transaction, RecordMonthSql, ("month", month), ("now", now), ("raw", rawRows), ("minutes", minuteRows)))
    {
      await cmd.ExecuteNonQueryAsync(ct);
    }
    await tx.CommitAsync(ct);
    return new RetentionFoldedDto(month, rawRows, minuteRows, false);
  }

  /// <summary>
  /// Rewrites the month's partition so the space freed by the fold goes back to the filesystem.
  /// False when the partition was busy past the lock timeout; the next run tries again.
  /// </summary>
  private async Task<bool> CompactAsync(DbConnection connection, DateOnly month, DateTime now, CancellationToken ct)
  {
    var partition = RetentionWindow.PartitionName(month);
    if (await ScalarAsync<string?>(connection, null, "SELECT to_regclass(@name)::text", ct, ("name", partition)) != null)
    {
      try
      {
        await ExecuteAsync(connection, $"SET lock_timeout = '{CompactLockTimeout}'", ct);
        // The name is sample_ and six digits (RetentionWindow.PartitionName), never user input.
        await ExecuteAsync(connection, $"VACUUM (FULL, ANALYZE) {partition}", ct);
      }
      catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
      {
        _logger.LogInformation("Retention left {Partition} for the next run: it was busy", partition);
        return false;
      }
      finally
      {
        await ExecuteAsync(connection, "RESET lock_timeout", CancellationToken.None);
      }
    }
    await using var cmd = Command(connection, null, "UPDATE sample_rollup_month SET compacted_at = @now WHERE month = @month", ("now", now), ("month", month));
    await cmd.ExecuteNonQueryAsync(ct);
    return true;
  }

  /// <summary>The InMemory provider used by tests: the same fold in code, and nothing to rewrite.</summary>
  private static async Task<RetentionRunResult> RunInMemoryAsync(SelfKeptDbContext context, DateOnly rawSince, DateTime now, CancellationToken ct)
  {
    var cutoff = DayBoundaries.MonthStartUtc(rawSince);
    var old = await context.Samples.Where(s => DenseMetrics.Keys.Contains(s.MetricKey) && s.StartAt < cutoff).ToListAsync(ct);
    var folded = new List<RetentionFoldedDto>();
    foreach (var group in old.GroupBy(s => DayBoundaries.MonthStart(s.StartAt)).OrderBy(g => g.Key))
    {
      var month = group.Key;
      var inserted = 0;
      foreach (var minute in Fold(group))
      {
        var existing = await context.SampleMinutes.FindAsync([minute.PersonId, minute.MetricKey, minute.MinuteAt, minute.SourceId], ct);
        if (existing == null)
        {
          context.SampleMinutes.Add(minute);
          inserted++;
        }
        else
        {
          Merge(existing, minute);
        }
      }
      context.Samples.RemoveRange(group);
      var record = await context.SampleRollupMonths.FindAsync([month], ct);
      if (record == null)
      {
        record = new SampleRollupMonth { Month = month };
        context.SampleRollupMonths.Add(record);
      }
      record.RolledAt = now;
      record.RawRows += group.Count();
      record.MinuteRows += inserted;
      record.CompactedAt = now;
      await context.SaveChangesAsync(ct);
      folded.Add(new RetentionFoldedDto(month, group.Count(), inserted, true));
    }
    return new RetentionRunResult(rawSince, folded, folded.Select(f => f.Month).ToList());
  }

  /// <summary>One row per (person, source, metric, UTC minute): mean, min, max and count. The fold SQL in code.</summary>
  public static IReadOnlyList<SampleMinute> Fold(IEnumerable<Sample> samples) =>
    samples
      .GroupBy(s => (s.PersonId, s.SourceId, s.MetricKey, Minute: SampleMinutes.MinuteOf(s.StartAt)))
      .Select(g => new SampleMinute
      {
        Id = Guid.NewGuid(),
        PersonId = g.Key.PersonId,
        SourceId = g.Key.SourceId,
        MetricKey = g.Key.MetricKey,
        MinuteAt = g.Key.Minute,
        Avg = g.Average(s => s.Value),
        Min = g.Min(s => s.Value),
        Max = g.Max(s => s.Value),
        Count = g.Count(),
      })
      .OrderBy(m => m.MinuteAt)
      .ToList();

  /// <summary>Adds <paramref name="incoming"/> into <paramref name="existing"/>, as the fold's ON CONFLICT does.</summary>
  public static void Merge(SampleMinute existing, SampleMinute incoming)
  {
    existing.Avg = (existing.Avg * existing.Count + incoming.Avg * incoming.Count) / (existing.Count + incoming.Count);
    existing.Min = Math.Min(existing.Min, incoming.Min);
    existing.Max = Math.Max(existing.Max, incoming.Max);
    existing.Count += incoming.Count;
  }

  private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
  {
    var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    cmd.Transaction = transaction;
    cmd.CommandTimeout = (int)StatementTimeout.TotalSeconds;
    foreach (var (name, value) in parameters) cmd.Parameters.Add(new NpgsqlParameter(name, value));
    return cmd;
  }

  private static async Task<T> ScalarAsync<T>(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
  {
    await using var cmd = Command(connection, transaction, sql, parameters);
    var value = await cmd.ExecuteScalarAsync(ct);
    return value is null or DBNull ? default! : (T)value;
  }

  private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
  {
    await using var cmd = Command(connection, null, sql);
    await cmd.ExecuteNonQueryAsync(ct);
  }
}
