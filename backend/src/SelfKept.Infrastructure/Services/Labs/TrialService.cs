using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>
/// Trials (docs/labs-design.md, "Trials"): a single change with a planned check. The title, what
/// changed and the outcome are encrypted; the dates, the readings and the status are plain. The
/// rules "trial ready to read" and "retest due" read them; nothing here judges the change.
/// </summary>
public sealed class TrialService : ITrialService
{
  public const int MaxTitleLength = 80;
  public const int MaxChangeLength = 200;
  public const int MaxOutcomeLength = 500;
  public const int MaxMetricKeys = 8;
  public const int MaxActive = 20;

  private readonly ITrialRepository _trials;
  private readonly IMetricCatalogue _catalogue;
  private readonly IPersonRepository _persons;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly ITodayCacheRepository _todayCache;
  private readonly TimeProvider _clock;

  public TrialService(
    ITrialRepository trials,
    IMetricCatalogue catalogue,
    IPersonRepository persons,
    IEncryptionService encryption,
    IAuditService audit,
    ITodayCacheRepository todayCache,
    TimeProvider clock)
  {
    _trials = trials;
    _catalogue = catalogue;
    _persons = persons;
    _encryption = encryption;
    _audit = audit;
    _todayCache = todayCache;
    _clock = clock;
  }

