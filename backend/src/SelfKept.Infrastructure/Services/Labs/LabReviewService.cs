using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services.Body;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>
/// The lab review, the timeline and the cross-reference reads (docs/labs-design.md, "API"). Every
/// read goes through a repository that checks the grant for its own category: labs for panels and
/// the medicines list, body for weight and trials, each metric's own category for related
/// readings. A missing secondary grant leaves that part out; it never hides the labs.
/// </summary>
public sealed class LabReviewService : ILabReviewService
{
  /// <summary>Weight beside a draw: the weigh-in nearest the draw within this many days either side.</summary>
  public const int WeightWindowDays = 7;

  /// <summary>How far back a related wearable reading's latest value is looked for.</summary>
  public const int RelatedLookbackDays = 90;

  private readonly ILabReportRepository _reports;
  private readonly IMedicationRepository _medications;
  private readonly IDailySummaryRepository _summaries;
  private readonly ITrialRepository _trials;
  private readonly IPersonRepository _persons;
  private readonly ILabReferenceRepository _references;
  private readonly IMetricCatalogue _catalogue;
  private readonly IEncryptionService _encryption;
  private readonly TimeProvider _clock;

  public LabReviewService(
    ILabReportRepository reports,
    IMedicationRepository medications,
    IDailySummaryRepository summaries,
    ITrialRepository trials,
    IPersonRepository persons,
    ILabReferenceRepository references,
    IMetricCatalogue catalogue,
    IEncryptionService encryption,
    TimeProvider clock)
  {
    _reports = reports;
    _medications = medications;
    _summaries = summaries;
    _trials = trials;
    _persons = persons;
    _references = references;
    _catalogue = catalogue;
    _encryption = encryption;
    _clock = clock;
  }

  public async Task<LabReviewDto?> GetReviewAsync(Guid viewerPersonId, Guid reportId, CancellationToken ct = default)
  {
    var report = await _reports.GetAsync(viewerPersonId, reportId, ct);
    if (report == null) return null;
    var personId = report.PersonId;
    var person = await _persons.GetByIdAsync(personId, ct);
    var all = await _reports.ListAsync(viewerPersonId, personId, ct);
    if (person == null || all == null) return null;

    var previous = all
      .Where(r => r.Id != report.Id && (r.CollectedOn < report.CollectedOn || (r.CollectedOn == report.CollectedOn && r.CreatedAt < report.CreatedAt)))
      .OrderByDescending(r => r.CollectedOn).ThenByDescending(r => r.CreatedAt)
      .FirstOrDefault();

    var medications = await _medications.ListAsync(viewerPersonId, personId, ct) ?? [];
    var latestDraw = Draw(report, medications);
    var previousDraw = previous == null ? DrawContext.Unknown : Draw(previous, medications);
    var age = LabReferenceLookup.AgeOn(person.BirthDate, report.CollectedOn);

    var judgements = new List<MarkerJudgement>();
    var markers = new List<LabReviewMarkerDto>();
    foreach (var result in report.Results.Where(r => r.Confirmed).OrderBy(r => LabPanel.OrderOf(r.MetricKey)))
    {
      var metric = _catalogue.Find(result.MetricKey);
      if (metric == null) continue;
      var reference = LabReferenceLookup.ForAge(_references.Find(metric, person.SexAtBirth), age);
      var earlier = previous?.Results.FirstOrDefault(r => r.Confirmed && r.MetricKey == result.MetricKey);
      var j = LabReview.Judge(result.MetricKey, result.Value, earlier?.Value, reference, metric.GoodDirection, person.SexAtBirth, latestDraw, previousDraw);
      judgements.Add(j);
      markers.Add(new LabReviewMarkerDto(
        result.MetricKey, metric.DisplayName, result.Unit, result.Value, j.PreviousValue, j.Change, j.ChangePercent, j.RcvPercent, j.BeyondRcv,
        j.Moved, j.Wanted, j.Verdict, j.Reasons,
        j.ReadWithCare.Select(l => new ReadWithCareDto(l.Id, l.From, l.Explanation, l.Source)).ToList(),
        j.Variation == null ? null : new VariationSourceDto(j.Variation.CviPercent, j.Variation.Source, j.Variation.SourceUrl, j.Variation.Caveat),
        LabReview.Prep(result.MetricKey, latestDraw),
        LabReview.Retest(result.MetricKey, result.Value, latestDraw)));
    }

    var weight = await WeightPairAsync(viewerPersonId, personId, report.CollectedOn, previous?.CollectedOn, ct);
    var trials = await TrialsAsync(viewerPersonId, personId, report, previous, judgements, ct);

    return new LabReviewDto(
      report.Id,
      personId,
      report.CollectedOn,
      previous?.Id,
      previous?.CollectedOn,
      DrawConditions.ToDto(report),
      previous == null ? null : DrawConditions.ToDto(previous),
      previous == null ? [] : LabReview.Differences(latestDraw, previousDraw).Select(d => new ConditionDifferenceDto(d.Condition, d.Now, d.Then)).ToList(),
      markers,
      Taking(medications, report.CollectedOn),
      previous == null ? [] : Taking(medications, previous.CollectedOn),
      latestDraw.Substances.OrderBy(s => s, StringComparer.Ordinal).ToList(),
      previousDraw.Substances.OrderBy(s => s, StringComparer.Ordinal).ToList(),
      weight,
      trials,
      LabVerdicts.All.ToDictionary(v => v, v => judgements.Count(j => j.Verdict == v)));
  }

