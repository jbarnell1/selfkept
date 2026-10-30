using SelfKept.Core.Enums;

namespace SelfKept.Core.Entities;

/// <summary>The people who share one server. The first account creates it and is its admin.</summary>
public class Household
{
  public Guid Id { get; set; }
  public string Name { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }

  public ICollection<HouseholdMember> Members { get; set; } = new List<HouseholdMember>();
}

/// <summary>
/// A person's membership of a household. <see cref="UserId"/> is null for a dependent who has
/// no account of their own (not created by this package yet, but the shape allows it).
/// </summary>
public class HouseholdMember
{
  public Guid Id { get; set; }
  public Guid HouseholdId { get; set; }
  public Guid PersonId { get; set; }
  public Guid? UserId { get; set; }
  public HouseholdRole Role { get; set; } = HouseholdRole.Member;
  public DateTime JoinedAt { get; set; }

  public Household Household { get; set; } = null!;
  public Person Person { get; set; } = null!;
  public User? User { get; set; }
}

/// <summary>
/// A one-time code an admin hands out so another person can register. Only the SHA-256 hash is
/// stored; the plain code is shown once, when it is created. <see cref="UsedAt"/> is a
/// concurrency token so two registrations cannot both spend the same code.
/// </summary>
public class HouseholdInvite
{
  public Guid Id { get; set; }
  public Guid HouseholdId { get; set; }
  public string CodeHash { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime ExpiresAt { get; set; }
  public Guid? UsedByUserId { get; set; }
  public DateTime? UsedAt { get; set; }

  public Household Household { get; set; } = null!;

  public bool IsPending(DateTime utcNow) => UsedAt == null && ExpiresAt > utcNow;
}

/// <summary>
/// "Owner lets viewer see category." One row per (owner, viewer, category). Absence of a row
/// is a denial; a person can always see their own data (see IAccessPolicy).
/// </summary>
public class ShareGrant
{
  public Guid Id { get; set; }
  public Guid HouseholdId { get; set; }
  public Guid OwnerPersonId { get; set; }
  public Guid ViewerPersonId { get; set; }
  public ShareCategory Category { get; set; }
  public DateTime CreatedAt { get; set; }
}
