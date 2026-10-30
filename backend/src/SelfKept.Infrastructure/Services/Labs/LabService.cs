using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Formatting;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Security;
using SelfKept.Infrastructure.Services.Collect;
using SelfKept.Infrastructure.Services.Interpret.Rules;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>
/// Lab reports end to end: parsing, the confirmed write (results, derived ratios, encrypted
/// image, lab.* samples under the "Lab results" source, Today cache, an interpret run for
/// today), the reads for the Labs screen and the clinician PDF. Access is decided in the
/// repositories: labs for reports and markers, each metric's own category for the PDF trend rows.
/// </summary>
public sealed class LabService : ILabService
{
  public const int MaxResults = 60;
  public const int MaxImageBytes = 8 * 1024 * 1024;
  public const int SummaryDays = 90;
  public const int MaxSummaryDays = 366;
  public const int LastThree = 3;

  private readonly ILabReportRepository _reports;
  private readonly ILabReferenceRepository _references;
  private readonly IMarkerAliasRepository _aliases;
  private readonly IMetricCatalogue _catalogue;
  private readonly IPersonRepository _persons;
  private readonly ISourceRepository _sources;
  private readonly ISampleRepository _samples;
  private readonly IDailySummaryRepository _summaries;
  private readonly ITodayCacheRepository _todayCache;
  private readonly IInterpretRunner _runner;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly IUnitOfWork _unitOfWork;
  private readonly LabSummaryPdfRenderer _pdf;
  private readonly TimeProvider _clock;
  private readonly ILogger<LabService> _logger;

  public LabService(
    ILabReportRepository reports,
    ILabReferenceRepository references,
    IMarkerAliasRepository aliases,
    IMetricCatalogue catalogue,
    IPersonRepository persons,
    ISourceRepository sources,
    ISampleRepository samples,
    IDailySummaryRepository summaries,
    ITodayCacheRepository todayCache,
    IInterpretRunner runner,
    IEncryptionService encryption,
    IAuditService audit,
    IUnitOfWork unitOfWork,
    LabSummaryPdfRenderer pdf,
    TimeProvider clock,
    ILogger<LabService> logger)
  {
    _reports = reports;
    _references = references;
    _aliases = aliases;
    _catalogue = catalogue;
    _persons = persons;
    _sources = sources;
    _samples = samples;
    _summaries = summaries;
    _todayCache = todayCache;
    _runner = runner;
    _encryption = encryption;
    _audit = audit;
    _unitOfWork = unitOfWork;
    _pdf = pdf;
    _clock = clock;
    _logger = logger;
  }

  public ParseResultDto Parse(string text) => new LabParser(_catalogue, _aliases.All).Parse(text);

  public async Task<LabReportDto> CreateReportAsync(Guid userId, Guid personId, Guid? householdId, CreateLabReportRequest request, RequestInfo info, CancellationToken ct = default)
  {
    var provider = request.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
    if (!LabProviders.All.Contains(provider)) throw new DomainValidationException($"provider must be one of {string.Join(", ", LabProviders.All)}.");
    if (request.CollectedOn is not { } collectedOn) throw new DomainValidationException("collectedOn is required.");
    if (request.Results == null || request.Results.Count == 0) throw new DomainValidationException("results must hold at least one marker.");
    if (request.Results.Count > MaxResults) throw new DomainValidationException($"At most {MaxResults} results per report.");

    var person = await _persons.GetByIdAsync(personId, ct) ?? throw new DomainValidationException("No person profile for this account.");
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var now = _clock.GetUtcNow().UtcDateTime;
    var today = DayBoundaries.LocalDay(now, zone);
    if (collectedOn > today) throw new DomainValidationException("collectedOn cannot be in the future.");
    if (collectedOn.Year < 1900) throw new DomainValidationException("collectedOn is before 1900.");
    if (request.ReceivedOn is { } received && received < collectedOn) throw new DomainValidationException("receivedOn cannot be before collectedOn.");

    var reportId = Guid.CreateVersion7();
    var results = new List<LabResult>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var input in request.Results)
    {
      results.Add(ToResult(input, reportId, personId, now, seen));
    }
    results.AddRange(DeriveRatios(results, reportId, personId, now));

