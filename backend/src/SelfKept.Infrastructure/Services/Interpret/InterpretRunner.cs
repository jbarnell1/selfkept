using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Interpret;

/// <summary>
/// One person, one day: compute and store the baselines, load the 28-day series, run the rules,
/// store each candidate as a card (created once per (person, kind, day), re-ranked on a rerun),
/// phrase the two that will lead Today, and drop the Today cache. Idempotent per (person, day).
/// </summary>
public sealed class InterpretRunner : IInterpretRunner
{
  private readonly IPersonRepository _persons;
  private readonly IBaselineService _baselineService;
  private readonly IBaselineRepository _baselines;
  private readonly IDailySummaryRepository _summaries;
  private readonly IInsightRepository _insights;
  private readonly IRuleEngine _engine;
  private readonly IInsightPhraser _phraser;
  private readonly IChatModel _model;
  private readonly ITodayCacheRepository _cache;
  private readonly ILabReportRepository _labReports;
  private readonly ILabReferenceRepository _labReferences;
  private readonly IMetricCatalogue _catalogue;
  private readonly TimeProvider _clock;
  private readonly ILogger<InterpretRunner> _logger;

  public InterpretRunner(
    IPersonRepository persons,
    IBaselineService baselineService,
    IBaselineRepository baselines,
    IDailySummaryRepository summaries,
    IInsightRepository insights,
    IRuleEngine engine,
    IInsightPhraser phraser,
    IChatModel model,
    ITodayCacheRepository cache,
    ILabReportRepository labReports,
    ILabReferenceRepository labReferences,
    IMetricCatalogue catalogue,
    TimeProvider clock,
    ILogger<InterpretRunner> logger)
  {
    _labReports = labReports;
    _labReferences = labReferences;
    _catalogue = catalogue;
    _persons = persons;
    _baselineService = baselineService;
    _baselines = baselines;
    _summaries = summaries;
    _insights = insights;
    _engine = engine;
    _phraser = phraser;
    _model = model;
    _cache = cache;
    _clock = clock;
    _logger = logger;
  }

  public async Task<RecomputeResultDto?> RunForPersonAsync(Guid personId, DateOnly day, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(personId, ct);
    if (person == null) return null;
    var now = _clock.GetUtcNow().UtcDateTime;

    var baselines = await _baselineService.ComputeAsync(personId, day, ct);
    if (baselines.Count > 0) await _baselines.UpsertManyAsync(baselines, ct);

    var from = day.AddDays(-(BaselineService.WindowDays - 1));
    var rows = await _summaries.GetOwnRangeAsync(personId, BaselineMetrics.Keys, from, day, ct);
    var series = rows
      .GroupBy(r => r.MetricKey, StringComparer.Ordinal)
      .ToDictionary(g => g.Key, g => (IReadOnlyList<DailySummary>)g.OrderBy(r => r.Day).ToList(), StringComparer.Ordinal);
    var labs = await LoadLabsAsync(person, ct);
    var context = new RuleContext(person, day, baselines.ToDictionary(b => b.MetricKey, StringComparer.Ordinal), series, labs);

    var candidates = _engine.Evaluate(context);
    var demoted = await DemotedKindsAsync(personId, day, candidates.Select(c => c.Kind), ct);
    var leading = candidates.Where(c => c.Kind != InsightKinds.Learning && !demoted.Contains(c.Kind)).Take(InsightRanking.TodayCardLimit).Select(c => c.Kind).ToHashSet(StringComparer.Ordinal);

    var kinds = new List<string>();
    foreach (var candidate in candidates)
    {
      var existing = await _insights.GetOwnByKindAsync(personId, candidate.Kind, day, ct);
      if (existing == null)
      {
        var insight = new Insight
        {
          Id = Guid.CreateVersion7(),
          PersonId = personId,
          Day = day,
          Kind = candidate.Kind,
          Severity = candidate.Severity,
          Metrics = candidate.Metrics.ToArray(),
          Evidence = InterpretMapping.EvidenceJson(candidate),
          Templated = candidate.Templated,
          CreatedAt = now,
        };
        if (leading.Contains(candidate.Kind) && _model.Enabled)
        {
          insight.Generated = await _phraser.PhraseAsync(personId, AiPurposes.Insight, candidate.Templated, ct);
          insight.Model = insight.Generated == null ? null : _model.ModelName;
        }
        await _insights.AddAsync(insight, ct);
      }
      else
      {
        // Re-rank, never duplicate: the numbers move, the reaction and dismissal stay.
        existing.Severity = candidate.Severity;
        existing.Metrics = candidate.Metrics.ToArray();
        existing.Evidence = InterpretMapping.EvidenceJson(candidate);
        if (existing.Templated != candidate.Templated)
        {
          existing.Templated = candidate.Templated;
          existing.Generated = null;
          existing.Model = null;
        }
        await _insights.UpdateAsync(existing, ct);
      }
      kinds.Add(candidate.Kind);
    }

    await _cache.InvalidateAsync(personId, ct);
    _logger.LogInformation("Interpreted {Day} for person {PersonId}: {Baselines} baselines, {Insights} insights ({Kinds})",
      day, personId, baselines.Count, kinds.Count, string.Join(",", kinds));
    return new RecomputeResultDto(personId, day, baselines.Count, kinds.Count, kinds);
  }

  /// <summary>The two most recent panels plus the reference rows for the person's sex, for the lab rules.</summary>
  private async Task<LabContext> LoadLabsAsync(Person person, CancellationToken ct)
  {
    var reports = await _labReports.ListOwnAsync(person.Id, 2, ct);
    if (reports.Count == 0) return LabContext.Empty;
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var panels = reports.Select(r => new LabPanelSnapshot(
        r.Id,
        r.CollectedOn,
        DayBoundaries.LocalDay(r.ConfirmedAt ?? r.CreatedAt, zone),
        r.Results.Where(x => x.Confirmed).GroupBy(x => x.MetricKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)))
      .ToList();
    return new LabContext(
      panels,
      key => _catalogue.Find(key) is { } metric ? _labReferences.Find(metric, person.SexAtBirth) : null,
      key => _catalogue.Find(key)?.DisplayName ?? key);
  }

  public async Task<bool> HasRunAsync(Guid personId, DateOnly day, CancellationToken ct = default) =>
    await _baselines.AnyForDayAsync(personId, day, ct) || await _insights.AnyForDayAsync(personId, day, ct);

  private async Task<HashSet<string>> DemotedKindsAsync(Guid personId, DateOnly day, IEnumerable<string> kinds, CancellationToken ct)
  {
    var demoted = new HashSet<string>(StringComparer.Ordinal);
    var since = day.AddDays(-InsightReactions.DemotionWindowDays);
    foreach (var kind in kinds.Distinct())
    {
      if (await _insights.CountReactionsAsync(personId, kind, InsightReactions.Wrong, since, ct) >= InsightReactions.DemotionCount) demoted.Add(kind);
    }
    return demoted;
  }
}
