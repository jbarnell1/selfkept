using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Infrastructure.Services;

internal static class Mapping
{
  public static PersonDto ToDto(this Person p) =>
    new(p.Id, p.DisplayName, p.SexAtBirth, p.BirthDate, p.Units, p.TimeZone);

  public static UserDto ToDto(this User u, HouseholdRole role) =>
    new(u.Id, u.Email, u.DisplayName, role, u.HouseholdId, u.Person.ToDto(), SetupItems(u.SetupPlan), u.SetupCompletedAt);

  /// <summary>The stored names as items; a name a newer version wrote and this one does not know is skipped.</summary>
  public static IReadOnlyList<SetupItem> SetupItems(IEnumerable<string> names) =>
    names.Select(n => Enum.TryParse<SetupItem>(n, ignoreCase: true, out var item) ? item : (SetupItem?)null)
      .OfType<SetupItem>()
      .Distinct()
      .ToList();

  public static MemberDto ToDto(this HouseholdMember m) =>
    new(m.PersonId, m.UserId, m.Person.DisplayName, m.Role, m.JoinedAt);

  public static HouseholdDto ToDto(this Household h) =>
    new(h.Id, h.Name, h.Members.OrderBy(m => m.JoinedAt).Select(ToDto).ToList());
}
