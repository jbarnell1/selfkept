using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Core.Interfaces;

/// <summary>
/// Wraps a unit of work in one database transaction. Repositories save per call; the transaction
/// makes a multi-step service operation (create person, user, household, membership) atomic.
/// An optional advisory-lock key serializes concurrent callers for the life of the transaction,
/// which is how two sign-ups on a fresh server cannot both become the first user.
/// </summary>
public interface IUnitOfWork
{
  Task<T> RunInTransactionAsync<T>(Func<Task<T>> work, long? serializationKey = null, CancellationToken ct = default);
}

public interface IUserRepository
{
  Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
  Task<bool> AnyAsync(CancellationToken ct = default);
  Task<User> AddAsync(User user, CancellationToken ct = default);
  Task UpdateAsync(User user, CancellationToken ct = default);
}

public interface IPersonRepository
{
  Task<Person?> GetByIdAsync(Guid id, CancellationToken ct = default);

  /// <summary>Every person on the server, for the scheduled jobs. Not a viewer read.</summary>
  Task<IReadOnlyList<Person>> ListAllAsync(CancellationToken ct = default);
  Task<Person> AddAsync(Person person, CancellationToken ct = default);
  Task UpdateAsync(Person person, CancellationToken ct = default);
}

public interface IHouseholdRepository
{
  /// <summary>The household with its members and their persons loaded.</summary>
  Task<Household?> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<Household> AddAsync(Household household, CancellationToken ct = default);
  Task UpdateAsync(Household household, CancellationToken ct = default);
  Task<HouseholdMember> AddMemberAsync(HouseholdMember member, CancellationToken ct = default);
  Task<HouseholdMember?> GetMembershipForUserAsync(Guid userId, CancellationToken ct = default);
  Task<HouseholdMember?> GetMemberByPersonAsync(Guid householdId, Guid personId, CancellationToken ct = default);
  Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(Guid householdId, CancellationToken ct = default);
}

public interface IInviteRepository
{
  Task<HouseholdInvite?> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<HouseholdInvite?> GetByCodeHashAsync(string codeHash, CancellationToken ct = default);
  Task<IReadOnlyList<HouseholdInvite>> GetPendingAsync(Guid householdId, DateTime utcNow, CancellationToken ct = default);
  Task<HouseholdInvite> AddAsync(HouseholdInvite invite, CancellationToken ct = default);
  Task RemoveAsync(HouseholdInvite invite, CancellationToken ct = default);

  /// <summary>
  /// Spends the invite. Returns false when another registration spent it first (the used_at
  /// concurrency token no longer matches), so the caller can roll back.
  /// </summary>
  Task<bool> TryMarkUsedAsync(HouseholdInvite invite, Guid usedByUserId, DateTime utcNow, CancellationToken ct = default);
}

public interface IShareGrantRepository
{
  Task<IReadOnlyList<ShareGrant>> GetByOwnerAsync(Guid ownerPersonId, CancellationToken ct = default);
  Task<IReadOnlyList<ShareGrant>> GetByViewerAsync(Guid viewerPersonId, CancellationToken ct = default);
  Task<bool> ExistsAsync(Guid ownerPersonId, Guid viewerPersonId, ShareCategory category, CancellationToken ct = default);

  /// <summary>Replaces every grant from owner to viewer with exactly the given categories.</summary>
  Task ReplaceAsync(Guid householdId, Guid ownerPersonId, Guid viewerPersonId, IReadOnlyCollection<ShareCategory> categories, CancellationToken ct = default);
}
