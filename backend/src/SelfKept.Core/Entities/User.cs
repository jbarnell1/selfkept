namespace SelfKept.Core.Entities;

/// <summary>
/// A sign-in account. Every account owns exactly one <see cref="Person"/> (its own health
/// profile); a person without an account is a dependent managed by someone else.
/// </summary>
public class User
{
  public Guid Id { get; set; }

  /// <summary>Stored lower-cased; unique.</summary>
  public string Email { get; set; } = string.Empty;

  public string PasswordHash { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public Guid PersonId { get; set; }
  public Guid? HouseholdId { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
  public DateTime? LastLoginAt { get; set; }

  /// <summary>The <see cref="Enums.SetupItem"/> names this person picked in the first-run questions.</summary>
  public string[] SetupPlan { get; set; } = [];

  /// <summary>Null until the person finishes or skips the first-run setup.</summary>
  public DateTime? SetupCompletedAt { get; set; }

  public Person Person { get; set; } = null!;
  public Household? Household { get; set; }
}
