using SelfKept.Core.Enums;

namespace SelfKept.Core.Entities;

/// <summary>
/// The subject of health data. Every data row in later packages hangs off a person, and every
/// read is checked as (viewerPersonId, targetPersonId) against <see cref="ShareGrant"/>.
/// </summary>
public class Person
{
  public Guid Id { get; set; }
  public string DisplayName { get; set; } = string.Empty;
  public SexAtBirth SexAtBirth { get; set; } = SexAtBirth.Unspecified;
  public DateOnly? BirthDate { get; set; }
  public UnitSystem Units { get; set; } = UnitSystem.Metric;

  /// <summary>IANA time zone id, for example "Europe/London". Defaults to UTC.</summary>
  public string TimeZone { get; set; } = "UTC";

  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}
