using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// Body measurements typed in by hand: weight, body fat and waist, and the readings of a home
/// device (blood pressure, pulse, temperature, glucose, oxygen), which a confirmed photo of its
/// display also arrives as (docs/body-design.md, "Typed measurements"; docs/photo-design.md). Each lands as an ordinary sample under the person's "Typed in" source, so it
/// flows through daily summaries, trends and rules exactly like a reading from a scale.
/// </summary>
public sealed class MeasurementService : IMeasurementService
{
  /// <summary>What can be typed in, with the range a value must fall in (in the metric's own unit).</summary>
  public static readonly IReadOnlyDictionary<string, (double Min, double Max)> Allowed = new Dictionary<string, (double, double)>(StringComparer.Ordinal)
  {
    [MetricKeys.WeightKg] = (20, 400),
    [MetricKeys.BodyFatPct] = (2, 75),
    [MetricKeys.WaistCm] = (30, 250),
    [MetricKeys.BloodPressureSys] = (50, 300),
    [MetricKeys.BloodPressureDia] = (20, 200),
    [MetricKeys.HeartRateBpm] = (20, 250),
    [MetricKeys.BodyTempC] = (30, 45),
    [MetricKeys.BloodGlucoseMmol] = (1, 40),
    [MetricKeys.Spo2Pct] = (50, 100),
  };

  private static readonly TimeSpan ListWindow = TimeSpan.FromDays(730);

  private readonly ISourceRepository _sources;
  private readonly ISampleRepository _samples;
  private readonly IPersonRepository _persons;
  private readonly IDailySummaryService _summaries;
  private readonly IMetricCatalogue _catalogue;
  private readonly IAuditService _audit;
  private readonly IUnitOfWork _unitOfWork;
  private readonly ITodayCacheRepository _todayCache;
  private readonly TimeProvider _clock;

  public MeasurementService(
    ISourceRepository sources,
    ISampleRepository samples,
    IPersonRepository persons,
    IDailySummaryService summaries,
    IMetricCatalogue catalogue,
    IAuditService audit,
    IUnitOfWork unitOfWork,
    ITodayCacheRepository todayCache,
    TimeProvider clock)
  {
    _sources = sources;
    _samples = samples;
    _persons = persons;
    _summaries = summaries;
    _catalogue = catalogue;
    _audit = audit;
    _unitOfWork = unitOfWork;
    _todayCache = todayCache;
    _clock = clock;
  }

