using SelfKept.Core.Entities;

namespace SelfKept.Core.Interfaces;

/// <summary>
/// Baselines. Viewer reads check the metric's own share category (trends) or the insights
/// category (Today); the "own" methods are server-internal, used by the nightly job.
/// </summary>
public interface IBaselineRepository
{
  /// <summary>Inserts or replaces each row by (person, metric, computed_for).</summary>
  Task UpsertManyAsync(IReadOnlyList<Baseline> rows, CancellationToken ct = default);

  /// <summary>Server-internal: the rows computed for one day. Not a viewer read.</summary>
  Task<IReadOnlyList<Baseline>> GetOwnForDayAsync(Guid personId, DateOnly computedFor, CancellationToken ct = default);

  /// <summary>Server-internal: whether any baseline row exists for the day (the nightly job's idempotency check).</summary>
  Task<bool> AnyForDayAsync(Guid personId, DateOnly computedFor, CancellationToken ct = default);

  /// <summary>The newest row for one metric. Null when the metric is unknown, the viewer may not see its category, or there is none.</summary>
  Task<Baseline?> GetLatestAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, CancellationToken ct = default);

  /// <summary>
  /// The newest row per metric, filtered to the categories the viewer may see. Null when nothing
  /// at all is granted (the caller answers 404).
  /// </summary>
  Task<IReadOnlyList<Baseline>?> GetLatestPerMetricAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  /// <summary>
  /// The newest row per metric for the Today screen: one read gated on the insights category
  /// rather than each metric's own, because Today is an insights-category product.
  /// </summary>
  Task<IReadOnlyList<Baseline>?> GetLatestPerMetricForInsightsAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);
}

public interface IInsightRepository
{
  Task<Insight> AddAsync(Insight insight, CancellationToken ct = default);
  Task UpdateAsync(Insight insight, CancellationToken ct = default);

  /// <summary>Server-internal: the row for (person, kind, day), so a rerun re-ranks instead of duplicating.</summary>
  Task<Insight?> GetOwnByKindAsync(Guid personId, string kind, DateOnly day, CancellationToken ct = default);

  /// <summary>The person's own card; null when it is someone else's or missing.</summary>
  Task<Insight?> GetOwnAsync(Guid personId, Guid insightId, CancellationToken ct = default);

  /// <summary>Server-internal: whether any insight exists for the day.</summary>
  Task<bool> AnyForDayAsync(Guid personId, DateOnly day, CancellationToken ct = default);

  /// <summary>Server-internal: how many of the person's cards of this kind carry the reaction since the day.</summary>
  Task<int> CountReactionsAsync(Guid personId, string kind, string reaction, DateOnly sinceDay, CancellationToken ct = default);

  /// <summary>Newest first. Null when the viewer may not see the target's insights.</summary>
  Task<IReadOnlyList<Insight>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly from, DateOnly to, bool includeDismissed, CancellationToken ct = default);
}

public interface IAiCallRepository
{
  /// <summary>Appends on its own context; a failure is logged, never thrown.</summary>
  Task RecordAsync(AiCall call, CancellationToken ct = default);
}

public interface ITodayCacheRepository
{
  /// <summary>Server-internal: the cached sentence for the person, whatever its age.</summary>
  Task<TodayCache?> GetAsync(Guid personId, CancellationToken ct = default);

  Task SetAsync(TodayCache cache, CancellationToken ct = default);

  /// <summary>Drops the cached sentence so the next GET /api/today composes afresh.</summary>
  Task InvalidateAsync(Guid personId, CancellationToken ct = default);
}
