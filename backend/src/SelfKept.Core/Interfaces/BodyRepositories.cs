using SelfKept.Core.Entities;

namespace SelfKept.Core.Interfaces;

// Body, goals and the day log (docs/body-design.md). Reads by a viewer take (viewerPersonId,
// targetPersonId) and return null on a denial; "Own" methods are the owner's writes and the
// server's own reads for rules, where no grant applies.

/// <summary>Goals are read under the body grant.</summary>
public interface IGoalRepository
{
  /// <summary>Newest first. Null when the viewer may not see the target's body data.</summary>
  Task<IReadOnlyList<Goal>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, bool includeEnded, CancellationToken ct = default);

  Task<IReadOnlyList<Goal>> ListOwnActiveAsync(Guid personId, CancellationToken ct = default);
  Task<Goal?> GetOwnAsync(Guid personId, Guid goalId, CancellationToken ct = default);
  Task<Goal> AddAsync(Goal goal, CancellationToken ct = default);
  Task UpdateAsync(Goal goal, CancellationToken ct = default);
}

/// <summary>Targets are read under the body grant.</summary>
public interface IDailyTargetRepository
{
  /// <summary>In display order. Null when the viewer may not see the target's body data.</summary>
  Task<IReadOnlyList<DailyTarget>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  Task<IReadOnlyList<DailyTarget>> ListOwnAsync(Guid personId, CancellationToken ct = default);
  Task<DailyTarget?> GetOwnAsync(Guid personId, Guid targetId, CancellationToken ct = default);
  Task<DailyTarget> AddAsync(DailyTarget target, CancellationToken ct = default);
  Task UpdateAsync(DailyTarget target, CancellationToken ct = default);
  Task RemoveAsync(DailyTarget target, CancellationToken ct = default);
}

/// <summary>The day log is read under the check-ins grant.</summary>
public interface IJournalRepository
{
  /// <summary>Newest first, at most limit rows. Null when the viewer may not see the target's check-ins.</summary>
  Task<IReadOnlyList<JournalEntry>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly from, DateOnly to, int limit, CancellationToken ct = default);

  /// <summary>Server-internal read for tallies, patterns and rules: the person's own rows, oldest first.</summary>
  Task<IReadOnlyList<JournalEntry>> ListOwnAsync(Guid personId, DateOnly from, DateOnly to, CancellationToken ct = default);

  Task<JournalEntry?> GetOwnAsync(Guid personId, Guid entryId, CancellationToken ct = default);

  /// <summary>The person's entries saved from one assistant message, so that save can be undone.</summary>
  Task<IReadOnlyList<JournalEntry>> ListOwnByMessageAsync(Guid personId, Guid messageId, CancellationToken ct = default);

  Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken ct = default);
  Task RemoveAsync(JournalEntry entry, CancellationToken ct = default);
}

/// <summary>Medicines and supplements are read under the labs grant: they explain blood work.</summary>
public interface IMedicationRepository
{
  /// <summary>Current ones first, then newest. Null when the viewer may not see the target's labs.</summary>
  Task<IReadOnlyList<Medication>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  Task<IReadOnlyList<Medication>> ListOwnAsync(Guid personId, CancellationToken ct = default);
  Task<Medication?> GetOwnAsync(Guid personId, Guid medicationId, CancellationToken ct = default);
  Task<Medication> AddAsync(Medication medication, CancellationToken ct = default);
  Task UpdateAsync(Medication medication, CancellationToken ct = default);
  Task RemoveAsync(Medication medication, CancellationToken ct = default);
}

/// <summary>The strength log is read under the activity grant.</summary>
public interface IStrengthRepository
{
  /// <summary>Oldest first. Null when the viewer may not see the target's activity.</summary>
  Task<IReadOnlyList<StrengthSet>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly from, DateOnly to, CancellationToken ct = default);

  Task<IReadOnlyList<StrengthSet>> ListOwnAsync(Guid personId, DateOnly from, DateOnly to, CancellationToken ct = default);
  Task<StrengthSet?> GetOwnAsync(Guid personId, Guid setId, CancellationToken ct = default);
  Task<StrengthSet?> FindOwnAsync(Guid personId, DateOnly day, string lift, CancellationToken ct = default);
  Task<StrengthSet> AddAsync(StrengthSet set, CancellationToken ct = default);
  Task UpdateAsync(StrengthSet set, CancellationToken ct = default);
  Task RemoveAsync(StrengthSet set, CancellationToken ct = default);
}
