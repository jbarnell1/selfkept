using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret;

/// <summary>
/// Turns 28 days of daily_summary into one baseline row per metric. The window is the 28 local
/// days ending on the given day (inclusive); the 7-day figures use the last 7 of those.
/// </summary>
public sealed class BaselineService : IBaselineService
{
  public const int WindowDays = 28;
  public const int ShortWindowDays = 7;

  private readonly IDailySummaryRepository _summaries;
  private readonly TimeProvider _clock;

  public BaselineService(IDailySummaryRepository summaries, TimeProvider clock)
  {
    _summaries = summaries;
    _clock = clock;
  }

  public async Task<IReadOnlyList<Baseline>> ComputeAsync(Guid personId, DateOnly day, CancellationToken ct = default)
  {
    var from = day.AddDays(-(WindowDays - 1));
    var rows = await _summaries.GetOwnRangeAsync(personId, BaselineMetrics.Keys, from, day, ct);
    var now = _clock.GetUtcNow().UtcDateTime;
    var result = new List<Baseline>();
    foreach (var group in rows.GroupBy(r => r.MetricKey, StringComparer.Ordinal))
    {
      var baseline = Compute(personId, group.Key, day, group.ToList(), now);
      if (baseline != null) result.Add(baseline);
    }
    return result;
  }

  /// <summary>Pure: the row for one metric from the summaries that fall in its window. Null when none do.</summary>
  public static Baseline? Compute(Guid personId, string metricKey, DateOnly day, IReadOnlyList<DailySummary> summaries, DateTime computedAtUtc)
  {
    var from = day.AddDays(-(WindowDays - 1));
    var shortFrom = day.AddDays(-(ShortWindowDays - 1));
    var window = summaries.Where(s => s.Day >= from && s.Day <= day).OrderBy(s => s.Day).ToList();
    if (window.Count == 0) return null;

    var values28 = window.Select(s => s.Value).ToList();
    var values7 = window.Where(s => s.Day >= shortFrom).Select(s => s.Value).ToList();
    var points = window.Select(s => (DayIndex: s.Day.DayNumber - from.DayNumber, s.Value)).ToList();

    return new Baseline
    {
      PersonId = personId,
      MetricKey = metricKey,
      ComputedFor = day,
      Median7 = BaselineMath.Median(values7),
      Median28 = BaselineMath.Median(values28),
      P10_28 = BaselineMath.Percentile(values28, 10),
      P25_28 = BaselineMath.Percentile(values28, 25),
      P75_28 = BaselineMath.Percentile(values28, 75),
      P90_28 = BaselineMath.Percentile(values28, 90),
      Slope28 = BaselineMath.Slope(points),
      Sd28 = BaselineMath.StandardDeviation(values28),
      N7 = values7.Count,
      N28 = values28.Count,
      ComputedAt = computedAtUtc,
    };
  }
}