  public async Task<IReadOnlyList<TrialDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, bool includeEnded, CancellationToken ct = default)
  {
    var rows = await _trials.ListAsync(viewerPersonId, targetPersonId, includeEnded, ct);
    return rows?.Select(t => t.ToDto(_encryption)).ToList();
  }

  public async Task<TrialDto> CreateAsync(Actor actor, CreateTrialRequest request, CancellationToken ct = default)
  {
    var today = await TodayAsync(actor.PersonId, ct);
    var title = Title(request.Title);
    var change = Optional(request.Change, MaxChangeLength, "change");
    var started = request.StartedOn ?? today;
    if (request.ReadOn is not { } readOn) throw new DomainValidationException("readOn is required: the date to read the result.");
    Dates(started, readOn, today);
    var keys = Keys(request.MetricKeys);

    var active = await _trials.ListOwnActiveAsync(actor.PersonId, ct);
    if (active.Count >= MaxActive) throw new DomainValidationException($"At most {MaxActive} active trials. End one first.");

    var now = _clock.GetUtcNow().UtcDateTime;
    var trial = new Trial
    {
      Id = Guid.CreateVersion7(),
      PersonId = actor.PersonId,
      TitleCipher = _encryption.Encrypt(title),
      ChangeCipher = change == null ? null : _encryption.Encrypt(change),
      StartedOn = started,
      ReadOn = readOn,
      MetricKeys = keys,
      Status = TrialStatuses.Active,
      CreatedAt = now,
      UpdatedAt = now,
    };
    await _trials.AddAsync(trial, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);
    await RecordAsync(AuditActions.TrialSaved, actor, trial, ct);
    return trial.ToDto(_encryption);
  }

  public async Task<TrialDto?> UpdateAsync(Actor actor, Guid trialId, UpdateTrialRequest request, CancellationToken ct = default)
  {
    var trial = await _trials.GetOwnAsync(actor.PersonId, trialId, ct);
    if (trial == null) return null;
    var today = await TodayAsync(actor.PersonId, ct);
    var now = _clock.GetUtcNow().UtcDateTime;

    if (request.Title != null) trial.TitleCipher = _encryption.Encrypt(Title(request.Title));
    if (request.Change != null)
    {
      var change = Optional(request.Change, MaxChangeLength, "change");
      trial.ChangeCipher = change == null ? null : _encryption.Encrypt(change);
    }
    if (request.Outcome != null)
    {
      var outcome = Optional(request.Outcome, MaxOutcomeLength, "outcome");
      trial.OutcomeCipher = outcome == null ? null : _encryption.Encrypt(outcome);
    }
    var started = request.StartedOn ?? trial.StartedOn;
    var readOn = request.ReadOn ?? trial.ReadOn;
    if (request.StartedOn != null || request.ReadOn != null) Dates(started, readOn, today);
    trial.StartedOn = started;
    trial.ReadOn = readOn;
    if (request.MetricKeys != null) trial.MetricKeys = Keys(request.MetricKeys);

    if (request.Status != null)
    {
      var status = request.Status.Trim().ToLowerInvariant();
      if (!TrialStatuses.All.Contains(status)) throw new DomainValidationException($"status must be one of: {string.Join(", ", TrialStatuses.All)}.");
      if (status == TrialStatuses.Active && trial.Status != TrialStatuses.Active)
      {
        var active = await _trials.ListOwnActiveAsync(actor.PersonId, ct);
        if (active.Count >= MaxActive) throw new DomainValidationException($"At most {MaxActive} active trials. End one first.");
      }
      trial.EndedAt = status == TrialStatuses.Active ? null : trial.Status == status ? trial.EndedAt : now;
      trial.Status = status;
    }

    trial.UpdatedAt = now;
    await _trials.UpdateAsync(trial, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);
    await RecordAsync(AuditActions.TrialSaved, actor, trial, ct);
    return trial.ToDto(_encryption);
  }

  public async Task<bool> DeleteAsync(Actor actor, Guid trialId, CancellationToken ct = default)
  {
    var trial = await _trials.GetOwnAsync(actor.PersonId, trialId, ct);
    if (trial == null) return false;
    await _trials.RemoveAsync(trial, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);
    await RecordAsync(AuditActions.TrialDeleted, actor, trial, ct);
    return true;
  }

  private static string Title(string? title)
  {
    var t = title?.Trim() ?? string.Empty;
    if (t.Length == 0) throw new DomainValidationException("Say what the trial is.");
    if (t.Length > MaxTitleLength) throw new DomainValidationException($"title is at most {MaxTitleLength} characters.");
    return t;
  }

  private static string? Optional(string? text, int max, string name)
  {
    var t = text?.Trim();
    if (string.IsNullOrEmpty(t)) return null;
    if (t.Length > max) throw new DomainValidationException($"{name} is at most {max} characters.");
    return t;
  }

  private static void Dates(DateOnly started, DateOnly readOn, DateOnly today)
  {
    if (started.Year < 1900) throw new DomainValidationException("startedOn is before 1900.");
    if (started > today.AddDays(31)) throw new DomainValidationException("startedOn is more than a month away.");
    if (readOn < started) throw new DomainValidationException("readOn cannot be before startedOn.");
    if (readOn > started.AddYears(2)) throw new DomainValidationException("readOn is at most two years after startedOn.");
  }

  private string[] Keys(IReadOnlyList<string>? keys)
  {
    if (keys == null || keys.Count == 0) return [];
    if (keys.Count > MaxMetricKeys) throw new DomainValidationException($"At most {MaxMetricKeys} readings per trial.");
    var list = new List<string>();
    foreach (var raw in keys)
    {
      var key = raw?.Trim() ?? string.Empty;
      if (_catalogue.Find(key) is not { } metric) throw new DomainValidationException($"'{key}' is not a reading in the catalogue.");
      if (!list.Contains(metric.Key)) list.Add(metric.Key);
    }
    return list.ToArray();
  }

  private async Task<DateOnly> TodayAsync(Guid personId, CancellationToken ct)
  {
    var person = await _persons.GetByIdAsync(personId, ct) ?? throw new DomainValidationException("No person profile for this account.");
    return DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
  }

  private Task RecordAsync(string action, Actor actor, Trial trial, CancellationToken ct) =>
    _audit.RecordAsync(new AuditEvent(action, actor.UserId, actor.HouseholdId, "trial", trial.Id.ToString(),
      new { status = trial.Status, readings = trial.MetricKeys.Length }, actor.Info.IpAddress), ct);
}

public static class TrialMapping
{
  public const string Unreadable = "Unreadable trial";

  public static TrialDto ToDto(this Trial t, IEncryptionService encryption) => new(
    t.Id,
    encryption.TryDecrypt(t.TitleCipher) ?? Unreadable,
    t.ChangeCipher == null ? null : encryption.TryDecrypt(t.ChangeCipher),
    t.StartedOn,
    t.ReadOn,
    t.MetricKeys,
    t.Status,
    t.OutcomeCipher == null ? null : encryption.TryDecrypt(t.OutcomeCipher),
    t.CreatedAt,
    t.UpdatedAt,
    t.EndedAt);
}
