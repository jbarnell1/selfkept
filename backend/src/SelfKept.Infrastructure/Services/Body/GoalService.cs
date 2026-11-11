using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// Goals (docs/body-design.md, "Goals"): one main and one secondary at a time. Starting a goal in
/// a slot that already has one ends the old one as dropped, so the history shows what was tried.
/// </summary>
public sealed class GoalService : IGoalService
{
  public const int MaxTitleLength = 80;

  private readonly IGoalRepository _goals;
  private readonly IMetricCatalogue _catalogue;
  private readonly IPersonRepository _persons;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly IUnitOfWork _unitOfWork;
  private readonly ITodayCacheRepository _todayCache;
  private readonly TimeProvider _clock;

  public GoalService(
    IGoalRepository goals,
    IMetricCatalogue catalogue,
    IPersonRepository persons,
    IEncryptionService encryption,
    IAuditService audit,
    IUnitOfWork unitOfWork,
    ITodayCacheRepository todayCache,
    TimeProvider clock)
  {
    _goals = goals;
    _catalogue = catalogue;
    _persons = persons;
    _encryption = encryption;
    _audit = audit;
    _unitOfWork = unitOfWork;
    _todayCache = todayCache;
    _clock = clock;
  }

  public async Task<IReadOnlyList<GoalDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, bool includeEnded, CancellationToken ct = default)
  {
    var goals = await _goals.ListAsync(viewerPersonId, targetPersonId, includeEnded, ct);
    return goals?.Select(g => g.ToDto(_encryption)).ToList();
  }

  public async Task<GoalDto> CreateAsync(Actor actor, SaveGoalRequest request, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(actor.PersonId, ct)
      ?? throw new DomainValidationException("No person profile for this account.");
    var now = _clock.GetUtcNow().UtcDateTime;
    var today = DayBoundaries.LocalDay(now, DayBoundaries.Resolve(person.TimeZone));
    var title = Validate(request, request.StartedOn ?? today);

    var goal = new Goal
    {
      Id = Guid.CreateVersion7(),
      PersonId = actor.PersonId,
      Slot = request.Slot,
      Status = GoalStatuses.Active,
      CreatedAt = now,
    };
    Apply(goal, request, title, request.StartedOn ?? today, now);

    await _unitOfWork.RunInTransactionAsync(async () =>
    {
      foreach (var active in await _goals.ListOwnActiveAsync(actor.PersonId, ct))
      {
        if (active.Slot != goal.Slot) continue;
        var tracked = await _goals.GetOwnAsync(actor.PersonId, active.Id, ct);
        if (tracked == null) continue;
        tracked.Status = GoalStatuses.Dropped;
        tracked.EndedAt = now;
        tracked.UpdatedAt = now;
        await _goals.UpdateAsync(tracked, ct);
      }
      await _goals.AddAsync(goal, ct);
      await _todayCache.InvalidateAsync(actor.PersonId, ct);
      return true;
    }, ct: ct);

    await RecordAsync(AuditActions.GoalSaved, actor, goal, ct);
    return goal.ToDto(_encryption);
  }

  public async Task<GoalDto?> UpdateAsync(Actor actor, Guid goalId, SaveGoalRequest request, CancellationToken ct = default)
  {
    var goal = await _goals.GetOwnAsync(actor.PersonId, goalId, ct);
    if (goal == null) return null;
    if (request.Slot != goal.Slot) throw new DomainValidationException("A goal keeps its slot. End it and start a new one.");
    var startedOn = request.StartedOn ?? goal.StartedOn;
    var title = Validate(request, startedOn);

    var now = _clock.GetUtcNow().UtcDateTime;
    Apply(goal, request, title, startedOn, now);
    await _goals.UpdateAsync(goal, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);

    await RecordAsync(AuditActions.GoalSaved, actor, goal, ct);
    return goal.ToDto(_encryption);
  }

  public async Task<GoalDto?> EndAsync(Actor actor, Guid goalId, EndGoalRequest request, CancellationToken ct = default)
  {
    if (request.Status is not (GoalStatuses.Done or GoalStatuses.Dropped))
    {
      throw new DomainValidationException($"status must be {GoalStatuses.Done} or {GoalStatuses.Dropped}.");
    }
    var goal = await _goals.GetOwnAsync(actor.PersonId, goalId, ct);
    if (goal == null) return null;
    if (goal.Status == GoalStatuses.Active)
    {
      var now = _clock.GetUtcNow().UtcDateTime;
      goal.Status = request.Status;
      goal.EndedAt = now;
      goal.UpdatedAt = now;
      await _goals.UpdateAsync(goal, ct);
      await _todayCache.InvalidateAsync(actor.PersonId, ct);
      await RecordAsync(AuditActions.GoalEnded, actor, goal, ct);
    }
    return goal.ToDto(_encryption);
  }

  /// <summary>Checks the request and returns the cleaned title (null for "use the name of the kind").</summary>
  private string? Validate(SaveGoalRequest request, DateOnly startedOn)
  {
    if (!GoalSlots.All.Contains(request.Slot)) throw new DomainValidationException($"slot must be one of: {string.Join(", ", GoalSlots.All)}.");
    if (!GoalKinds.All.Contains(request.Kind)) throw new DomainValidationException($"kind must be one of: {string.Join(", ", GoalKinds.All)}.");
    if (request.MetricKey != null && _catalogue.Find(request.MetricKey) == null)
    {
      throw new DomainValidationException($"Unknown metric '{request.MetricKey}'.");
    }
    var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
    if (title is { Length: > MaxTitleLength }) throw new DomainValidationException($"A title is at most {MaxTitleLength} characters.");
    if (title == null && request.Kind == GoalKinds.Custom) throw new DomainValidationException("A goal of your own needs a title.");

    foreach (var (name, value) in new[] { ("startValue", request.StartValue), ("targetValue", request.TargetValue), ("pacePerWeek", request.PacePerWeek) })
    {
      if (value is { } v && !double.IsFinite(v)) throw new DomainValidationException($"{name} must be a number.");
    }
    if (request is { StartValue: { } start, TargetValue: { } target, PacePerWeek: { } pace } && pace != 0 && target != start)
    {
      if (Math.Sign(pace) != Math.Sign(target - start))
      {
        throw new DomainValidationException("pacePerWeek points away from the target.");
      }
    }
    if (request.TargetDate is { } due && due <= startedOn) throw new DomainValidationException("targetDate must be after the start.");
    return title;
  }

  private void Apply(Goal goal, SaveGoalRequest request, string? title, DateOnly startedOn, DateTime now)
  {
    goal.Kind = request.Kind;
    goal.TitleCipher = title == null ? null : _encryption.Encrypt(title);
    goal.MetricKey = request.MetricKey;
    goal.StartValue = request.StartValue;
    goal.TargetValue = request.TargetValue;
    goal.PacePerWeek = request.PacePerWeek;
    goal.StartedOn = startedOn;
    goal.TargetDate = request.TargetDate;
    goal.UpdatedAt = now;
  }

  private Task RecordAsync(string action, Actor actor, Goal goal, CancellationToken ct) =>
    _audit.RecordAsync(new AuditEvent(
      action, actor.UserId, actor.HouseholdId, "goal", goal.Id.ToString(),
      new { slot = goal.Slot, kind = goal.Kind, status = goal.Status }, actor.Info.IpAddress), ct);
}
