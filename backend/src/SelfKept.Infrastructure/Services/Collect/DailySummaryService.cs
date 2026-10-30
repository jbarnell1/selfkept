using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>
/// Recomputes each touched (metric, day) from the samples in the database rather than adding
/// deltas, so a resent batch, a deleted check-in or a late sample all leave the row correct.
/// </summary>
public sealed class DailySummaryService : IDailySummaryService
{
  private readonly ISampleRepository _samples;
  private readonly IDailySummaryRepository _summaries;
  private readonly TimeProvider _clock;

  public DailySummaryService(ISampleRepository samples, IDailySummaryRepository summaries, TimeProvider clock)
  {
    _samples = samples;
    _summaries = summaries;
    _clock = clock;
  }

  public async Task RecomputeAsync(Guid personId, string timeZone, IReadOnlyCollection<(string MetricKey, DateOnly Day)> touched, CancellationToken ct = default)
  {
    if (touched.Count == 0) return;
    var zone = DayBoundaries.Resolve(timeZone);
    var now = _clock.GetUtcNow().UtcDateTime;
    var rows = new List<DailySummary>();

    foreach (var (metricKey, day) in touched.Distinct())
    {
      if (!DailySummaryRules.HasRule(metricKey)) continue;
      var (fromUtc, toUtc) = DayBoundaries.UtcWindow(day, zone);
      var samples = await _samples.GetOwnInWindowAsync(personId, metricKey, fromUtc, toUtc, DailySummaryRules.AttributesByEnd(metricKey), ct);
      var row = DailySummaryRules.Derive(personId, metricKey, day, samples, now);
      if (row == null)
      {
        await _summaries.DeleteAsync(personId, metricKey, day, ct);
      }
      else
      {
        rows.Add(row);
      }
    }

    if (rows.Count > 0) await _summaries.UpsertManyAsync(rows, ct);
  }
}
