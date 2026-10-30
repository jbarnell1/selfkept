using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Interpret;

/// <summary>
/// The Trends screen: one metric's daily points with the latest 28-day band behind them, and the
/// list of metrics with enough history to offer. Access is each metric's own category, decided in
/// the repositories.
/// </summary>
public sealed class TrendService : ITrendService
{
  public const int DefaultDays = 28;
  public static readonly int[] AllowedDays = [28, 90];

  private readonly IDailySummaryRepository _summaries;
  private readonly IBaselineRepository _baselines;
  private readonly IPersonRepository _persons;
  private readonly IMetricCatalogue _catalogue;
  private readonly TimeProvider _clock;

  public TrendService(IDailySummaryRepository summaries, IBaselineRepository baselines, IPersonRepository persons, IMetricCatalogue catalogue, TimeProvider clock)
  {
    _summaries = summaries;
    _baselines = baselines;
    _persons = persons;
    _catalogue = catalogue;
    _clock = clock;
  }

  public async Task<TrendSeriesDto?> GetSeriesAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, int? days, CancellationToken ct = default)
  {
    var span = days ?? DefaultDays;
    if (!AllowedDays.Contains(span)) throw new DomainValidationException($"days must be one of {string.Join(", ", AllowedDays)}.");
    var metric = _catalogue.Find(metricKey.Trim());
    if (metric == null) return null;
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;

    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
    var rows = await _summaries.GetRangeAsync(viewerPersonId, targetPersonId, metric.Key, today.AddDays(-(span - 1)), today, ct);
    if (rows == null) return null;
    var baseline = await _baselines.GetLatestAsync(viewerPersonId, targetPersonId, metric.Key, ct);

    return new TrendSeriesDto(metric.Key, UnitFor(metric), rows.Select(r => r.ToTrendPoint()).ToList(), BandOf(baseline), baseline?.N28 ?? 0);
  }

  public async Task<IReadOnlyList<TrendMetricDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var baselines = await _baselines.GetLatestPerMetricAsync(viewerPersonId, targetPersonId, ct);
    if (baselines == null) return null;

    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
    var result = new List<TrendMetricDto>();
    foreach (var baseline in baselines.Where(b => b.N28 >= Baseline.TrendDays))
    {
      var metric = _catalogue.Find(baseline.MetricKey);
      if (metric == null) continue;
      var recent = await _summaries.GetRangeAsync(viewerPersonId, targetPersonId, metric.Key, today.AddDays(-(BaselineService.WindowDays - 1)), today, ct);
      var latest = recent is { Count: > 0 } ? recent[^1] : null;
      result.Add(new TrendMetricDto(
        metric.Key,
        MetricNames.For(metric.Key),
        UnitFor(metric),
        latest?.Day,
        latest?.Value,
        latest == null ? null : BandPositions.Of(latest.Value, baseline.P25_28, baseline.P75_28),
        baseline.N28));
    }
    return result;
  }

  /// <summary>The daily summary of sleep_stage is minutes asleep, not a stage code.</summary>
  public static string UnitFor(Metric metric) => metric.Key == MetricKeys.SleepStage ? "min" : metric.Unit;

  private static TrendBandDto? BandOf(Baseline? b) =>
    b is { Median28: { } median, P25_28: { } p25, P75_28: { } p75, P10_28: { } p10, P90_28: { } p90 } && b.N28 >= Baseline.TrendDays
      ? new TrendBandDto(median, p25, p75, p10, p90)
      : null;
}