  public async Task<LabTimelineDto?> GetTimelineAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var reports = await _reports.ListAsync(viewerPersonId, targetPersonId, ct);
    if (reports == null) return null;
    var medications = await _medications.ListAsync(viewerPersonId, targetPersonId, ct) ?? [];
    var ordered = reports.OrderBy(r => r.CollectedOn).ThenBy(r => r.CreatedAt).ToList();

    IReadOnlyList<DailySummary>? weights = null;
    var shared = await CanSeeAsync(viewerPersonId, targetPersonId, MetricKeys.WeightKg, ct);
    if (shared && ordered.Count > 0)
    {
      weights = await _summaries.GetRangeAsync(viewerPersonId, targetPersonId, MetricKeys.WeightKg,
        ordered[0].CollectedOn.AddDays(-WeightWindowDays), ordered[^1].CollectedOn.AddDays(WeightWindowDays), ct);
    }

    var panels = ordered.Select(r => new LabTimelinePanelDto(
        r.Id,
        r.CollectedOn,
        r.Provider,
        DrawConditions.ToDto(r),
        r.Results.Count(x => x.Confirmed),
        weights == null ? null : Nearest(weights, r.CollectedOn),
        Substances(medications, r.CollectedOn).OrderBy(s => s, StringComparer.Ordinal).ToList()))
      .ToList();
    return new LabTimelineDto(targetPersonId, shared, panels);
  }

  public IReadOnlyList<LabLinkDto> GetLinks() => LabLinks.All.Select(ToDto).ToList();

  public async Task<RelatedReadingsDto?> GetRelatedAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, CancellationToken ct = default)
  {
    var key = metricKey.Trim();
    if (!key.StartsWith(MetricKeys.LabPrefix, StringComparison.Ordinal)) key = MetricKeys.LabPrefix + key;
    var metric = _catalogue.Find(key);
    if (metric is not { IsLab: true }) return null;
    var reports = await _reports.ListAsync(viewerPersonId, targetPersonId, ct);
    if (reports == null) return null;
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));

    var related = new List<RelatedReadingDto>();
    foreach (var group in LabLinks.For(metric.Key).GroupBy(l => l.From == metric.Key ? l.To : l.From))
    {
      var otherKey = group.Key;
      var links = group.Select(ToDto).ToList();
      if (otherKey.StartsWith(LabLinks.SubstancePrefix, StringComparison.Ordinal))
      {
        var tag = otherKey[LabLinks.SubstancePrefix.Length..];
        related.Add(new RelatedReadingDto(otherKey, "substance", tag.Replace('_', ' '), null, "labs", true, null, null, links));
        continue;
      }
      if (otherKey.StartsWith(DrawConditionKeys.Prefix, StringComparison.Ordinal))
      {
        var name = otherKey == DrawConditionKeys.NotFasting ? "Not fasting at the draw" : "Drawn after the morning";
        related.Add(new RelatedReadingDto(otherKey, "condition", name, null, "labs", true, null, null, links));
        continue;
      }
      var other = _catalogue.Find(otherKey);
      if (other == null) continue;
      var category = other.Category.ToString().ToLowerInvariant();
      if (other.IsLab)
      {
        // Labs were granted to get this far.
        var latest = reports.SelectMany(r => r.Results.Where(x => x.Confirmed && x.MetricKey == other.Key).Select(x => (r.CollectedOn, x.Value))).OrderByDescending(p => p.CollectedOn).FirstOrDefault();
        related.Add(new RelatedReadingDto(other.Key, "lab", other.DisplayName, other.Unit, category, true,
          latest == default ? null : latest.Value, latest == default ? null : latest.CollectedOn, links));
      }
      else
      {
        var rows = await _summaries.GetRangeAsync(viewerPersonId, targetPersonId, other.Key, today.AddDays(-RelatedLookbackDays), today, ct);
        var last = rows?.LastOrDefault();
        related.Add(new RelatedReadingDto(other.Key, "metric", other.DisplayName, BodyMapping.SummaryUnit(other.Key, _catalogue), category, rows != null,
          last?.Value, last?.Day, links));
      }
    }
    return new RelatedReadingsDto(metric.Key, metric.DisplayName, related);
  }

  // --- building blocks ---------------------------------------------------------------------

  public static LabLinkDto ToDto(LabLink l) => new(l.Id, l.From, l.To, l.Direction, l.Explanation, l.Source, l.SourceUrl, l.Evidence);

  private static DrawContext Draw(LabReport report, IReadOnlyList<Medication> medications) =>
    new(DrawConditions.Of(report), Substances(medications, report.CollectedOn));

  public static HashSet<string> Substances(IReadOnlyList<Medication> medications, DateOnly day) =>
    medications.Where(m => m.ActiveOn(day)).SelectMany(m => m.Substances).ToHashSet(StringComparer.Ordinal);

  private List<TakenItemDto> Taking(IReadOnlyList<Medication> medications, DateOnly day) =>
    medications.Where(m => m.ActiveOn(day))
      .Select(m =>
      {
        var detail = BodyMapping.ReadDetail(m, _encryption);
        return new TakenItemDto(m.Id, m.Kind, detail.Brand, detail.Product, m.Substances);
      })
      .OrderBy(t => t.Kind, StringComparer.Ordinal).ThenBy(t => t.Product, StringComparer.OrdinalIgnoreCase)
      .ToList();

  /// <summary>The weigh-in nearest the day within the window; the earlier one on a tie.</summary>
  public static WeightAtDrawDto? Nearest(IReadOnlyList<DailySummary> rows, DateOnly day)
  {
    var best = rows
      .Where(r => Math.Abs(r.Day.DayNumber - day.DayNumber) <= WeightWindowDays)
      .OrderBy(r => Math.Abs(r.Day.DayNumber - day.DayNumber)).ThenBy(r => r.Day)
      .FirstOrDefault();
    return best == null ? null : new WeightAtDrawDto(best.Day, Math.Round(best.Value, 2));
  }

  private async Task<bool> CanSeeAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, CancellationToken ct)
  {
    // The repository decides: a one-day read that comes back null means no grant for the category.
    var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
    return await _summaries.GetRangeAsync(viewerPersonId, targetPersonId, metricKey, today, today, ct) != null;
  }

  private async Task<LabReviewWeightDto?> WeightPairAsync(Guid viewerPersonId, Guid personId, DateOnly now, DateOnly? then, CancellationToken ct)
  {
    var from = (then ?? now).AddDays(-WeightWindowDays);
    var rows = await _summaries.GetRangeAsync(viewerPersonId, personId, MetricKeys.WeightKg, from, now.AddDays(WeightWindowDays), ct);
    if (rows == null) return null;
    var a = Nearest(rows, now);
    var b = then is { } t ? Nearest(rows, t) : null;
    return new LabReviewWeightDto(a, b, a != null && b != null ? Math.Round(a.Kg - b.Kg, 2) : null);
  }

  private async Task<IReadOnlyList<LabReviewTrialDto>?> TrialsAsync(Guid viewerPersonId, Guid personId, LabReport report, LabReport? previous, IReadOnlyList<MarkerJudgement> judgements, CancellationToken ct)
  {
    var trials = await _trials.ListAsync(viewerPersonId, personId, includeEnded: true, ct);
    if (trials == null) return null;
    if (previous == null) return [];

    var inWindow = trials
      .Where(t => t.HasLabMarkers && t.StartedOn > previous.CollectedOn && t.StartedOn <= report.CollectedOn)
      .OrderBy(t => t.StartedOn)
      .ToList();
    var byKey = judgements.ToDictionary(j => j.MetricKey, StringComparer.Ordinal);
    return inWindow.Select(t =>
    {
      var markers = t.MetricKeys.Where(byKey.ContainsKey).Select(k => new LabReviewTrialMarkerDto(k, byKey[k].Verdict)).ToList();
      var shared = inWindow.Any(o => o.Id != t.Id && o.MetricKeys.Intersect(t.MetricKeys).Any());
      return new LabReviewTrialDto(t.ToDto(_encryption), markers, shared);
    }).ToList();
  }
}
