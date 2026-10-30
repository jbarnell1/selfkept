using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Repositories;

/// <summary>
/// One transaction around a multi-step write, with an optional transaction-scoped advisory lock
/// that serializes concurrent callers until commit. Without a relational provider (the InMemory
/// test database) there is nothing to lock and the work simply runs.
/// </summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
  private readonly SelfKeptDbContext _context;

  public EfUnitOfWork(SelfKeptDbContext context)
  {
    _context = context;
  }

  public async Task<T> RunInTransactionAsync<T>(Func<Task<T>> work, long? serializationKey = null, CancellationToken ct = default)
  {
    if (!_context.Database.IsRelational())
    {
      return await work();
    }

    if (_context.Database.CurrentTransaction != null)
    {
      // Already inside a transaction (nested service call): join it.
      return await work();
    }

    await using var transaction = await _context.Database.BeginTransactionAsync(ct);
    if (serializationKey is { } key)
    {
      await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", new object[] { key }, ct);
    }
    var result = await work();
    await transaction.CommitAsync(ct);
    return result;
  }
}

public sealed class UserRepository : IUserRepository
{
  private readonly SelfKeptDbContext _context;

  public UserRepository(SelfKeptDbContext context)
  {
    _context = context;
  }

  public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
    _context.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == id, ct);

  public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
  {
    var normalized = email.Trim().ToLowerInvariant();
    return _context.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Email == normalized, ct);
  }

  public Task<bool> AnyAsync(CancellationToken ct = default) => _context.Users.AnyAsync(ct);

  public async Task<User> AddAsync(User user, CancellationToken ct = default)
  {
    user.Email = user.Email.Trim().ToLowerInvariant();
    _context.Users.Add(user);
    await _context.SaveChangesAsync(ct);
    return user;
  }

  public async Task UpdateAsync(User user, CancellationToken ct = default)
  {
    _context.Users.Update(user);
    await _context.SaveChangesAsync(ct);
  }
}

public sealed class PersonRepository : IPersonRepository
{
  private readonly SelfKeptDbContext _context;

  public PersonRepository(SelfKeptDbContext context)
  {
    _context = context;
  }

  public Task<Person?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
    _context.Persons.FirstOrDefaultAsync(p => p.Id == id, ct);

  public async Task<IReadOnlyList<Person>> ListAllAsync(CancellationToken ct = default) =>
    await _context.Persons.AsNoTracking().OrderBy(p => p.CreatedAt).ToListAsync(ct);

  public async Task<Person> AddAsync(Person person, CancellationToken ct = default)
  {
    _context.Persons.Add(person);
    await _context.SaveChangesAsync(ct);
    return person;
  }

  public async Task UpdateAsync(Person person, CancellationToken ct = default)
  {
    _context.Persons.Update(person);
    await _context.SaveChangesAsync(ct);
  }
}

public sealed class HouseholdRepository : IHouseholdRepository
{
  private readonly SelfKeptDbContext _context;

  public HouseholdRepository(SelfKeptDbContext context)
  {
    _context = context;
  }

  public Task<Household?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
    _context.Households
      .Include(h => h.Members).ThenInclude(m => m.Person)
      .FirstOrDefaultAsync(h => h.Id == id, ct);

  public async Task<Household> AddAsync(Household household, CancellationToken ct = default)
  {
    _context.Households.Add(household);
    await _context.SaveChangesAsync(ct);
    return household;
  }

  public async Task UpdateAsync(Household household, CancellationToken ct = default)
  {
    _context.Households.Update(household);
    await _context.SaveChangesAsync(ct);
  }

  public async Task<HouseholdMember> AddMemberAsync(HouseholdMember member, CancellationToken ct = default)
  {
    _context.HouseholdMembers.Add(member);
    await _context.SaveChangesAsync(ct);
    return member;
  }

  public Task<HouseholdMember?> GetMembershipForUserAsync(Guid userId, CancellationToken ct = default) =>
    _context.HouseholdMembers.Include(m => m.Person).FirstOrDefaultAsync(m => m.UserId == userId, ct);

  public Task<HouseholdMember?> GetMemberByPersonAsync(Guid householdId, Guid personId, CancellationToken ct = default) =>
    _context.HouseholdMembers.Include(m => m.Person)
      .FirstOrDefaultAsync(m => m.HouseholdId == householdId && m.PersonId == personId, ct);