  public async Task<MeasurementDto> CreateAsync(Actor actor, CreateMeasurementRequest request, CancellationToken ct = default)
  {
    var metric = RequireMetric(request.MetricKey);
    var (min, max) = Allowed[metric.Key];
    if (!double.IsFinite(request.Value) || request.Value < min || request.Value > max)
    {
      throw new DomainValidationException($"{metric.DisplayName} must be between {min:0} and {max:0} {metric.Unit}.");
    }
    var now = _clock.GetUtcNow().UtcDateTime;
    var at = request.At is { } requested ? DayBoundaries.AsUtc(requested) : now;
    if (at > now.AddHours(1)) throw new DomainValidationException("at cannot be in the future.");
    if (at < now - ListWindow) throw new DomainValidationException("at cannot be more than two years ago.");

    var person = await _persons.GetByIdAsync(actor.PersonId, ct)
      ?? throw new DomainValidationException("No person profile for this account.");
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var id = Guid.CreateVersion7();

    await _unitOfWork.RunInTransactionAsync(async () =>
    {
      var source = await GetOrCreateSourceAsync(actor.PersonId, now, ct);
      await _samples.EnsurePartitionsAsync([DayBoundaries.MonthStart(at)], ct);
      var result = await _samples.InsertManyAsync(
      [
        new Sample
        {
          Id = id,
          PersonId = actor.PersonId,
          SourceId = source.Id,
          SourceUid = SourceUid(id),
          MetricKey = metric.Key,
          StartAt = at,
          Value = request.Value,
          Unit = metric.Unit,
          CreatedAt = now,
        },
      ], ct);

      source.LastSyncAt = now;
      source.LastSampleAt = source.LastSampleAt is { } last && last > at ? last : at;
      source.SampleCount += result.Accepted;
      await _sources.UpdateAsync(source, ct);

      await _summaries.RecomputeAsync(actor.PersonId, person.TimeZone, [(metric.Key, DayBoundaries.LocalDay(at, zone))], ct);
      await _todayCache.InvalidateAsync(actor.PersonId, ct);
      return true;
    }, ct: ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.MeasurementCreated, actor.UserId, actor.HouseholdId, "sample", id.ToString(),
      new { metric = metric.Key }, actor.Info.IpAddress), ct);
    return new MeasurementDto(id, metric.Key, request.Value, metric.Unit, at);
  }

  public async Task<IReadOnlyList<MeasurementDto>> ListOwnAsync(Guid personId, string metricKey, CancellationToken ct = default)
  {
    var metric = RequireMetric(metricKey);
    var typed = await TypedAsync(personId, metric.Key, ct);
    return typed
      .OrderByDescending(s => s.StartAt)
      .Select(s => new MeasurementDto(IdOf(s.SourceUid) ?? s.Id, s.MetricKey, s.Value, s.Unit, s.StartAt))
      .ToList();
  }

  public async Task<bool> DeleteAsync(Actor actor, string metricKey, Guid measurementId, CancellationToken ct = default)
  {
    var metric = RequireMetric(metricKey);
    var uid = SourceUid(measurementId);
    var sample = (await TypedAsync(actor.PersonId, metric.Key, ct)).FirstOrDefault(s => s.SourceUid == uid);
    if (sample == null) return false;
    var person = await _persons.GetByIdAsync(actor.PersonId, ct);
    var timeZone = person?.TimeZone ?? "UTC";

    await _unitOfWork.RunInTransactionAsync(async () =>
    {
      var removed = await _samples.DeleteBySourceUidAsync(sample.SourceId, uid, ct);
      var source = await _sources.GetAsync(actor.PersonId, sample.SourceId, ct);
      if (source != null)
      {
        source.SampleCount = Math.Max(0, source.SampleCount - removed);
        await _sources.UpdateAsync(source, ct);
      }
      await _summaries.RecomputeAsync(actor.PersonId, timeZone, [(metric.Key, DayBoundaries.LocalDay(sample.StartAt, DayBoundaries.Resolve(timeZone)))], ct);
      await _todayCache.InvalidateAsync(actor.PersonId, ct);
      return true;
    }, ct: ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.MeasurementDeleted, actor.UserId, actor.HouseholdId, "sample", measurementId.ToString(),
      new { metric = metric.Key }, actor.Info.IpAddress), ct);
    return true;
  }

  public static string SourceUid(Guid measurementId) => $"manual:{measurementId:N}";

  private static Guid? IdOf(string sourceUid) =>
    sourceUid.StartsWith("manual:", StringComparison.Ordinal) && Guid.TryParseExact(sourceUid["manual:".Length..], "N", out var id) ? id : null;

  private Metric RequireMetric(string? metricKey)
  {
    if (metricKey == null || !Allowed.ContainsKey(metricKey) || _catalogue.Find(metricKey) is not { } metric)
    {
      throw new DomainValidationException($"metricKey must be one of: {string.Join(", ", Allowed.Keys)}.");
    }
    return metric;
  }

  /// <summary>The person's own typed-in samples of one metric, from the last two years.</summary>
  private async Task<IReadOnlyList<Sample>> TypedAsync(Guid personId, string metricKey, CancellationToken ct)
  {
    var source = await _sources.FindActiveAsync(personId, SourceKinds.Manual, SourceKinds.TypedDeviceName, SourceKinds.CheckinPlatform, ct);
    if (source == null) return [];
    var now = _clock.GetUtcNow().UtcDateTime;
    var samples = await _samples.GetOwnInWindowAsync(personId, metricKey, now - ListWindow, now.AddHours(2), byEnd: false, ct);
    return samples.Where(s => s.SourceId == source.Id).ToList();
  }

  private async Task<Source> GetOrCreateSourceAsync(Guid personId, DateTime now, CancellationToken ct)
  {
    var existing = await _sources.FindActiveAsync(personId, SourceKinds.Manual, SourceKinds.TypedDeviceName, SourceKinds.CheckinPlatform, ct);
    if (existing != null) return existing;
    return await _sources.AddAsync(new Source
    {
      Id = Guid.CreateVersion7(),
      PersonId = personId,
      Kind = SourceKinds.Manual,
      DeviceName = SourceKinds.TypedDeviceName,
      Platform = SourceKinds.CheckinPlatform,
      Enabled = true,
      CreatedAt = now,
    }, ct);
  }
}
