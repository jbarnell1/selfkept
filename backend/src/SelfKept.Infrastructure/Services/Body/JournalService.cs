using System.Text.RegularExpressions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// The day log (docs/body-design.md, "Day log"): short entries in the person's own words plus
/// coarse tags. No weighing of food. The words are encrypted; the tags and the kind are plain so
/// targets and patterns can count them without decrypting anything. The one place amounts come
/// from is a confirmed photo (docs/photo-design.md): such an entry carries its numbers, and each
/// number is also a sample under the person's "Photo" source.
/// </summary>
public sealed partial class JournalService : IJournalService
{
  public const int MaxTextLength = 500;
  public const int MaxTags = 8;
  public const int ListLimit = 500;
  public const int DefaultListDays = 14;

  private readonly IJournalRepository _journal;
  private readonly IPersonRepository _persons;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly ITodayCacheRepository _todayCache;
  private readonly NutritionSamples _nutrition;
  private readonly IUnitOfWork _unitOfWork;
  private readonly TimeProvider _clock;

  public JournalService(
    IJournalRepository journal,
    IPersonRepository persons,
    IEncryptionService encryption,
    IAuditService audit,
    ITodayCacheRepository todayCache,
    NutritionSamples nutrition,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
  {
    _journal = journal;
    _persons = persons;
    _encryption = encryption;
    _audit = audit;
    _todayCache = todayCache;
    _nutrition = nutrition;
    _unitOfWork = unitOfWork;
    _clock = clock;
  }

  public Task<JournalEntryDto> CreateAsync(Actor actor, CreateJournalEntryRequest request, CancellationToken ct = default) =>
    SaveDraftAsync(actor, new JournalDraft(
      request.Kind,
      request.Text,
      request.Tags ?? [],
      request.Portion,
      request.At,
      request.Nutrition != null ? JournalSources.Photo : string.IsNullOrWhiteSpace(request.Text) ? JournalSources.Chip : JournalSources.Typed,
      MessageId: null,
      request.Nutrition), ct);

  public async Task<JournalEntryDto> SaveDraftAsync(Actor actor, JournalDraft draft, CancellationToken ct = default)
  {
    if (!JournalKinds.All.Contains(draft.Kind)) throw new DomainValidationException($"kind must be one of: {string.Join(", ", JournalKinds.All)}.");
    if (!JournalSources.All.Contains(draft.Source)) throw new DomainValidationException("Unknown source.");
    var text = string.IsNullOrWhiteSpace(draft.Text) ? null : draft.Text.Trim();
    if (text is { Length: > MaxTextLength }) throw new DomainValidationException($"An entry is at most {MaxTextLength} characters.");
    var tags = NormaliseTags(draft.Tags);
    if (text == null && tags.Length == 0) throw new DomainValidationException("Nothing to save.");
    if (draft.Portion != null)
    {
      if (draft.Kind is not (JournalKinds.Food or JournalKinds.Drink)) throw new DomainValidationException("Only food and drink have a portion.");
      if (!Portions.All.Contains(draft.Portion)) throw new DomainValidationException($"portion must be one of: {string.Join(", ", Portions.All)}.");
    }
    var nutrition = draft.Nutrition == null ? ((string Basis, decimal? Energy, decimal? Protein, decimal? Carbs, decimal? Fat)?)null : NutritionSamples.Validate(draft.Kind, draft.Nutrition);

    var now = _clock.GetUtcNow().UtcDateTime;
    var at = draft.At is { } requested ? DayBoundaries.AsUtc(requested) : now;
    if (at > now.AddHours(1)) throw new DomainValidationException("at cannot be in the future.");
    var person = await _persons.GetByIdAsync(actor.PersonId, ct)
      ?? throw new DomainValidationException("No person profile for this account.");

    var entry = new JournalEntry
    {
      Id = Guid.CreateVersion7(),
      PersonId = actor.PersonId,
      At = at,
      Day = DayBoundaries.LocalDay(at, DayBoundaries.Resolve(person.TimeZone)),
      Kind = draft.Kind,
      TextCipher = text == null ? null : CollectMapping.ToCipherBytes(_encryption.Encrypt(text)),
      Tags = tags,
      Portion = draft.Portion,
      Source = draft.Source,
      MessageId = draft.MessageId,
      NutritionBasis = nutrition?.Basis,
      EnergyKcal = nutrition?.Energy,
      ProteinG = nutrition?.Protein,
      CarbsG = nutrition?.Carbs,
      FatG = nutrition?.Fat,
      CreatedAt = now,
    };
    if (nutrition == null)
    {
      await _journal.AddAsync(entry, ct);
      await _todayCache.InvalidateAsync(actor.PersonId, ct);
    }
    else
    {
      // The entry, its samples, the day's summary and the Today cache change together or not at all.
      await _unitOfWork.RunInTransactionAsync(async () =>
      {
        await _journal.AddAsync(entry, ct);
        await _nutrition.WriteAsync(entry, person.TimeZone, now, ct);
        await _todayCache.InvalidateAsync(actor.PersonId, ct);
        return true;
      }, ct: ct);
    }

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.JournalCreated, actor.UserId, actor.HouseholdId, "journal_entry", entry.Id.ToString(),
      nutrition == null
        ? new { kind = entry.Kind, tagCount = tags.Length, source = entry.Source }
        : new { kind = entry.Kind, tagCount = tags.Length, source = entry.Source, basis = nutrition.Value.Basis }, actor.Info.IpAddress), ct);
    return entry.ToDto(_encryption);
  }

  public async Task<IReadOnlyList<JournalEntryDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person?.TimeZone));
    var last = to ?? today;
    var first = from ?? last.AddDays(-(DefaultListDays - 1));
    if (first > last) throw new DomainValidationException("from must not be after to.");

    var rows = await _journal.ListAsync(viewerPersonId, targetPersonId, first, last, ListLimit, ct);
    return rows?.Select(e => e.ToDto(_encryption)).ToList();
  }

  public async Task<bool> DeleteAsync(Actor actor, Guid entryId, CancellationToken ct = default)
  {
    var entry = await _journal.GetOwnAsync(actor.PersonId, entryId, ct);
    if (entry == null) return false;
    await RemoveAsync(actor, entry, ct);
    return true;
  }

  public async Task<int> DeleteByMessageAsync(Actor actor, Guid messageId, CancellationToken ct = default)
  {
    var entries = await _journal.ListOwnByMessageAsync(actor.PersonId, messageId, ct);
    foreach (var entry in entries) await RemoveAsync(actor, entry, ct);
    return entries.Count;
  }

  public IReadOnlyList<JournalTagGroupDto> TagGroups() =>
    JournalKinds.All
      .Select(kind => new JournalTagGroupDto(kind, JournalTags.ForKind(kind).Select(t => new JournalTagDto(t, JournalTags.DisplayName(t))).ToList()))
      .Where(group => group.Tags.Count > 0)
      .ToList();

  private async Task RemoveAsync(Actor actor, JournalEntry entry, CancellationToken ct)
  {
    if (entry.NutritionBasis == null)
    {
      await _journal.RemoveAsync(entry, ct);
      await _todayCache.InvalidateAsync(actor.PersonId, ct);
    }
    else
    {
      var person = await _persons.GetByIdAsync(actor.PersonId, ct);
      await _unitOfWork.RunInTransactionAsync(async () =>
      {
        await _journal.RemoveAsync(entry, ct);
        await _nutrition.RemoveAsync(entry, person?.TimeZone ?? "UTC", ct);
        await _todayCache.InvalidateAsync(actor.PersonId, ct);
        return true;
      }, ct: ct);
    }
    await _audit.RecordAsync(new AuditEvent(
      AuditActions.JournalDeleted, actor.UserId, actor.HouseholdId, "journal_entry", entry.Id.ToString(),
      new { kind = entry.Kind }, actor.Info.IpAddress), ct);
  }

  /// <summary>Lowercase, distinct, shaped like the built-in tags. A tag of the person's own is allowed.</summary>
  public static string[] NormaliseTags(IReadOnlyList<string>? tags)
  {
    if (tags == null || tags.Count == 0) return [];
    var cleaned = tags
      .Select(t => t?.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_') ?? string.Empty)
      .Where(t => t.Length > 0)
      .Distinct(StringComparer.Ordinal)
      .ToArray();
    if (cleaned.Length > MaxTags) throw new DomainValidationException($"At most {MaxTags} tags per entry.");
    if (cleaned.Any(t => !TagShape().IsMatch(t))) throw new DomainValidationException("Tags are 1 to 32 letters, digits or underscores.");
    return cleaned;
  }

  [GeneratedRegex("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant)]
  private static partial Regex TagShape();
}
