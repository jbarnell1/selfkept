using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// Daily and weekly targets, and the tally Today and Progress show (docs/body-design.md, "Targets
/// and the tally"). The tally reads every input under the viewer's own grants, so a target built
/// on something the viewer may not see comes back unknown instead of leaking a yes or a no.
/// </summary>
public sealed class TargetService : ITargetService
{
  public const int MaxTargets = 12;
  public const int MaxLabelLength = 48;
  private const int JournalReadLimit = 2000;

  private readonly IDailyTargetRepository _targets;
  private readonly IGoalRepository _goals;
  private readonly IJournalRepository _journal;
  private readonly IStrengthRepository _strength;
  private readonly IDailySummaryRepository _summaries;
  private readonly IPersonRepository _persons;
  private readonly IMetricCatalogue _catalogue;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly ITodayCacheRepository _todayCache;
  private readonly TimeProvider _clock;

  public TargetService(
    IDailyTargetRepository targets,
    IGoalRepository goals,
    IJournalRepository journal,
    IStrengthRepository strength,
    IDailySummaryRepository summaries,
    IPersonRepository persons,
    IMetricCatalogue catalogue,
    IEncryptionService encryption,
    IAuditService audit,
    ITodayCacheRepository todayCache,
    TimeProvider clock)
  {
    _targets = targets;
    _goals = goals;
    _journal = journal;
    _strength = strength;
    _summaries = summaries;
    _persons = persons;
    _catalogue = catalogue;
    _encryption = encryption;
    _audit = audit;
    _todayCache = todayCache;
    _clock = clock;
  }