  public async Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(Guid householdId, CancellationToken ct = default) =>
    await _context.HouseholdMembers.Include(m => m.Person)
      .Where(m => m.HouseholdId == householdId)
      .OrderBy(m => m.JoinedAt)
      .ToListAsync(ct);
}

public sealed class InviteRepository : IInviteRepository
{
  private readonly SelfKeptDbContext _context;

  public InviteRepository(SelfKeptDbContext context)
  {
    _context = context;
  }

  public Task<HouseholdInvite?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
    _context.HouseholdInvites.FirstOrDefaultAsync(i => i.Id == id, ct);

  public Task<HouseholdInvite?> GetByCodeHashAsync(string codeHash, CancellationToken ct = default) =>
    _context.HouseholdInvites.FirstOrDefaultAsync(i => i.CodeHash == codeHash, ct);

  public async Task<IReadOnlyList<HouseholdInvite>> GetPendingAsync(Guid householdId, DateTime utcNow, CancellationToken ct = default) =>
    await _context.HouseholdInvites
      .Where(i => i.HouseholdId == householdId && i.UsedAt == null && i.ExpiresAt > utcNow)
      .OrderBy(i => i.CreatedAt)
      .ToListAsync(ct);

  public async Task<HouseholdInvite> AddAsync(HouseholdInvite invite, CancellationToken ct = default)
  {
    _context.HouseholdInvites.Add(invite);
    await _context.SaveChangesAsync(ct);
    return invite;
  }

  public async Task RemoveAsync(HouseholdInvite invite, CancellationToken ct = default)
  {
    _context.HouseholdInvites.Remove(invite);
    await _context.SaveChangesAsync(ct);
  }

  public async Task<bool> TryMarkUsedAsync(HouseholdInvite invite, Guid usedByUserId, DateTime utcNow, CancellationToken ct = default)
  {
    if (invite.UsedAt != null) return false;
    invite.UsedByUserId = usedByUserId;
    invite.UsedAt = utcNow;
    try
    {
      // used_at is a concurrency token: the update only lands while it is still null.
      await _context.SaveChangesAsync(ct);
      return true;
    }
    catch (DbUpdateConcurrencyException)
    {
      return false;
    }
  }
}

public sealed class ShareGrantRepository : IShareGrantRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly TimeProvider _clock;

  public ShareGrantRepository(SelfKeptDbContext context, TimeProvider clock)
  {
    _context = context;
    _clock = clock;
  }

  public async Task<IReadOnlyList<ShareGrant>> GetByOwnerAsync(Guid ownerPersonId, CancellationToken ct = default) =>
    await _context.ShareGrants.AsNoTracking().Where(g => g.OwnerPersonId == ownerPersonId).ToListAsync(ct);

  public async Task<IReadOnlyList<ShareGrant>> GetByViewerAsync(Guid viewerPersonId, CancellationToken ct = default) =>
    await _context.ShareGrants.AsNoTracking().Where(g => g.ViewerPersonId == viewerPersonId).ToListAsync(ct);

  public Task<bool> ExistsAsync(Guid ownerPersonId, Guid viewerPersonId, ShareCategory category, CancellationToken ct = default) =>
    _context.ShareGrants.AnyAsync(
      g => g.OwnerPersonId == ownerPersonId && g.ViewerPersonId == viewerPersonId && g.Category == category, ct);

  public async Task ReplaceAsync(Guid householdId, Guid ownerPersonId, Guid viewerPersonId, IReadOnlyCollection<ShareCategory> categories, CancellationToken ct = default)
  {
    var wanted = categories.Distinct().ToHashSet();
    var existing = await _context.ShareGrants
      .Where(g => g.OwnerPersonId == ownerPersonId && g.ViewerPersonId == viewerPersonId)
      .ToListAsync(ct);

    foreach (var grant in existing.Where(g => !wanted.Contains(g.Category)))
    {
      _context.ShareGrants.Remove(grant);
    }

    var have = existing.Select(g => g.Category).ToHashSet();
    var now = _clock.GetUtcNow().UtcDateTime;
    foreach (var category in wanted.Where(c => !have.Contains(c)))
    {
      _context.ShareGrants.Add(new ShareGrant
      {
        Id = Guid.CreateVersion7(),
        HouseholdId = householdId,
        OwnerPersonId = ownerPersonId,
        ViewerPersonId = viewerPersonId,
        Category = category,
        CreatedAt = now,
      });
    }

    await _context.SaveChangesAsync(ct);
  }
}
