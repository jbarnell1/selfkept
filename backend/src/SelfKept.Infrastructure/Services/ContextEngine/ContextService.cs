using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.ContextEngine;

/// <summary>
/// Context events a person logs (an illness, a trip, a new scale) and what is active today,
/// stored and derived (docs/context-design.md). The list is read under the insights grant; each
/// derived piece is read under its own source's grant, so a viewer without the labs grant never
/// learns that a medicine started. Writes are always the caller's own.
/// </summary>
public sealed class ContextService : IContextService
{
  public const int MaxNoteLength = 500;
  public const int MaxEvents = 500;
  public const int DefaultListDays = 30;
  public const int MaxListDays = 366;
  public const int MaxDaysAhead = 60;
  public const int MaxYearsBack = 5;
  private const int JournalReadLimit = 500;

  private readonly IContextEventRepository _events;
  private readonly IGoalRepository _goals;
  private readonly IMedicationRepository _medications;
  private readonly IJournalRepository _journal;
  private readonly IStrengthRepository _strength;
  private readonly IWorkoutRepository _workouts;
  private readonly IPersonRepository _persons;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly ITodayCacheRepository _todayCache;
  private readonly TimeProvider _clock;

  public ContextService(
    IContextEventRepository events,
    IGoalRepository goals,
    IMedicationRepository medications,
    IJournalRepository journal,
    IStrengthRepository strength,
    IWorkoutRepository workouts,
    IPersonRepository persons,
    IEncryptionService encryption,
    IAuditService audit,
    ITodayCacheRepository todayCache,
    TimeProvider clock)
  {
    _events = events;
    _goals = goals;
    _medications = medications;
    _journal = journal;
    _strength = strength;
    _workouts = workouts;
    _persons = persons;
    _encryption = encryption;
    _audit = audit;
    _todayCache = todayCache;
    _clock = clock;
  }

