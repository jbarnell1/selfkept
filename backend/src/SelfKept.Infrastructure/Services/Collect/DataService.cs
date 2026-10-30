using System.Buffers.Text;
using System.Globalization;
using System.Text;
using SelfKept.Core.DTOs;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>Read side: daily series, raw sample pages and the per-metric overview. Access is decided in the repositories.</summary>
public sealed class DataService : IDataService
{
  public const int DefaultDailyDays = 30;
  public const int MaxDailyDays = 366;
  public const int DefaultSampleLimit = 500;
  public const int MaxSampleLimit = 2000;

  private readonly ISampleRepository _samples;
  private readonly IDailySummaryRepository _summaries;
  private readonly IPersonRepository _persons;
  private readonly IMetricCatalogue _catalogue;
  private readonly TimeProvider _clock;

  public DataService(ISampleRepository samples, IDailySummaryRepository summaries, IPersonRepository persons, IMetricCatalogue catalogue, TimeProvider clock)
  {
    _samples = samples;
    _summaries = summaries;
    _persons = persons;
    _catalogue = catalogue;
    _clock = clock;
  }

  public async Task<DailySeriesDto?> GetDailyAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, DateOnly? from, DateOnly? to, CancellationToken ct = default)
  {
    var metric = _catalogue.Find(metricKey.Trim());
    if (metric == null) return null;
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;

    var zone = DayBoundaries.Resolve(person.TimeZone);
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, zone);
    var end = to ?? today;
    var start = from ?? end.AddDays(-(DefaultDailyDays - 1));
    if (start > end) throw new DomainValidationException("from must not be after to.");
    if (end.DayNumber - start.DayNumber >= MaxDailyDays) throw new DomainValidationException($"A daily range covers at most {MaxDailyDays} days.");

    var rows = await _summaries.GetRangeAsync(viewerPersonId, targetPersonId, metric.Key, start, end, ct);
    if (rows == null) return null;
    // Sleep stages are summarised as minutes asleep per night, so the series' unit is minutes.
    return new DailySeriesDto(metric.Key, Interpret.TrendService.UnitFor(metric), rows.Select(r => r.ToDto()).ToList());
  }

  public async Task<SamplePageDto?> GetSamplesAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, DateTime? fromUtc, DateTime? toUtc, int? limit, string? cursor, CancellationToken ct = default)
  {
    var metric = _catalogue.Find(metricKey.Trim());
    if (metric == null) return null;

    var pageSize = Math.Clamp(limit ?? DefaultSampleLimit, 1, MaxSampleLimit);
    var from = fromUtc is { } f ? DayBoundaries.AsUtc(f) : (DateTime?)null;
    var to = toUtc is { } t ? DayBoundaries.AsUtc(t) : (DateTime?)null;
    if (from != null && to != null && from > to) throw new DomainValidationException("from must not be after to.");
    var after = DecodeCursor(cursor);

    var page = await _samples.GetPageAsync(viewerPersonId, targetPersonId, metric.Key, from, to, pageSize, after, ct);
    if (page == null) return null;
    return new SamplePageDto(page.Items.Select(s => s.ToDto()).ToList(), page.Next == null ? null : EncodeCursor(page.Next));
  }

  public async Task<OverviewDto?> GetOverviewAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var rows = await _samples.GetOverviewAsync(viewerPersonId, targetPersonId, ct);
    if (rows == null) return null;
    var metrics = new List<OverviewMetricDto>();
    foreach (var row in rows)
    {
      var metric = _catalogue.Find(row.MetricKey);
      if (metric == null) continue;
      metrics.Add(new OverviewMetricDto(metric.Key, metric.Unit, row.FirstAt, row.LastAt, row.Count, row.LastValue));
    }
    return new OverviewDto(metrics);
  }

  // Opaque to clients: base64url of "<utc ticks>:<guid>".
  public static string EncodeCursor(SampleCursor cursor) =>
    Base64Url.EncodeToString(Encoding.ASCII.GetBytes($"{cursor.StartAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{cursor.Id:N}"));

  public static SampleCursor? DecodeCursor(string? cursor)
  {
    if (string.IsNullOrWhiteSpace(cursor)) return null;
    try
    {
      var text = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor.Trim()));
      var parts = text.Split(':');
      if (parts.Length == 2
          && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
          && Guid.TryParseExact(parts[1], "N", out var id))
      {
        return new SampleCursor(new DateTime(ticks, DateTimeKind.Utc), id);
      }
    }
    catch (FormatException)
    {
      // fall through
    }
    throw new DomainValidationException("cursor is not valid. Use the nextCursor from the previous page.");
  }
}
