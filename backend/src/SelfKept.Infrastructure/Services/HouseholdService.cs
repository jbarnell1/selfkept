using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services;

public sealed class HouseholdService : IHouseholdService
{
  private readonly IHouseholdRepository _households;
  private readonly IShareGrantRepository _grants;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;

  public HouseholdService(IHouseholdRepository households, IShareGrantRepository grants, IAuditService audit, TimeProvider clock)
  {
    _households = households;
    _grants = grants;
    _audit = audit;
    _clock = clock;
  }

  public async Task<HouseholdDto?> GetForUserAsync(Guid userId, CancellationToken ct = default)
  {
    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    if (membership == null) return null;
    var household = await _households.GetByIdAsync(membership.HouseholdId, ct);
    return household?.ToDto();
  }

  public async Task<HouseholdDto?> RenameAsync(Guid userId, string name, CancellationToken ct = default)
  {
    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    if (membership == null) return null;
    if (membership.Role != HouseholdRole.Admin) throw new ForbiddenException("Only the household admin can rename it.");

    var trimmed = name.Trim();
    if (trimmed.Length == 0) throw new DomainValidationException("Household name is required.");

    var household = await _households.GetByIdAsync(membership.HouseholdId, ct);
    if (household == null) return null;
    household.Name = trimmed;
    household.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
    await _households.UpdateAsync(household, ct);
    return household.ToDto();
  }

  public async Task<IReadOnlyList<ShareGrantDto>> GetSharesGivenAsync(Guid userId, CancellationToken ct = default)
  {
    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    if (membership == null) return Array.Empty<ShareGrantDto>();

    var grants = await _grants.GetByOwnerAsync(membership.PersonId, ct);
    var members = await _households.GetMembersAsync(membership.HouseholdId, ct);
    return Summarise(grants, g => g.ViewerPersonId, members);
  }

  public async Task<ShareGrantDto?> ReplaceSharesAsync(Guid userId, Guid memberPersonId, IReadOnlyCollection<ShareCategory> categories, RequestInfo info, CancellationToken ct = default)
  {
    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    if (membership == null) return null;
    if (memberPersonId == membership.PersonId) throw new DomainValidationException("You cannot share data with yourself.");

    var target = await _households.GetMemberByPersonAsync(membership.HouseholdId, memberPersonId, ct);
    if (target == null) return null;

    var wanted = categories.Distinct().OrderBy(c => c).ToList();
    await _grants.ReplaceAsync(membership.HouseholdId, membership.PersonId, memberPersonId, wanted, ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.ShareChanged, userId, membership.HouseholdId, "person", memberPersonId.ToString(),
      new { categories = wanted.Select(c => c.ToString().ToLowerInvariant()).ToArray() }, info.IpAddress), ct);

    return new ShareGrantDto(memberPersonId, target.Person.DisplayName, wanted);
  }

  public async Task<IReadOnlyList<ShareGrantDto>> GetSharedWithMeAsync(Guid userId, CancellationToken ct = default)
  {
    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    if (membership == null) return Array.Empty<ShareGrantDto>();

    var grants = await _grants.GetByViewerAsync(membership.PersonId, ct);
    var members = await _households.GetMembersAsync(membership.HouseholdId, ct);
    return Summarise(grants, g => g.OwnerPersonId, members);
  }

  private static IReadOnlyList<ShareGrantDto> Summarise(
    IReadOnlyList<ShareGrant> grants, Func<ShareGrant, Guid> counterpart, IReadOnlyList<HouseholdMember> members)
  {
    var names = members.ToDictionary(m => m.PersonId, m => m.Person.DisplayName);
    return grants
      .GroupBy(counterpart)
      .Select(g => new ShareGrantDto(
        g.Key,
        names.TryGetValue(g.Key, out var name) ? name : string.Empty,
        g.Select(x => x.Category).Distinct().OrderBy(c => c).ToList()))
      .OrderBy(d => d.DisplayName)
      .ToList();
  }
}
