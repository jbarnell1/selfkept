using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Formatting;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Interpret;

/// <summary>The archive, reactions, dismissals and on-demand recompute. Access is decided in the repositories.</summary>
public sealed class InsightService : IInsightService
{
  public const int DefaultDays = 30;
  public const int MaxDays = 366;

  private readonly IInsightRepository _insights;
  private readonly IPersonRepository _persons;
  private readonly IHouseholdRepository _households;
  private readonly IInterpretRunner _runner;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;

  public InsightService(IInsightRepository insights, IPersonRepository persons, IHouseholdRepository households, IInterpretRunner runner, IAuditService audit, TimeProvider clock)
  {
    _insights = insights;
    _persons = persons;
    _households = households;
    _runner = runner;
    _audit = audit;
    _clock = clock;
  }

  public async Task<IReadOnlyList<InsightDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, bool includeDismissed, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
    var end = to ?? today;
    var start = from ?? end.AddDays(-(DefaultDays - 1));
    if (start > end) throw new DomainValidationException("from must not be after to.");
    if (end.DayNumber - start.DayNumber >= MaxDays) throw new DomainValidationException($"An insights range covers at most {MaxDays} days.");

    var rows = await _insights.ListAsync(viewerPersonId, targetPersonId, start, end, includeDismissed, ct);
    return rows?.Select(i => i.ToDto()).ToList();
  }

  public async Task<InsightDto?> ReactAsync(Guid userId, Guid personId, Guid? householdId, Guid insightId, string reaction, RequestInfo info, CancellationToken ct = default)
  {
    var value = reaction?.Trim().ToLowerInvariant() ?? string.Empty;
    if (!InsightReactions.All.Contains(value)) throw new DomainValidationException($"reaction must be one of {string.Join(", ", InsightReactions.All)}.");

    var insight = await _insights.GetOwnAsync(personId, insightId, ct);
    if (insight == null) return null;
    insight.Reaction = value;
    await _insights.UpdateAsync(insight, ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.InsightReaction, userId, householdId, "insight", insight.Id.ToString(),
      new { kind = insight.Kind, reaction = value }, info.IpAddress), ct);
    return insight.ToDto();
  }

  public async Task<InsightDto?> DismissAsync(Guid userId, Guid personId, Guid? householdId, Guid insightId, RequestInfo info, CancellationToken ct = default)
  {
    var insight = await _insights.GetOwnAsync(personId, insightId, ct);
    if (insight == null) return null;
    if (insight.DismissedAt == null)
    {
      insight.DismissedAt = _clock.GetUtcNow().UtcDateTime;
      await _insights.UpdateAsync(insight, ct);
      await _audit.RecordAsync(new AuditEvent(
        AuditActions.InsightDismissed, userId, householdId, "insight", insight.Id.ToString(),
        new { kind = insight.Kind }, info.IpAddress), ct);
    }
    return insight.ToDto();
  }

  public async Task<RecomputeResultDto?> RecomputeAsync(Guid userId, Guid callerPersonId, Guid? householdId, bool isAdmin, RecomputeRequest request, RequestInfo info, CancellationToken ct = default)
  {
    var targetPersonId = request.PersonId ?? callerPersonId;
    if (targetPersonId != callerPersonId)
    {
      // Admin-or-self: someone else's data is recomputed only by the household admin, and only
      // for a member of that household. Anything else is 404, so nothing leaks.
      if (!isAdmin || householdId is not { } hid) return null;
      if (await _households.GetMemberByPersonAsync(hid, targetPersonId, ct) == null) return null;
    }

    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, zone);
    var day = request.Day ?? today.AddDays(-1);
    if (day > today) throw new DomainValidationException("day cannot be in the future.");

    var result = await _runner.RunForPersonAsync(targetPersonId, day, ct);
    if (result == null) return null;

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.InterpretRecompute, userId, householdId, "person", targetPersonId.ToString(),
      new { day = day.ToIso(), baselines = result.Baselines, insights = result.Insights }, info.IpAddress), ct);
    return result;
  }
}