    var (image, sha) = DecodeImage(request.Image);
    var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
    var report = new LabReport
    {
      Id = reportId,
      PersonId = personId,
      Provider = provider,
      CollectedOn = collectedOn,
      ReceivedOn = request.ReceivedOn,
      SourceImageCipher = image == null ? null : CollectMapping.ToCipherBytes(_encryption.Encrypt(Convert.ToBase64String(image))),
      ImageSha256 = sha,
      NotesCipher = notes == null ? null : CollectMapping.ToCipherBytes(_encryption.Encrypt(notes)),
      CreatedAt = now,
      ConfirmedAt = now,
      Results = results,
    };

    var sampleAt = NoonLocal(collectedOn, zone);
    await _unitOfWork.RunInTransactionAsync(async () =>
    {
      var source = await GetOrCreateSystemSourceAsync(personId, now, ct);
      await _reports.AddAsync(report, ct);

      await _samples.EnsurePartitionsAsync([DayBoundaries.MonthStart(sampleAt)], ct);
      var inserted = await _samples.InsertManyAsync(results.Select(r => ToSample(r, source, sampleAt, now)).ToList(), ct);
      source.LastSyncAt = now;
      source.LastSampleAt = source.LastSampleAt is { } last && last > sampleAt ? last : sampleAt;
      source.SampleCount += inserted.Accepted;
      await _sources.UpdateAsync(source, ct);

      await _todayCache.InvalidateAsync(personId, ct);
      return true;
    }, ct: ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.LabReportCreated, userId, householdId, "lab_report", report.Id.ToString(),
      new { provider, collectedOn = collectedOn.ToIso(), results = results.Count, derived = results.Count(r => r.Derived), hasImage = image != null }, info.IpAddress), ct);

    // The lab rules run today rather than waiting for the nightly job. A failure here is logged
    // and never fails the report, which is already stored.
    try
    {
      await _runner.RunForPersonAsync(personId, today, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "Interpret run after lab report {ReportId} failed for person {PersonId}", report.Id, personId);
    }

    return report.ToDto(_catalogue, notes);
  }

  public async Task<IReadOnlyList<LabReportDto>?> ListReportsAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var rows = await _reports.ListAsync(viewerPersonId, targetPersonId, ct);
    return rows?.Select(r => r.ToDto(_catalogue, Notes(r))).ToList();
  }

  public async Task<LabReportDto?> GetReportAsync(Guid viewerPersonId, Guid reportId, CancellationToken ct = default)
  {
    var report = await _reports.GetAsync(viewerPersonId, reportId, ct);
    return report?.ToDto(_catalogue, Notes(report));
  }

  public async Task<bool> DeleteReportAsync(Guid userId, Guid personId, Guid? householdId, Guid reportId, RequestInfo info, CancellationToken ct = default)
  {
    var report = await _reports.GetOwnAsync(personId, reportId, ct);
    if (report == null) return false;

    await _unitOfWork.RunInTransactionAsync(async () =>
    {
      var source = await _sources.FindActiveAsync(personId, SourceKinds.LabScan, LabSource.DeviceName, LabSource.Platform, ct);
      if (source != null)
      {
        var removed = 0;
        foreach (var result in report.Results) removed += await _samples.DeleteBySourceUidAsync(source.Id, SampleUid(result.Id), ct);
        source.SampleCount = Math.Max(0, source.SampleCount - removed);
        await _sources.UpdateAsync(source, ct);
      }
      await _reports.RemoveAsync(report, ct);
      await _todayCache.InvalidateAsync(personId, ct);
      return true;
    }, ct: ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.LabReportDeleted, userId, householdId, "lab_report", report.Id.ToString(),
      new { collectedOn = report.CollectedOn.ToIso(), results = report.Results.Count }, info.IpAddress), ct);
    return true;
  }

  public async Task<IReadOnlyList<LabMarkerDto>?> GetMarkersAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var reports = await _reports.ListAsync(viewerPersonId, targetPersonId, ct);
    if (reports == null) return null;

    var history = HistoryByMarker(reports);
    return _catalogue.All
      .Where(m => m.IsLab)
      .OrderBy(m => LabPanel.OrderOf(m.Key)).ThenBy(m => m.Key, StringComparer.Ordinal)
      .Select(m => Marker(m, person.SexAtBirth, history.TryGetValue(m.Key, out var points) ? points : []))
      .ToList();
  }

  public async Task<LabMarkerDetailDto?> GetMarkerAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, CancellationToken ct = default)
  {
    var key = metricKey.Trim();
    if (!key.StartsWith(MetricKeys.LabPrefix, StringComparison.Ordinal)) key = MetricKeys.LabPrefix + key;
    var metric = _catalogue.Find(key);
    if (metric is not { IsLab: true }) return null;
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var reports = await _reports.ListAsync(viewerPersonId, targetPersonId, ct);
    if (reports == null) return null;

    var history = HistoryByMarker(reports).TryGetValue(metric.Key, out var points) ? points : [];
    var reference = _references.Find(metric, person.SexAtBirth);
    return new LabMarkerDetailDto(Marker(metric, person.SexAtBirth, history), metric.Blurb, LabPanel.WhatMoves(metric.Key), reference?.SourceUrl, history);
  }

  public async Task<LabSummaryPdf?> ExportSummaryAsync(Guid userId, Guid viewerPersonId, Guid targetPersonId, Guid? householdId, DateOnly? from, DateOnly? to, RequestInfo info, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
    var end = to ?? today;
    var start = from ?? end.AddDays(-(SummaryDays - 1));
    if (start > end) throw new DomainValidationException("from must not be after to.");
    if (end.DayNumber - start.DayNumber >= MaxSummaryDays) throw new DomainValidationException($"A summary covers at most {MaxSummaryDays} days.");

    var reports = await _reports.ListAsync(viewerPersonId, targetPersonId, ct);
    if (reports == null) return null;

    var trends = new List<LabSummaryTrendRow>();
    foreach (var (key, label) in LabSummaryPdfRenderer.TrendMetrics)
    {
      var rows = await _summaries.GetRangeAsync(viewerPersonId, targetPersonId, key, start, end, ct);
      if (rows == null || rows.Count == 0) continue;
      var values = rows.Select(r => r.Value).OrderBy(v => v).ToList();
      var unit = _catalogue.Find(key) is { } m ? Interpret.TrendService.UnitFor(m) : string.Empty;
      trends.Add(new LabSummaryTrendRow(label, unit, values.Count, Median(values), values[0], values[^1]));
    }

    var panels = reports.Where(r => r.CollectedOn <= end).Take(2).ToList();
    var labRows = new List<LabSummaryLabRow>();
    foreach (var metric in _catalogue.All.Where(m => m.IsLab).OrderBy(m => LabPanel.OrderOf(m.Key)))
    {
      var latest = panels.ElementAtOrDefault(0)?.Results.FirstOrDefault(x => x.MetricKey == metric.Key);
      var previous = panels.ElementAtOrDefault(1)?.Results.FirstOrDefault(x => x.MetricKey == metric.Key);
      if (latest == null && previous == null) continue;
      var reference = _references.Find(metric, person.SexAtBirth);
      labRows.Add(new LabSummaryLabRow(
        metric.DisplayName, metric.Unit,
        latest?.Value, FlagFor(latest, reference),
        previous?.Value, FlagFor(previous, reference),
        reference == null ? string.Empty : LabRules.Range(reference)));
    }

    var model = new LabSummaryModel(
      person.DisplayName,
      start, end,
      trends,
      panels.ElementAtOrDefault(0)?.CollectedOn,
      panels.ElementAtOrDefault(1)?.CollectedOn,
      labRows,
      today);
    var bytes = _pdf.Render(model);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.LabSummaryExported, userId, householdId, "person", targetPersonId.ToString(),
      new { from = start.ToIso(), to = end.ToIso(), panels = panels.Count, trendRows = trends.Count }, info.IpAddress), ct);

    return new LabSummaryPdf(bytes, $"labs-summary-{start:yyyyMMdd}-{end:yyyyMMdd}.pdf", start, end);
  }

  // --- building blocks ---------------------------------------------------------------------

  private LabResult ToResult(LabResultInput input, Guid reportId, Guid personId, DateTime now, HashSet<string> seen)
  {
    var key = input.MetricKey?.Trim() ?? string.Empty;
    var metric = _catalogue.Find(key);
    if (metric is not { IsLab: true }) throw new DomainValidationException($"'{key}' is not a lab marker (metricKey must be one of the lab.* catalogue keys).");
    if (!seen.Add(metric.Key)) throw new DomainValidationException($"'{metric.Key}' appears more than once.");
    if (!double.IsFinite(input.Value) || input.Value < 0) throw new DomainValidationException($"{metric.Key}: value must be a finite number of zero or more.");

    var unit = string.IsNullOrWhiteSpace(input.Unit) ? metric.Unit : input.Unit.Trim();
    var value = UnitConversion.Convert(metric, unit, input.Value)
      ?? throw new DomainValidationException($"{metric.Key}: unit '{unit}' is not accepted (expected '{metric.Unit}' or a convertible unit).");
    var factor = input.Value == 0 ? 1 : value / input.Value;

    return new LabResult
    {
      Id = Guid.CreateVersion7(),
      ReportId = reportId,
      PersonId = personId,
      MetricKey = metric.Key,
      Value = Math.Round(value, 4),
      Unit = metric.Unit,
      Flag = NormaliseFlag(input.Flag, metric.Key),
      RawText = string.IsNullOrWhiteSpace(input.RawText) ? null : input.RawText.Trim(),
      Confirmed = true,
      ReferenceLow = input.ReferenceLow is { } low ? Math.Round(low * factor, 4) : null,
      ReferenceHigh = input.ReferenceHigh is { } high ? Math.Round(high * factor, 4) : null,
      Derived = false,
      CreatedAt = now,
    };
  }

  /// <summary>The three ratios, when both components are present and the ratio itself was not supplied. Marked derived.</summary>
  public static IReadOnlyList<LabResult> DeriveRatios(IReadOnlyList<LabResult> results, Guid reportId, Guid personId, DateTime now)
  {
    var byKey = results.ToDictionary(r => r.MetricKey, StringComparer.Ordinal);
    var derived = new List<LabResult>();
    foreach (var (ratio, numerator, denominator) in LabPanel.DerivedRatios)
    {
      if (byKey.ContainsKey(ratio)) continue;
      if (!byKey.TryGetValue(numerator, out var top) || !byKey.TryGetValue(denominator, out var bottom) || bottom.Value <= 0) continue;
      derived.Add(new LabResult
      {
        Id = Guid.CreateVersion7(),
        ReportId = reportId,
        PersonId = personId,
        MetricKey = ratio,
        Value = Math.Round(top.Value / bottom.Value, 2),
        Unit = "ratio",
        Flag = LabFlags.None,
        Confirmed = true,
        Derived = true,
        CreatedAt = now,
      });
    }
    return derived;
  }

  public static string NormaliseFlag(string? flag, string metricKey)
  {
    var f = flag?.Trim().ToLowerInvariant();
    return f switch
    {
      null or "" => LabFlags.None,
      "l" or "lo" or "low" or "↓" or "below" => LabFlags.Low,
      "h" or "hi" or "high" or "↑" or "above" => LabFlags.High,
      "n" or "normal" or "ok" or "optimal" or "in range" => LabFlags.Normal,
      "none" => LabFlags.None,
      _ => throw new DomainValidationException($"{metricKey}: flag must be low, high, normal or none."),
    };
  }

  private static (byte[]? Bytes, string? Sha256) DecodeImage(string? base64)
  {
    if (string.IsNullOrWhiteSpace(base64)) return (null, null);
    var text = base64.Trim();
    var comma = text.IndexOf(',');
    if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) text = text[(comma + 1)..];
    byte[] bytes;
    try
    {
      bytes = Convert.FromBase64String(text);
    }
    catch (FormatException)
    {
      throw new DomainValidationException("image must be base64.");
    }
    if (bytes.Length == 0) return (null, null);
    if (bytes.Length > MaxImageBytes) throw new DomainValidationException($"image is larger than {MaxImageBytes / (1024 * 1024)} MB.");
    return (bytes, TokenCrypto.Sha256Hex(bytes));
  }

  private string? Notes(LabReport report) => report.NotesCipher == null ? null : _encryption.TryDecrypt(CollectMapping.FromCipherBytes(report.NotesCipher));

  /// <summary>Noon on the collected day in the person's zone, so the sample lands on that calendar day everywhere.</summary>
  public static DateTime NoonLocal(DateOnly day, TimeZoneInfo zone)
  {
    var local = day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
    if (zone.IsInvalidTime(local)) local = local.AddHours(1);
    return TimeZoneInfo.ConvertTimeToUtc(local, zone);
  }

  public static string SampleUid(Guid resultId) => $"lab:{resultId:N}";

  private static Sample ToSample(LabResult result, Source source, DateTime at, DateTime now) => new()
  {
    Id = Guid.CreateVersion7(),
    PersonId = result.PersonId,
    SourceId = source.Id,
    SourceUid = SampleUid(result.Id),
    MetricKey = result.MetricKey,
    StartAt = at,
    Value = result.Value,
    Unit = result.Unit,
    Meta = result.Derived ? """{"derived":true}""" : null,
    CreatedAt = now,
  };

  private async Task<Source> GetOrCreateSystemSourceAsync(Guid personId, DateTime now, CancellationToken ct)
  {
    var existing = await _sources.FindActiveAsync(personId, SourceKinds.LabScan, LabSource.DeviceName, LabSource.Platform, ct);
    if (existing != null) return existing;
    return await _sources.AddAsync(new Source
    {
      Id = Guid.CreateVersion7(),
      PersonId = personId,
      Kind = SourceKinds.LabScan,
      DeviceName = LabSource.DeviceName,
      Platform = LabSource.Platform,
      Enabled = true,
      CreatedAt = now,
    }, ct);
  }

  /// <summary>Points per marker, newest first (reports arrive newest first).</summary>
  private static Dictionary<string, List<LabPointDto>> HistoryByMarker(IReadOnlyList<LabReport> reports)
  {
    var history = new Dictionary<string, List<LabPointDto>>(StringComparer.Ordinal);
    foreach (var report in reports)
    {
      foreach (var result in report.Results.Where(r => r.Confirmed))
      {
        if (!history.TryGetValue(result.MetricKey, out var list)) history[result.MetricKey] = list = [];
        list.Add(result.ToPoint(report.CollectedOn));
      }
    }
    return history;
  }

  private LabMarkerDto Marker(Metric metric, SexAtBirth sex, List<LabPointDto> points)
  {
    var reference = _references.Find(metric, sex);
    var latest = points.ElementAtOrDefault(0);
    var previous = points.ElementAtOrDefault(1);
    return new LabMarkerDto(
      metric.Key,
      metric.DisplayName,
      metric.Unit,
      LabPanel.GroupFor(metric.Key),
      metric.SexApplicability,
      LabReferenceLookup.IsApplicable(metric, sex),
      reference?.GoodDirection ?? metric.GoodDirection,
      latest?.Value,
      latest?.Date,
      latest?.Flag,
      previous?.Value,
      previous?.Date,
      LabMapping.PopulationRange(reference),
      LabMapping.OptimalRange(reference),
      reference == null || reference.Tier == LabTiers.None ? null : reference.Tier,
      latest != null && reference is { HasRange: true } ? LabFlagging.Position(reference, latest.Value) : null,
      latest == null ? null : LabFlagging.Assess(reference, latest.Value)?.Severity,
      points.Take(LastThree).ToList());
  }

  private static string FlagFor(LabResult? result, LabReference? reference)
  {
    if (result == null) return string.Empty;
    if (result.Flag is LabFlags.Low or LabFlags.High) return result.Flag == LabFlags.High ? "H" : "L";
    var assessment = LabFlagging.Assess(reference, result.Value);
    if (assessment is { OutOfRange: true }) return assessment.Position == LabPositions.Above ? "H" : "L";
    return string.Empty;
  }

  private static double Median(List<double> sorted) =>
    sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
