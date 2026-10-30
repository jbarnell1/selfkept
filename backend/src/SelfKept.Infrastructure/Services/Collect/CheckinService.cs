using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>
/// Check-ins: a mood level plus an encrypted note. Each one also lands as a checkin_mood sample
/// under the person's system source ("Check-ins", kind manual) so it shows on trends and in the
/// daily summary like any other metric.
/// </summary>
public sealed class CheckinService : ICheckinService
{
  public const int MaxTags = 10;
  public const int MaxTagLength = 32;
  public const int ListLimit = 500;
  private static readonly TimeSpan DefaultListWindow = TimeSpan.FromDays(30);

  private readonly ICheckinRepository _checkins;
  private readonly ISourceRepository _sources;
  private readonly ISampleRepository _samples;
  private readonly IPersonRepository _persons;
  private readonly IDailySummaryService _summaries;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly IUnitOfWork _unitOfWork;
  private readonly ITodayCacheRepository _todayCache;
  private readonly TimeProvider _clock;

  public CheckinService(
    ICheckinRepository checkins,
    ISourceRepository sources,
    ISampleRepository samples,
    IPersonRepository persons,
    IDailySummaryService summaries,
    IEncryptionService encryption,
    IAuditService audit,
    IUnitOfWork unitOfWork,
    ITodayCacheRepository todayCache,
    TimeProvider clock)
  {
    _todayCache = todayCache;
    _checkins = checkins;
    _sources = sources;
    _samples = samples;
    _persons = persons;
    _summaries = summaries;
    _encryption = encryption;
    _audit = audit;
    _unitOfWork = unitOfWork;
    _clock = clock;
  }

  public async Task<CheckinDto> CreateAsync(Guid userId, Guid personId, Guid? householdId, CreateCheckinRequest request, RequestInfo info, CancellationToken ct = default)
  {
    if (request.Mood is < Checkin.MinMood or > Checkin.MaxMood)
    {
      throw new DomainValidationException($"mood must be between {Checkin.MinMood} and {Checkin.MaxMood}.");
    }
    var now = _clock.GetUtcNow().UtcDateTime;
    var at = request.At is { } requested ? DayBoundaries.AsUtc(requested) : now;
    if (at > now.AddHours(1)) throw new DomainValidationException("at cannot be in the future.");

    var person = await _persons.GetByIdAsync(personId, ct)
      ?? throw new DomainValidationException("No person profile for this account.");
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
    var tags = NormaliseTags(request.Tags);

    var checkin = new Checkin
    {
      Id = Guid.CreateVersion7(),
      PersonId = personId,
      At = at,
      Mood = (short)request.Mood,
      NoteCipher = note == null ? null : CollectMapping.ToCipherBytes(_encryption.Encrypt(note)),
      Tags = tags,
      CreatedAt = now,
    };

    await _unitOfWork.RunInTransactionAsync(async () =>
    {
      var source = await GetOrCreateSystemSourceAsync(personId, now, ct);
      await _checkins.AddAsync(checkin, ct);

      await _samples.EnsurePartitionsAsync([DayBoundaries.MonthStart(at)], ct);
      var result = await _samples.InsertManyAsync([MoodSample(checkin, source, now)], ct);

      source.LastSyncAt = now;
      source.LastSampleAt = source.LastSampleAt is { } last && last > at ? last : at;
      source.SampleCount += result.Accepted;
      await _sources.UpdateAsync(source, ct);

      await _summaries.RecomputeAsync(personId, person.TimeZone, [(MetricKeys.CheckinMood, DayBoundaries.LocalDay(at, zone))], ct);
      await _todayCache.InvalidateAsync(personId, ct);
      return true;
    }, ct: ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.CheckinCreated, userId, householdId, "checkin", checkin.Id.ToString(),
      new { hasNote = note != null, tagCount = tags.Length }, info.IpAddress), ct);

