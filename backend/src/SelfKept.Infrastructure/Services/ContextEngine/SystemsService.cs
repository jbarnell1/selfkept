using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;
using SelfKept.Infrastructure.Services.Interpret;

namespace SelfKept.Infrastructure.Services.ContextEngine;

/// <summary>One body system: the wearable metrics and lab markers that tell about it.</summary>
public sealed record BodySystem(string Key, string Name, IReadOnlyList<string> Metrics, IReadOnlyList<string> Labs);

/// <summary>
/// The Systems tab (docs/context-design.md, "Systems"): metrics grouped by what they tell, with lab
/// results beside the wearable readings and the cards that concern them. Each reading is read under
/// its own category, lab results under labs and cards under insights, so a viewer sees only what
/// was shared; a system with nothing the viewer may see is left out.
/// </summary>
public sealed class SystemsService : ISystemsService
{
  /// <summary>How far back a reading may be and still count as the latest: weigh-ins and VO2 max are sparse.</summary>
  public const int LatestLookbackDays = 120;

  public const int CardDays = 7;
  public const int DetailCardDays = 30;
  public const int LabHistory = 6;

  public static readonly IReadOnlyList<BodySystem> All =
  [
    new("heart", "Heart and fitness",
      [MetricKeys.RestingHrBpm, MetricKeys.HrvRmssdMs, MetricKeys.HrvSdnnMs, MetricKeys.Vo2Max, MetricKeys.RespiratoryRateBpm, MetricKeys.Spo2Pct, "blood_pressure_sys", "blood_pressure_dia", MetricKeys.Steps, MetricKeys.ExerciseMin],
      ["lab.apob", "lab.ldl", "lab.hdl", "lab.ldl_apob_ratio", "lab.total_cholesterol", "lab.remnant_cholesterol", "lab.tc_hdl_ratio", "lab.hscrp"]),
    new("sleep", "Sleep",
      [MetricKeys.SleepStage, MetricKeys.OuraSleepScore, MetricKeys.WristTempDeltaC],
      []),
    new("metabolism", "Metabolism",
      [MetricKeys.BloodGlucoseMmol, MetricKeys.ActiveEnergyKcal, MetricKeys.EnergyIntakeKcal, MetricKeys.ProteinG],
      ["lab.triglycerides", "lab.tg_hdl_ratio", "lab.fructosamine", "lab.uric_acid", "lab.albumin", "lab.alp", "lab.ggt", "lab.creatinine", "lab.egfr"]),
    new("hormones", "Hormones",
      [],
      ["lab.total_testosterone", "lab.free_testosterone", "lab.estradiol", "lab.progesterone", "lab.shbg", "lab.cortisol", "lab.tsh", "lab.free_t3", "lab.free_t4"]),
    new("body", "Body",
      [MetricKeys.WeightKg, MetricKeys.BodyFatPct, MetricKeys.WaistCm, MetricKeys.LeanMassKg],
      ["lab.vitamin_d", "lab.ferritin", "lab.hemoglobin", "lab.hematocrit"]),
    new("cycle", "Cycle",
      ["menstrual_flow", "basal_body_temp_c"],
      []),
  ];

  private readonly IReadBackRepository _readBack;
  private readonly IBaselineRepository _baselines;
  private readonly IDailySummaryRepository _summaries;
  private readonly ILabReportRepository _labs;
  private readonly IInsightRepository _insights;
  private readonly IPersonRepository _persons;
  private readonly IMetricCatalogue _catalogue;
  private readonly TimeProvider _clock;

  public SystemsService(
    IReadBackRepository readBack,
    IBaselineRepository baselines,
    IDailySummaryRepository summaries,
    ILabReportRepository labs,
    IInsightRepository insights,
    IPersonRepository persons,
    IMetricCatalogue catalogue,
    TimeProvider clock)
  {
    _readBack = readBack;
    _baselines = baselines;
    _summaries = summaries;
    _labs = labs;
    _insights = insights;
    _persons = persons;
    _catalogue = catalogue;
    _clock = clock;
  }

