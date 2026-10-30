using SelfKept.Core.DTOs;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services;

/// <summary>Stores each account's first-run answers and when it finished them.</summary>
public sealed class SetupService : ISetupService
{
  private readonly IUserRepository _users;
  private readonly IHouseholdRepository _households;
  private readonly TimeProvider _clock;

  public SetupService(IUserRepository users, IHouseholdRepository households, TimeProvider clock)
  {
    _users = users;
    _households = households;
    _clock = clock;
  }

  public async Task<UserDto?> SaveAsync(Guid userId, SaveSetupRequest request, CancellationToken ct = default)
  {
    var user = await _users.GetByIdAsync(userId, ct);
    if (user == null) return null;

    user.SetupPlan = request.Plan.Distinct().Select(item => item.ToString()).ToArray();
    if (request.Completed && user.SetupCompletedAt == null)
      user.SetupCompletedAt = _clock.GetUtcNow().UtcDateTime;
    user.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
    await _users.UpdateAsync(user, ct);

    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    return user.ToDto(membership?.Role ?? HouseholdRole.Member);
  }
}
