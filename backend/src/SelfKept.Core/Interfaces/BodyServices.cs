using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;

namespace SelfKept.Core.Interfaces;

// Body, goals and the day log (docs/body-design.md). Reads take (viewerPersonId, targetPersonId)
// and answer null when the viewer may not see the target's data; writes are always the caller's own.

/// <summary>Who is writing: the signed-in user, their person, their household and where the request came from.</summary>
public sealed record Actor(Guid UserId, Guid PersonId, Guid? HouseholdId, RequestInfo Info);

public interface IGoalService
{
  /// <summary>Null when the viewer may not see the target's body data.</summary>
  Task<IReadOnlyList<GoalDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, bool includeEnded, CancellationToken ct = default);

  /// <summary>Starts a goal. An active goal already in the slot ends as dropped.</summary>
  Task<GoalDto> CreateAsync(Actor actor, SaveGoalRequest request, CancellationToken ct = default);

  /// <summary>Null when the goal is not the caller's.</summary>
  Task<GoalDto?> UpdateAsync(Actor actor, Guid goalId, SaveGoalRequest request, CancellationToken ct = default);

  /// <summary>Null when the goal is not the caller's.</summary>
  Task<GoalDto?> EndAsync(Actor actor, Guid goalId, EndGoalRequest request, CancellationToken ct = default);
}

public interface ITargetService
{
  /// <summary>Null when the viewer may not see the target's body data.</summary>
  Task<IReadOnlyList<TargetDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  Task<TargetDto> CreateAsync(Actor actor, SaveTargetRequest request, CancellationToken ct = default);

  /// <summary>Null when the target is not the caller's.</summary>
  Task<TargetDto?> UpdateAsync(Actor actor, Guid targetId, SaveTargetRequest request, CancellationToken ct = default);

  /// <summary>False when the target is not the caller's.</summary>
  Task<bool> DeleteAsync(Actor actor, Guid targetId, CancellationToken ct = default);

  /// <summary>
  /// The active goals and where each active target stands today and over the last seven days.
  /// Null when the viewer may not see the target's body data. A target whose inputs the viewer
  /// may not read (the day log, the strength log, another category's metric) comes back unknown.
  /// </summary>
  Task<GoalTallyDto?> TallyAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  /// <summary>The person's own active targets tallied over the seven days ending on a given day. Server-internal.</summary>
  Task<IReadOnlyList<TargetTallyDto>> TallyOwnAsync(Guid personId, DateOnly day, CancellationToken ct = default);
}

/// <summary>A day log entry to save on a person's behalf (the assistant and the evening check-in use this).</summary>
public sealed record JournalDraft(
  string Kind,
  string? Text,
  IReadOnlyList<string> Tags,
  string? Portion,
  DateTime? At,
  string Source,
  Guid? MessageId,
  JournalNutritionDto? Nutrition = null);

public interface IJournalService
{
  Task<JournalEntryDto> CreateAsync(Actor actor, CreateJournalEntryRequest request, CancellationToken ct = default);

  /// <summary>Saves an entry for the person from inside the server (assistant capture, evening check-in).</summary>
  Task<JournalEntryDto> SaveDraftAsync(Actor actor, JournalDraft draft, CancellationToken ct = default);

  /// <summary>Newest first. Null when the viewer may not see the target's check-ins.</summary>
  Task<IReadOnlyList<JournalEntryDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

  /// <summary>False when the entry is not the caller's. An entry's numbers go with it and the day is recomputed.</summary>
  Task<bool> DeleteAsync(Actor actor, Guid entryId, CancellationToken ct = default);

  /// <summary>Removes everything saved from one assistant message. Returns how many entries went.</summary>
  Task<int> DeleteByMessageAsync(Actor actor, Guid messageId, CancellationToken ct = default);

  /// <summary>The chips: every known tag grouped by kind.</summary>
  IReadOnlyList<JournalTagGroupDto> TagGroups();
}

public interface IMedicationService
{
  /// <summary>Current ones first. Null when the viewer may not see the target's labs.</summary>
  Task<IReadOnlyList<MedicationDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  Task<MedicationDto> CreateAsync(Actor actor, SaveMedicationRequest request, CancellationToken ct = default);

  /// <summary>Null when the row is not the caller's.</summary>
  Task<MedicationDto?> UpdateAsync(Actor actor, Guid medicationId, SaveMedicationRequest request, CancellationToken ct = default);

  /// <summary>False when the row is not the caller's.</summary>
  Task<bool> DeleteAsync(Actor actor, Guid medicationId, CancellationToken ct = default);
}

public interface IStrengthService
{
  /// <summary>One group per lift, oldest set first. Null when the viewer may not see the target's activity.</summary>
  Task<IReadOnlyList<StrengthLiftDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

  /// <summary>Saves the day's top set of a lift, replacing the one already there for that day.</summary>
  Task<StrengthSetDto> SaveAsync(Actor actor, SaveStrengthSetRequest request, CancellationToken ct = default);

  /// <summary>False when the set is not the caller's.</summary>
  Task<bool> DeleteAsync(Actor actor, Guid setId, CancellationToken ct = default);
}

public interface IMeasurementService
{
  /// <summary>Writes a typed-in weight, body fat or waist reading as a sample under the person's "Typed in" source.</summary>
  Task<MeasurementDto> CreateAsync(Actor actor, CreateMeasurementRequest request, CancellationToken ct = default);

  /// <summary>The caller's own typed-in readings of one metric, newest first.</summary>
  Task<IReadOnlyList<MeasurementDto>> ListOwnAsync(Guid personId, string metricKey, CancellationToken ct = default);

  /// <summary>False when there is no such typed-in reading of the caller's.</summary>
  Task<bool> DeleteAsync(Actor actor, string metricKey, Guid measurementId, CancellationToken ct = default);
}

public interface IProgressService
{
  /// <summary>
  /// GET /api/progress: goals with pace, the target tally, the body snapshot and series, the
  /// strength log and goal suggestions. Null when the viewer may not see the target's body data.
  /// </summary>
  Task<ProgressDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);
}

/// <summary>Loads what the body and goal rules read for one person and day. Server-internal: the person's own data.</summary>
public interface IBodyContextLoader
{
  Task<BodyContext> LoadAsync(Person person, DateOnly day, CancellationToken ct = default);
}