    return checkin.ToDto(note);
  }

  public async Task<IReadOnlyList<CheckinDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
  {
    var now = _clock.GetUtcNow().UtcDateTime;
    var to = toUtc is { } t ? DayBoundaries.AsUtc(t) : now.AddMinutes(1);
    var from = fromUtc is { } f ? DayBoundaries.AsUtc(f) : to - DefaultListWindow;
    if (from > to) throw new DomainValidationException("from must not be after to.");

    var rows = await _checkins.ListAsync(viewerPersonId, targetPersonId, from, to, ListLimit, ct);
    if (rows == null) return null;
    return rows.Select(c => c.ToDto(c.NoteCipher == null ? null : _encryption.TryDecrypt(CollectMapping.FromCipherBytes(c.NoteCipher)))).ToList();
  }

  public async Task<bool> DeleteAsync(Guid personId, Guid checkinId, CancellationToken ct = default)
  {
    var checkin = await _checkins.GetOwnAsync(personId, checkinId, ct);
    if (checkin == null) return false;
    var person = await _persons.GetByIdAsync(personId, ct);
    var timeZone = person?.TimeZone ?? "UTC";
    var zone = DayBoundaries.Resolve(timeZone);

    await _unitOfWork.RunInTransactionAsync(async () =>
    {
      await _checkins.RemoveAsync(checkin, ct);
      var source = await _sources.FindActiveAsync(personId, SourceKinds.Manual, SourceKinds.CheckinDeviceName, SourceKinds.CheckinPlatform, ct);
      if (source != null)
      {
        var removed = await _samples.DeleteBySourceUidAsync(source.Id, MoodSourceUid(checkin.Id), ct);
        source.SampleCount = Math.Max(0, source.SampleCount - removed);
        await _sources.UpdateAsync(source, ct);
      }
      await _summaries.RecomputeAsync(personId, timeZone, [(MetricKeys.CheckinMood, DayBoundaries.LocalDay(checkin.At, zone))], ct);
      await _todayCache.InvalidateAsync(personId, ct);
      return true;
    }, ct: ct);
    return true;
  }

  private async Task<Source> GetOrCreateSystemSourceAsync(Guid personId, DateTime now, CancellationToken ct)
  {
    var existing = await _sources.FindActiveAsync(personId, SourceKinds.Manual, SourceKinds.CheckinDeviceName, SourceKinds.CheckinPlatform, ct);
    if (existing != null) return existing;
    return await _sources.AddAsync(new Source
    {
      Id = Guid.CreateVersion7(),
      PersonId = personId,
      Kind = SourceKinds.Manual,
      DeviceName = SourceKinds.CheckinDeviceName,
      Platform = SourceKinds.CheckinPlatform,
      Enabled = true,
      CreatedAt = now,
    }, ct);
  }

  public static string MoodSourceUid(Guid checkinId) => $"checkin:{checkinId:N}";

  private static Sample MoodSample(Checkin checkin, Source source, DateTime now) => new()
  {
    Id = Guid.CreateVersion7(),
    PersonId = checkin.PersonId,
    SourceId = source.Id,
    SourceUid = MoodSourceUid(checkin.Id),
    MetricKey = MetricKeys.CheckinMood,
    StartAt = checkin.At,
    Value = checkin.Mood,
    Unit = "level",
    CreatedAt = now,
  };

  private static string[] NormaliseTags(IReadOnlyList<string>? tags)
  {
    if (tags == null || tags.Count == 0) return [];
    var cleaned = tags
      .Select(t => t?.Trim().ToLowerInvariant() ?? string.Empty)
      .Where(t => t.Length > 0)
      .Distinct(StringComparer.Ordinal)
      .ToArray();
    if (cleaned.Length > MaxTags) throw new DomainValidationException($"At most {MaxTags} tags per check-in.");
    if (cleaned.Any(t => t.Length > MaxTagLength)) throw new DomainValidationException($"Tags are at most {MaxTagLength} characters.");
    return cleaned;
  }
}
