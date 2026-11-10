using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services.Body;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>
/// The household view (docs/labs-design.md, "Household overview"): each member with a short
/// summary per category. Every summary is built from a repository read that takes (viewer,
/// target) and checks its own category there, so a summary the caller may not see is null from
/// the repository and absent from the JSON. Nothing here decides access.
/// </summary>
public sealed class HouseholdOverviewService : IHouseholdOverviewService
{
  /// <summary>How far back the latest daily value of a summary metric is looked for.</summary>
  public const int LookbackDays = 14;

  /// <summary>Insight cards count over this many days, dismissed ones left out.</summary>
  public const int InsightDays = 7;

  private readonly IHouseholdRepository _households;
  private readonly IAccessPolicy _policy;
  private readonly ILabReportRepository _reports;
  private readonly ILabReferenceRepository _references;
  private readonly IDailySummaryRepository _summaries;
  private readonly IGoalRepository _goals;
  private readonly ITrialRepository _trials;
  private readonly IInsightRepository _insights;
  private readonly ICheckinRepository _checkins;
  private readonly IMetricCatalogue _catalogue;
  private readonly IEncryptionService _encryption;
  private readonly TimeProvider _clock;

  public HouseholdOverviewService(
    IHouseholdRepository households,
    IAccessPolicy policy,
    ILabReportRepository reports,
    ILabReferenceRepository references,
    IDailySummaryRepository summaries,
    IGoalRepository goals,
    ITrialRepository trials,
    IInsightRepository insights,
    ICheckinRepository checkins,
    IMetricCatalogue catalogue,
    IEncryptionService encryption,
    TimeProvider clock)
  {
    _households = households;
    _policy = policy;
    _reports = reports;
    _references = references;
    _summaries = summaries;
    _goals = goals;
    _trials = trials;
    _insights = insights;
    _checkins = checkins;
    _catalogue = catalogue;
    _encryption = encryption;
    _clock = clock;
  }

  public async Task<HouseholdOverviewDto?> GetAsync(Guid userId, Guid viewerPersonId, CancellationToken ct = default)
  {
    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    if (membership == null) return null;
    var members = await _households.GetMembersAsync(membership.HouseholdId, ct);
    var now = _clock.GetUtcNow().UtcDateTime;
    var viewer = members.FirstOrDefault(m => m.PersonId == viewerPersonId)?.Person;
    var day = DayBoundaries.LocalDay(now, DayBoundaries.Resolve(viewer?.TimeZone ?? "UTC"));

    var list = new List<HouseholdMemberOverviewDto>();
    foreach (var member in members.OrderBy(m => m.PersonId == viewerPersonId ? 0 : 1).ThenBy(m => m.JoinedAt))
    {
      list.Add(await MemberAsync(viewerPersonId, member.Person, now, ct));
    }
    return new HouseholdOverviewDto(membership.HouseholdId, day, list);
  }

  private async Task<HouseholdMemberOverviewDto> MemberAsync(Guid viewer, Person person, DateTime now, CancellationToken ct)
  {
    var target = person.Id;
    var today = DayBoundaries.LocalDay(now, DayBoundaries.Resolve(person.TimeZone));
    var shared = new List<string>();
    foreach (var category in Enum.GetValues<ShareCategory>())
    {
      if (viewer == target || await _policy.CanViewAsync(viewer, target, category, ct)) shared.Add(category.ToString().ToLowerInvariant());
    }

    return new HouseholdMemberOverviewDto(
      target,
      person.DisplayName,
      viewer == target,
      shared,
      await LabsAsync(viewer, person, today, ct),
      await BodyAsync(viewer, target, today, ct),
      await LatestAsync(viewer, target, MetricKeys.SleepStage, today, ct),
      await LatestAsync(viewer, target, MetricKeys.Steps, today, ct),
      await LatestAsync(viewer, target, MetricKeys.RestingHrBpm, today, ct),
      await InsightsAsync(viewer, target, today, ct),
      await CheckinsAsync(viewer, target, now, ct));
  }

  private async Task<OverviewLabsDto?> LabsAsync(Guid viewer, Person person, DateOnly today, CancellationToken ct)
  {
    var reports = await _reports.ListAsync(viewer, person.Id, ct);
    if (reports == null) return null;
    var latest = reports.FirstOrDefault();
    var outOfRange = 0;
    if (latest != null)
    {
      var age = LabReferenceLookup.AgeOn(person.BirthDate, latest.CollectedOn);
      foreach (var result in latest.Results.Where(r => r.Confirmed))
      {
        if (_catalogue.Find(result.MetricKey) is not { } metric) continue;
        var reference = LabReferenceLookup.ForAge(_references.Find(metric, person.SexAtBirth), age);
        if (LabFlagging.Assess(reference, result.Value) is { OutOfRange: true, Severity: not null }) outOfRange++;
      }
    }
    return new OverviewLabsDto(latest?.CollectedOn, reports.Count, outOfRange);
  }

  private async Task<OverviewBodyDto?> BodyAsync(Guid viewer, Guid target, DateOnly today, CancellationToken ct)
  {
    var goals = await _goals.ListAsync(viewer, target, includeEnded: false, ct);
    if (goals == null) return null;
    var weights = await _summaries.GetRangeAsync(viewer, target, MetricKeys.WeightKg, today.AddDays(-(LookbackDays * 2 - 1)), today, ct);
    var trials = await _trials.ListAsync(viewer, target, includeEnded: false, ct);
    var last = weights?.LastOrDefault();
    var main = goals.FirstOrDefault(g => g.Slot == GoalSlots.Main);
    return new OverviewBodyDto(
      last == null ? null : Math.Round(last.Value, 2),
      last?.Day,
      main == null ? null : BodyMapping.Title(main, _encryption),
      trials?.Count ?? 0);
  }

  private async Task<OverviewValueDto?> LatestAsync(Guid viewer, Guid target, string metricKey, DateOnly today, CancellationToken ct)
  {
    var rows = await _summaries.GetRangeAsync(viewer, target, metricKey, today.AddDays(-(LookbackDays - 1)), today, ct);
    if (rows == null) return null;
    var last = rows.LastOrDefault();
    var unit = BodyMapping.SummaryUnit(metricKey, _catalogue) ?? _catalogue.Find(metricKey)?.Unit ?? string.Empty;
    return new OverviewValueDto(metricKey, last?.Day, last == null ? null : Math.Round(last.Value, 2), unit);
  }

  private async Task<OverviewInsightsDto?> InsightsAsync(Guid viewer, Guid target, DateOnly today, CancellationToken ct)
  {
    var cards = await _insights.ListAsync(viewer, target, today.AddDays(-(InsightDays - 1)), today, includeDismissed: false, ct);
    if (cards == null) return null;
    var shown = cards.Where(c => c.Kind != InsightKinds.Learning).ToList();
    var top = shown.OrderByDescending(c => InsightSeverities.Rank(c.Severity)).FirstOrDefault()?.Severity;
    return new OverviewInsightsDto(shown.Count, top);
  }

  private async Task<OverviewCheckinsDto?> CheckinsAsync(Guid viewer, Guid target, DateTime now, CancellationToken ct)
  {
    var rows = await _checkins.ListAsync(viewer, target, now.AddDays(-30), now, 1, ct);
    if (rows == null) return null;
    var last = rows.FirstOrDefault();
    return new OverviewCheckinsDto(last?.At, last?.Mood);
  }
}
