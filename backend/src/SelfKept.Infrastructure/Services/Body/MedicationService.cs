using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// Medicines and supplements a person takes (docs/body-design.md, "Medicines and supplements").
/// Brand, product, dose and the ingredient list are encrypted together; the only plain parts are
/// the kind, the schedule, the dates and the substance tags rules read to explain a lab result.
/// The list is a record of what is taken. Nothing here is advice to start, stop or change a dose.
/// </summary>
public sealed class MedicationService : IMedicationService
{
  public const int MaxItems = 60;
  public const int MaxIngredients = 40;
  public const int MaxNameLength = 80;
  public const int MaxNoteLength = 120;
  public const int MaxUnitLength = 12;

  private readonly IMedicationRepository _medications;
  private readonly IPersonRepository _persons;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly ITodayCacheRepository _todayCache;
  private readonly TimeProvider _clock;

  public MedicationService(
    IMedicationRepository medications,
    IPersonRepository persons,
    IEncryptionService encryption,
    IAuditService audit,
    ITodayCacheRepository todayCache,
    TimeProvider clock)
  {
    _medications = medications;
    _persons = persons;
    _encryption = encryption;
    _audit = audit;
    _todayCache = todayCache;
    _clock = clock;
  }

  public async Task<IReadOnlyList<MedicationDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var rows = await _medications.ListAsync(viewerPersonId, targetPersonId, ct);
    if (rows == null) return null;
    var today = await TodayAsync(targetPersonId, ct);
    return rows
      .Select(m => m.ToDto(_encryption, today))
      .OrderByDescending(m => m.Active)
      .ThenBy(m => m.Kind, StringComparer.Ordinal)
      .ThenBy(m => m.Product, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  public async Task<MedicationDto> CreateAsync(Actor actor, SaveMedicationRequest request, CancellationToken ct = default)
  {
    var detail = Validate(request);
    var existing = await _medications.ListOwnAsync(actor.PersonId, ct);
    if (existing.Count >= MaxItems) throw new DomainValidationException($"At most {MaxItems} items. Remove one you no longer take.");

    var now = _clock.GetUtcNow().UtcDateTime;
    var medication = new Medication
    {
      Id = Guid.CreateVersion7(),
      PersonId = actor.PersonId,
      CreatedAt = now,
    };
    Apply(medication, request, detail, now);
    await _medications.AddAsync(medication, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);

    await RecordAsync(AuditActions.MedicationSaved, actor, medication, ct);
    return medication.ToDto(_encryption, await TodayAsync(actor.PersonId, ct));
  }

  public async Task<MedicationDto?> UpdateAsync(Actor actor, Guid medicationId, SaveMedicationRequest request, CancellationToken ct = default)
  {
    var medication = await _medications.GetOwnAsync(actor.PersonId, medicationId, ct);
    if (medication == null) return null;
    var detail = Validate(request);

    Apply(medication, request, detail, _clock.GetUtcNow().UtcDateTime);
    await _medications.UpdateAsync(medication, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);

    await RecordAsync(AuditActions.MedicationSaved, actor, medication, ct);
    return medication.ToDto(_encryption, await TodayAsync(actor.PersonId, ct));
  }

  public async Task<bool> DeleteAsync(Actor actor, Guid medicationId, CancellationToken ct = default)
  {
    var medication = await _medications.GetOwnAsync(actor.PersonId, medicationId, ct);
    if (medication == null) return false;
    await _medications.RemoveAsync(medication, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);
    await RecordAsync(AuditActions.MedicationDeleted, actor, medication, ct);
    return true;
  }

  private async Task<DateOnly> TodayAsync(Guid personId, CancellationToken ct)
  {
    var person = await _persons.GetByIdAsync(personId, ct);
    return DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person?.TimeZone));
  }

  /// <summary>Checks the request and returns the cleaned detail that gets encrypted.</summary>
  private static MedicationDetail Validate(SaveMedicationRequest request)
  {
    if (!MedicationKinds.All.Contains(request.Kind)) throw new DomainValidationException($"kind must be one of: {string.Join(", ", MedicationKinds.All)}.");
    if (request.Schedule != null && !MedicationSchedules.All.Contains(request.Schedule))
    {
      throw new DomainValidationException($"schedule must be one of: {string.Join(", ", MedicationSchedules.All)}.");
    }
    var product = Clean(request.Product, "product", MaxNameLength)
      ?? throw new DomainValidationException("Say what the product is.");
    var brand = Clean(request.Brand, "brand", MaxNameLength);
    var dose = Clean(request.Dose, "dose", MaxNoteLength);
    var note = Clean(request.ScheduleNote, "scheduleNote", MaxNoteLength);

    var ingredients = new List<MedicationIngredientDto>();
    foreach (var ingredient in request.Ingredients ?? [])
    {
      var name = Clean(ingredient.Name, "ingredient name", MaxNameLength)
        ?? throw new DomainValidationException("Every ingredient needs a name.");
      if (ingredient.Amount is { } amount && (!double.IsFinite(amount) || amount < 0))
      {
        throw new DomainValidationException($"The amount of {name} must be zero or more.");
      }
      var unit = Clean(ingredient.Unit, "ingredient unit", MaxUnitLength);
      ingredients.Add(new MedicationIngredientDto(name, ingredient.Amount, unit));
    }
    if (ingredients.Count > MaxIngredients) throw new DomainValidationException($"At most {MaxIngredients} ingredients.");
    if (request is { StartedOn: { } started, StoppedOn: { } stopped } && stopped < started)
    {
      throw new DomainValidationException("stoppedOn cannot be before startedOn.");
    }
    return new MedicationDetail(brand, product, dose, note, ingredients);
  }

  private static string? Clean(string? value, string name, int max)
  {
    if (string.IsNullOrWhiteSpace(value)) return null;
    var trimmed = value.Trim();
    if (trimmed.Length > max) throw new DomainValidationException($"{name} is at most {max} characters.");
    return trimmed;
  }

  private void Apply(Medication medication, SaveMedicationRequest request, MedicationDetail detail, DateTime now)
  {
    medication.Kind = request.Kind;
    medication.DetailCipher = BodyMapping.WriteDetail(detail, _encryption);
    medication.Generic = request.Generic;
    medication.Schedule = request.Schedule ?? MedicationSchedules.Daily;
    medication.Substances = SubstanceTagger.Tag([detail.Product, detail.Brand, .. detail.Ingredients.Select(i => i.Name)]);
    medication.StartedOn = request.StartedOn;
    medication.StoppedOn = request.StoppedOn;
    medication.UpdatedAt = now;
  }

  // The audit row says that the list changed, never what is on it.
  private Task RecordAsync(string action, Actor actor, Medication medication, CancellationToken ct) =>
    _audit.RecordAsync(new AuditEvent(
      action, actor.UserId, actor.HouseholdId, "medication", medication.Id.ToString(),
      new { kind = medication.Kind }, actor.Info.IpAddress), ct);
}