  public async Task<IReadOnlyList<TargetDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var targets = await _targets.ListAsync(viewerPersonId, targetPersonId, ct);
    return targets?.Select(t => t.ToDto(_catalogue)).ToList();
  }

  public async Task<TargetDto> CreateAsync(Actor actor, SaveTargetRequest request, CancellationToken ct = default)
  {
    var label = await ValidateAsync(actor.PersonId, request, ct);
    var existing = await _targets.ListOwnAsync(actor.PersonId, ct);
    if (existing.Count >= MaxTargets) throw new DomainValidationException($"At most {MaxTargets} targets. Remove one first.");

    var now = _clock.GetUtcNow().UtcDateTime;
    var target = new DailyTarget
    {
      Id = Guid.CreateVersion7(),
      PersonId = actor.PersonId,
      CreatedAt = now,
    };
    Apply(target, request, label, request.SortOrder ?? existing.Count, now);
    await _targets.AddAsync(target, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);

    await RecordAsync(AuditActions.TargetSaved, actor, target, ct);
    return target.ToDto(_catalogue);
  }

  public async Task<TargetDto?> UpdateAsync(Actor actor, Guid targetId, SaveTargetRequest request, CancellationToken ct = default)
  {
    var target = await _targets.GetOwnAsync(actor.PersonId, targetId, ct);
    if (target == null) return null;
    var label = await ValidateAsync(actor.PersonId, request, ct);

    Apply(target, request, label, request.SortOrder ?? target.SortOrder, _clock.GetUtcNow().UtcDateTime);
    await _targets.UpdateAsync(target, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);

    await RecordAsync(AuditActions.TargetSaved, actor, target, ct);
    return target.ToDto(_catalogue);
  }

  public async Task<bool> DeleteAsync(Actor actor, Guid targetId, CancellationToken ct = default)
  {
    var target = await _targets.GetOwnAsync(actor.PersonId, targetId, ct);
    if (target == null) return false;
    await _targets.RemoveAsync(target, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);
    await RecordAsync(AuditActions.TargetDeleted, actor, target, ct);
    return true;
  }

  public async Task<GoalTallyDto?> TallyAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var targets = await _targets.ListAsync(viewerPersonId, targetPersonId, ct);
    if (targets == null) return null;
    var goals = await _goals.ListAsync(viewerPersonId, targetPersonId, includeEnded: false, ct);
    if (goals == null) return null;

    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person?.TimeZone));
    var active = targets.Where(t => t.Active).ToList();
    var data = await ReadAsync(viewerPersonId, targetPersonId, active, today, ct);

    return new GoalTallyDto(
      today,
      goals.Select(g => g.ToDto(_encryption)).ToList(),
      active.Select(t => TargetTally.Compute(t, t.ToDto(_catalogue), today, data)).ToList());
  }

  /// <summary>Reads what the active targets need for the last seven days, each under the viewer's grant for it.</summary>
  private async Task<TallyData> ReadAsync(Guid viewerPersonId, Guid targetPersonId, IReadOnlyList<DailyTarget> active, DateOnly today, CancellationToken ct)
  {
    var from = today.AddDays(-(TargetTally.WeekDays - 1));
    var metrics = new Dictionary<string, IReadOnlyDictionary<DateOnly, double>>(StringComparer.Ordinal);
    foreach (var key in active.Where(t => t.Kind == TargetKinds.Metric && t.MetricKey != null).Select(t => t.MetricKey!).Distinct(StringComparer.Ordinal))
    {
      var rows = await _summaries.GetRangeAsync(viewerPersonId, targetPersonId, key, from, today, ct);
      if (rows != null) metrics[key] = rows.ToDictionary(r => r.Day, r => r.Value);
    }
    var journal = active.Any(t => t.Kind == TargetKinds.Journal)
      ? await _journal.ListAsync(viewerPersonId, targetPersonId, from, today, JournalReadLimit, ct)
      : null;
    var strength = active.Any(t => t.Kind == TargetKinds.Strength)
      ? await _strength.ListAsync(viewerPersonId, targetPersonId, from, today, ct)
      : null;
    return new TallyData(metrics, journal, strength);
  }

  /// <summary>Checks the request and returns the cleaned label.</summary>
  private async Task<string> ValidateAsync(Guid personId, SaveTargetRequest request, CancellationToken ct)
  {
    var label = request.Label?.Trim() ?? string.Empty;
    if (label.Length is 0 or > MaxLabelLength) throw new DomainValidationException($"A label is 1 to {MaxLabelLength} characters.");
    if (!TargetKinds.All.Contains(request.Kind)) throw new DomainValidationException($"kind must be one of: {string.Join(", ", TargetKinds.All)}.");
    if (!TargetComparators.All.Contains(request.Comparator)) throw new DomainValidationException($"comparator must be one of: {string.Join(", ", TargetComparators.All)}.");
    if (!TargetPeriods.All.Contains(request.Period)) throw new DomainValidationException($"period must be one of: {string.Join(", ", TargetPeriods.All)}.");
    if (!double.IsFinite(request.Value) || request.Value < 0) throw new DomainValidationException("value must be zero or more.");

    switch (request.Kind)
    {
      case TargetKinds.Metric:
        if (request.Tag != null) throw new DomainValidationException("A metric target has no tag.");
        if (request.MetricKey == null || _catalogue.Find(request.MetricKey) is not { } metric)
        {
          throw new DomainValidationException($"Unknown metric '{request.MetricKey}'.");
        }
        if (metric.IsLab || !DailySummaryRules.HasRule(metric.Key))
        {
          throw new DomainValidationException($"'{metric.Key}' has no daily figure to set a target on.");
        }
        break;
      case TargetKinds.Journal:
        if (request.MetricKey != null) throw new DomainValidationException("A day log target has no metric.");
        if (request.Tag == null || !JournalTags.All.Contains(request.Tag)) throw new DomainValidationException($"Unknown tag '{request.Tag}'.");
        break;
      default:
        if (request.MetricKey != null || request.Tag != null) throw new DomainValidationException("A strength target has no metric or tag.");
        break;
    }

    if (request.GoalId is { } goalId && await _goals.GetOwnAsync(personId, goalId, ct) == null)
    {
      throw new DomainValidationException("No such goal.");
    }
    return label;
  }

  private static void Apply(DailyTarget target, SaveTargetRequest request, string label, int sortOrder, DateTime now)
  {
    target.GoalId = request.GoalId;
    target.Label = label;
    target.Kind = request.Kind;
    target.MetricKey = request.MetricKey;
    target.Tag = request.Tag;
    target.Comparator = request.Comparator;
    target.Value = request.Value;
    target.Period = request.Period;
    target.Active = request.Active ?? true;
    target.SortOrder = (short)Math.Clamp(sortOrder, 0, short.MaxValue);
    target.UpdatedAt = now;
  }

  private Task RecordAsync(string action, Actor actor, DailyTarget target, CancellationToken ct) =>
    _audit.RecordAsync(new AuditEvent(
      action, actor.UserId, actor.HouseholdId, "daily_target", target.Id.ToString(),
      new { kind = target.Kind, period = target.Period }, actor.Info.IpAddress), ct);
}