  public async Task<ContextDto?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, zone);
    var last = to ?? today;
    var first = from ?? last.AddDays(-(DefaultListDays - 1));
    if (first > last) throw new DomainValidationException("from must not be after to.");
    if (last.DayNumber - first.DayNumber + 1 > MaxListDays) throw new DomainValidationException($"A list covers at most {MaxListDays} days.");

    var events = await _events.ListAsync(viewerPersonId, targetPersonId, first, last, ct);
    if (events == null) return null;

    // What is active today: stored events plus what the record shows, each part under its own grant.
    var current = await _events.ListAsync(viewerPersonId, targetPersonId, today, today, ct) ?? [];
    var goals = await _goals.ListAsync(viewerPersonId, targetPersonId, includeEnded: false, ct);
    var medications = await _medications.ListAsync(viewerPersonId, targetPersonId, ct);
    var journal = await _journal.ListAsync(viewerPersonId, targetPersonId, today.AddDays(-(ContextDerivation.SymptomDays - 1)), today, JournalReadLimit, ct);
    var strength = await _strength.ListAsync(viewerPersonId, targetPersonId, today.AddDays(-1), today, ct);
    var (fromUtc, _) = DayBoundaries.UtcWindow(today.AddDays(-1), zone);
    var (_, toUtc) = DayBoundaries.UtcWindow(today, zone);
    var workouts = await _workouts.ListAsync(viewerPersonId, targetPersonId, fromUtc, toUtc, ct);

    var record = new ContextRecord(
      current,
      goals,
      medications,
      journal == null ? null : ContextLoader.Logged(journal),
      strength,
      workouts?.Select(w => new WorkoutDay(DayBoundaries.LocalDay(w.StartAt, zone), (w.EndAt - w.StartAt).TotalMinutes)).ToList());
    var active = ContextDerivation.Active(today, record)
      .Select(a => new ActiveContextDto(a.Kind, ContextKinds.Label(a.Kind), a.Since, a.Until, a.Source, a.Reason, a.EventId, a.Detail))
      .ToList();

    var own = viewerPersonId == targetPersonId;
    return new ContextDto(today, first, last, active, events.Select(e => ToDto(e, today, own)).ToList());
  }

  public async Task<ContextEventDto> CreateAsync(Actor actor, SaveContextEventRequest request, CancellationToken ct = default)
  {
    var today = await TodayAsync(actor.PersonId, ct);
    var kind = Kind(request.Kind);
    var started = request.StartedOn ?? today;
    CheckDates(started, request.EndedOn, today);
    var note = Note(request.Note);
    if (await _events.CountOwnAsync(actor.PersonId, ct) >= MaxEvents)
      throw new DomainValidationException($"At most {MaxEvents} context events. Remove one first.");

    var now = _clock.GetUtcNow().UtcDateTime;
    var entity = new ContextEvent
    {
      Id = Guid.CreateVersion7(),
      PersonId = actor.PersonId,
      Kind = kind,
      StartedOn = started,
      EndedOn = request.EndedOn,
      Source = ContextSources.User,
      NoteCipher = note == null ? null : CollectMapping.ToCipherBytes(_encryption.Encrypt(note)),
      CreatedAt = now,
      UpdatedAt = now,
    };
    await _events.AddAsync(entity, ct);
    await AfterWriteAsync(AuditActions.ContextSaved, actor, entity, ct);
    return ToDto(entity, today, own: true);
  }

  public async Task<ContextEventDto?> UpdateAsync(Actor actor, Guid eventId, UpdateContextEventRequest request, CancellationToken ct = default)
  {
    var entity = await _events.GetOwnAsync(actor.PersonId, eventId, ct);
    if (entity == null) return null;
    var today = await TodayAsync(actor.PersonId, ct);

    var kind = request.Kind == null ? entity.Kind : Kind(request.Kind);
    var started = request.StartedOn ?? entity.StartedOn;
    var ended = request.Reopen == true ? null : request.EndedOn ?? entity.EndedOn;
    CheckDates(started, ended, today);

    entity.Kind = kind;
    entity.StartedOn = started;
    entity.EndedOn = ended;
    if (request.Note != null)
    {
      var note = Note(request.Note);
      entity.NoteCipher = note == null ? null : CollectMapping.ToCipherBytes(_encryption.Encrypt(note));
    }
    entity.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
    await _events.UpdateAsync(entity, ct);
    await AfterWriteAsync(AuditActions.ContextSaved, actor, entity, ct);
    return ToDto(entity, today, own: true);
  }

  public async Task<bool> DeleteAsync(Actor actor, Guid eventId, CancellationToken ct = default)
  {
    var entity = await _events.GetOwnAsync(actor.PersonId, eventId, ct);
    if (entity == null) return false;
    await _events.RemoveAsync(entity, ct);
    await AfterWriteAsync(AuditActions.ContextDeleted, actor, entity, ct);
    return true;
  }

  private async Task<DateOnly> TodayAsync(Guid personId, CancellationToken ct)
  {
    var person = await _persons.GetByIdAsync(personId, ct)
      ?? throw new DomainValidationException("No person profile for this account.");
    return DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
  }

  private static string Kind(string? kind)
  {
    var value = kind?.Trim().ToLowerInvariant() ?? string.Empty;
    if (!ContextKinds.All.Contains(value)) throw new DomainValidationException($"kind must be one of: {string.Join(", ", ContextKinds.All)}.");
    return value;
  }

  private static void CheckDates(DateOnly started, DateOnly? ended, DateOnly today)
  {
    if (started > today.AddDays(MaxDaysAhead)) throw new DomainValidationException($"startedOn cannot be more than {MaxDaysAhead} days ahead.");
    if (started < today.AddYears(-MaxYearsBack)) throw new DomainValidationException($"startedOn cannot be more than {MaxYearsBack} years ago.");
    if (ended is { } end && end < started) throw new DomainValidationException("endedOn cannot be before startedOn.");
  }

  private static string? Note(string? note)
  {
    var value = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    if (value is { Length: > MaxNoteLength }) throw new DomainValidationException($"A note is at most {MaxNoteLength} characters.");
    return value;
  }

  private async Task AfterWriteAsync(string action, Actor actor, ContextEvent entity, CancellationToken ct)
  {
    await _todayCache.InvalidateAsync(actor.PersonId, ct);
    await _audit.RecordAsync(new AuditEvent(
      action, actor.UserId, actor.HouseholdId, "context_event", entity.Id.ToString(),
      new { kind = entity.Kind }, actor.Info.IpAddress), ct);
  }

  private ContextEventDto ToDto(ContextEvent e, DateOnly today, bool own) => new(
    e.Id,
    e.Kind,
    ContextKinds.Label(e.Kind),
    e.StartedOn,
    e.EndedOn,
    e.Source,
    own && e.NoteCipher != null ? _encryption.TryDecrypt(CollectMapping.FromCipherBytes(e.NoteCipher)) : null,
    e.ActiveOn(today));
}