  public async Task<SystemsDto?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var read = await ReadAsync(viewerPersonId, targetPersonId, detail: false, ct);
    if (read == null) return null;
    var systems = new List<SystemDto>();
    foreach (var system in All)
    {
      if (await BuildAsync(viewerPersonId, targetPersonId, system, read, detail: false, ct) is { } dto) systems.Add(dto);
    }
    return new SystemsDto(read.Today, systems, LabsEntry(read));
  }

  public async Task<SystemDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, string key, CancellationToken ct = default)
  {
    var system = All.FirstOrDefault(s => s.Key == key.Trim().ToLowerInvariant());
    if (system == null) return null;
    var read = await ReadAsync(viewerPersonId, targetPersonId, detail: true, ct);
    if (read == null) return null;
    return await BuildAsync(viewerPersonId, targetPersonId, system, read, detail: true, ct);
  }

  private sealed record Read(
    DateOnly Today,
    IReadOnlySet<ShareCategory> Allowed,
    IReadOnlyDictionary<string, Baseline> Baselines,
    IReadOnlyList<LabReport>? Reports,
    IReadOnlyList<Insight>? Insights);

  private async Task<Read?> ReadAsync(Guid viewer, Guid target, bool detail, CancellationToken ct)
  {
    var person = await _persons.GetByIdAsync(target, ct);
    if (person == null) return null;
    var allowed = await _readBack.AllowedCategoriesAsync(viewer, target, ct);
    if (allowed.Count == 0) return null;
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
    var baselines = (await _baselines.GetLatestPerMetricAsync(viewer, target, ct) ?? []).ToDictionary(b => b.MetricKey, StringComparer.Ordinal);
    var reports = await _labs.ListAsync(viewer, target, ct);
    var insights = await _insights.ListAsync(viewer, target, today.AddDays(-((detail ? DetailCardDays : CardDays) - 1)), today, includeDismissed: false, ct);
    return new Read(today, allowed, baselines, reports, insights);
  }

  private async Task<SystemDto?> BuildAsync(Guid viewer, Guid target, BodySystem system, Read read, bool detail, CancellationToken ct)
  {
    var metrics = system.Metrics.Select(_catalogue.Find).OfType<Metric>().ToList();
    var visibleMetrics = metrics.Where(m => read.Allowed.Contains(m.Category)).ToList();
    var labsVisible = system.Labs.Count > 0 && read.Reports != null;
    var keys = system.Metrics.Concat(system.Labs).ToHashSet(StringComparer.Ordinal);
    var cards = (read.Insights ?? []).Where(i => i.Metrics.Any(keys.Contains)).ToList();
    if (visibleMetrics.Count == 0 && !labsVisible && cards.Count == 0) return null;

    var readings = new List<SystemReadingDto>();
    foreach (var metric in visibleMetrics)
    {
      var rows = await _summaries.GetRangeAsync(viewer, target, metric.Key, read.Today.AddDays(-(LatestLookbackDays - 1)), read.Today, ct);
      if (rows == null) continue;
      read.Baselines.TryGetValue(metric.Key, out var baseline);
      if (rows.Count == 0 && baseline == null) continue;
      var latest = rows.Count > 0 ? rows[^1] : null;
      var usual = baseline is { IsEstablished: true, Median28: { } median, P25_28: { } p25, P75_28: { } p75 } ? new SystemUsualDto(median, p25, p75, baseline.N28) : null;
      readings.Add(new SystemReadingDto(
        metric.Key,
        MetricNames.For(metric.Key),
        TrendService.UnitFor(metric),
        metric.Category.ToString().ToLowerInvariant(),
        latest?.Day,
        latest?.Value,
        usual,
        latest != null && usual != null ? BandPositions.Of(latest.Value, usual.P25, usual.P75) : null,
        detail ? rows.Where(r => r.Day > read.Today.AddDays(-BaselineService.WindowDays)).Select(r => r.ToTrendPoint()).ToList() : null));
    }

    var labs = labsVisible ? Labs(read.Reports!, system.Labs, detail) : [];
    var cardDtos = cards.Select(i => i.ToDto()).ToList();
    return new SystemDto(system.Key, system.Name, State(readings, labs, cardDtos), readings, labs, cardDtos);
  }

  /// <summary>Latest and previous confirmed value per marker, newest panel first; with history on the detail page.</summary>
  public IReadOnlyList<SystemLabDto> Labs(IReadOnlyList<LabReport> reports, IReadOnlyList<string> markers, bool detail)
  {
    var results = reports
      .SelectMany(r => r.Results.Where(x => x.Confirmed).Select(x => (r.CollectedOn, Result: x)))
      .Where(x => markers.Contains(x.Result.MetricKey))
      .GroupBy(x => x.Result.MetricKey, StringComparer.Ordinal)
      .Select(g =>
      {
        var ordered = g.OrderByDescending(x => x.CollectedOn).ToList();
        var latest = ordered[0];
        var previous = ordered.Count > 1 ? ordered[1] : ((DateOnly, LabResult)?)null;
        return new SystemLabDto(
          g.Key,
          _catalogue.Find(g.Key)?.DisplayName ?? g.Key,
          latest.Result.Unit,
          latest.Result.Value,
          latest.CollectedOn,
          latest.Result.Flag,
          previous?.Item2.Value,
          previous?.Item1,
          detail ? ordered.Take(LabHistory).Reverse().Select(x => new SystemLabPointDto(x.CollectedOn, x.Result.Value, x.Result.Flag)).ToList() : null);
      })
      .OrderBy(l => markers.ToList().IndexOf(l.Metric))
      .ToList();
    return results;
  }

  /// <summary>
  /// The one line under a system's name, built by code: cards worth a look, then the first reading
  /// outside its usual range, then a lab flag, else that readings sit inside the usual range.
  /// </summary>
  public static string State(IReadOnlyList<SystemReadingDto> readings, IReadOnlyList<SystemLabDto> labs, IReadOnlyList<InsightDto> cards)
  {
    var parts = new List<string>();
    var serious = cards.Count(c => c.Severity is InsightSeverities.Watch or InsightSeverities.Clinician);
    if (serious > 0) parts.Add(serious == 1 ? "One card worth a look." : $"{serious} cards worth a look.");

    var outside = readings.Where(r => r.Band is BandPositions.Above or BandPositions.Below).ToList();
    var flagged = labs.Where(l => l.Flag is LabFlags.High or LabFlags.Low).ToList();
    if (outside.Count > 0)
    {
      var first = outside[0];
      var more = outside.Count > 1 ? $" {outside.Count - 1} more reading{(outside.Count > 2 ? "s are" : " is")} outside it too." : string.Empty;
      parts.Add($"{first.DisplayName} is {first.Band} your usual range.{more}");
    }
    else if (flagged.Count > 0)
    {
      parts.Add($"{flagged[0].DisplayName} came back {flagged[0].Flag} on {Interpret.Rules.LabRules.Spoken(flagged[0].CollectedOn)}.");
    }
    else if (readings.Any(r => r.Band != null))
    {
      parts.Add("Readings are inside your usual range.");
    }
    else if (readings.Any(r => r.LatestValue != null) || labs.Count > 0)
    {
      parts.Add("Not enough days yet for a usual range.");
    }
    else
    {
      parts.Add("No readings yet.");
    }
    return string.Join(" ", parts);
  }

  private static LabsEntryDto? LabsEntry(Read read)
  {
    if (read.Reports == null) return null;
    var latest = read.Reports.OrderByDescending(r => r.CollectedOn).FirstOrDefault();
    var confirmed = latest?.Results.Where(r => r.Confirmed).ToList() ?? [];
    return new LabsEntryDto(latest?.CollectedOn, read.Reports.Count, confirmed.Count, confirmed.Count(r => r.Flag is LabFlags.High or LabFlags.Low));
  }
}
