using SelfKept.Core.Entities;

namespace SelfKept.Core.Interfaces;

/// <summary>
/// The metric catalogue, loaded from the metric table once at startup and held in memory. Every
/// ingest and every data read consults it: unknown keys are rejected, and the metric's category
/// decides which share grant a viewer needs.
/// </summary>
public interface IMetricCatalogue
{
  IReadOnlyList<Metric> All { get; }
  Metric? Find(string key);
  Task ReloadAsync(CancellationToken ct = default);
}

/// <summary>
/// Sources are device registrations, not health data: they are never shared, so every method is
/// scoped to the owning person and there is no viewer/target pair here.
/// </summary>
public interface ISourceRepository
{
  Task<IReadOnlyList<Source>> ListAsync(Guid personId, CancellationToken ct = default);

  /// <summary>Null when the source does not exist, belongs to someone else, or is deleted.</summary>
  Task<Source?> GetAsync(Guid personId, Guid sourceId, CancellationToken ct = default);

  Task<Source?> FindActiveAsync(Guid personId, string kind, string deviceName, string platform, CancellationToken ct = default);
  Task<Source> AddAsync(Source source, CancellationToken ct = default);
  Task UpdateAsync(Source source, CancellationToken ct = default);
}

public sealed record BulkInsertResult(int Accepted, int Duplicates);

/// <summary>Keyset position for the samples page: the last row's (startAt, id), newest first.</summary>
public sealed record SampleCursor(DateTime StartAt, Guid Id);

public sealed record SamplePage(IReadOnlyList<Sample> Items, SampleCursor? Next);

public sealed record MetricOverview(string MetricKey, DateTime FirstAt, DateTime LastAt, long Count, double LastValue);

public interface ISampleRepository
{
  /// <summary>Creates the monthly partitions the given days fall in (no-op on a non-relational provider).</summary>
  Task EnsurePartitionsAsync(IEnumerable<DateOnly> months, CancellationToken ct = default);

  /// <summary>Inserts with ON CONFLICT (source_id, source_uid) DO NOTHING; reports how many were new.</summary>
  Task<BulkInsertResult> InsertManyAsync(IReadOnlyList<Sample> samples, CancellationToken ct = default);

  Task<int> DeleteBySourceUidAsync(Guid sourceId, string sourceUid, CancellationToken ct = default);

  /// <summary>
  /// Server-internal read for summary derivation: the person's own samples of one metric whose
  /// start (or end, when byEnd) lies in [fromUtc, toUtc). Not a viewer read; no grant applies.
  /// </summary>
  Task<IReadOnlyList<Sample>> GetOwnInWindowAsync(Guid personId, string metricKey, DateTime fromUtc, DateTime toUtc, bool byEnd, CancellationToken ct = default);

  /// <summary>Newest first. Null when the metric is unknown or the viewer may not see the target's category.</summary>
  Task<SamplePage?> GetPageAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, DateTime? fromUtc, DateTime? toUtc, int limit, SampleCursor? after, CancellationToken ct = default);

  /// <summary>
  /// One row per metric the viewer may see. For another person only granted categories are
  /// included; null when nothing at all is granted (the caller answers 404).
  /// </summary>
  Task<IReadOnlyList<MetricOverview>?> GetOverviewAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);
}

public interface IWorkoutRepository
{
  Task<BulkInsertResult> InsertManyAsync(IReadOnlyList<Workout> workouts, CancellationToken ct = default);

  /// <summary>Null when the viewer may not see the target's activity.</summary>
  Task<IReadOnlyList<Workout>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}

public interface ICheckinRepository
{
  Task<Checkin> AddAsync(Checkin checkin, CancellationToken ct = default);

  /// <summary>The person's own check-in; null when it is someone else's or missing.</summary>
  Task<Checkin?> GetOwnAsync(Guid personId, Guid checkinId, CancellationToken ct = default);

  /// <summary>Newest first, at most limit rows. Null when the viewer may not see the target's check-ins.</summary>
  Task<IReadOnlyList<Checkin>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct = default);

  Task RemoveAsync(Checkin checkin, CancellationToken ct = default);
}

public interface IDailySummaryRepository
{
  /// <summary>Inserts or replaces each row by (person, metric, day).</summary>
  Task UpsertManyAsync(IReadOnlyList<DailySummary> rows, CancellationToken ct = default);

  Task DeleteAsync(Guid personId, string metricKey, DateOnly day, CancellationToken ct = default);

  /// <summary>Days ascending. Null when the metric is unknown or the viewer may not see the target's category.</summary>
  Task<IReadOnlyList<DailySummary>?> GetRangeAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, DateOnly from, DateOnly to, CancellationToken ct = default);

  /// <summary>
  /// Server-internal read for baselines and rules: the person's own rows for several metrics over
  /// a day range, days ascending. Not a viewer read; no grant applies.
  /// </summary>
  Task<IReadOnlyList<DailySummary>> GetOwnRangeAsync(Guid personId, IReadOnlyCollection<string> metricKeys, DateOnly from, DateOnly to, CancellationToken ct = default);

  /// <summary>
  /// The Today screen's read: several metrics over a day range, gated on the insights category
  /// rather than each metric's own. Null when the viewer may not see the target's insights.
  /// </summary>
  Task<IReadOnlyList<DailySummary>?> GetRangeForInsightsAsync(Guid viewerPersonId, Guid targetPersonId, IReadOnlyCollection<string> metricKeys, DateOnly from, DateOnly to, CancellationToken ct = default);
}
