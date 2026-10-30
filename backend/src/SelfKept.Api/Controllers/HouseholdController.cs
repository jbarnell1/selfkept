using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Auth;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>The caller's household: members, invites and what the caller shares with whom.</summary>
[Authorize]
public sealed class HouseholdController : BaseController
{
  private const string NoHousehold = "You are not in a household.";

  private readonly IHouseholdService _householdService;
  private readonly IInviteService _inviteService;

  public HouseholdController(IHouseholdService householdService, IInviteService inviteService)
  {
    _householdService = householdService;
    _inviteService = inviteService;
  }

  [HttpGet]
  public async Task<IActionResult> Get(CancellationToken ct) =>
    HandleServiceResponse(await _householdService.GetForUserAsync(GetCurrentUserId(), ct), NoHousehold);

  [HttpPut]
  [Authorize(Roles = TokenRoles.Admin)]
  public async Task<IActionResult> Rename([FromBody] UpdateHouseholdRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _householdService.RenameAsync(GetCurrentUserId(), request.Name, ct), NoHousehold);

  /// <summary>The code is in this response and nowhere else: only its hash is stored.</summary>
  [HttpPost("invites")]
  [Authorize(Roles = TokenRoles.Admin)]
  public async Task<IActionResult> CreateInvite(CancellationToken ct)
  {
    if (GetCurrentHouseholdId() is not { } householdId) return NotFoundProblem(NoHousehold);
    var invite = await _inviteService.CreateAsync(householdId, GetCurrentUserId(), CurrentRequestInfo(), ct);
    return Ok(invite);
  }

  [HttpGet("invites")]
  [Authorize(Roles = TokenRoles.Admin)]
  public async Task<IActionResult> ListInvites(CancellationToken ct)
  {
    if (GetCurrentHouseholdId() is not { } householdId) return NotFoundProblem(NoHousehold);
    return Ok(await _inviteService.ListPendingAsync(householdId, ct));
  }

  [HttpDelete("invites/{id:guid}")]
  [Authorize(Roles = TokenRoles.Admin)]
  public async Task<IActionResult> RevokeInvite(Guid id, CancellationToken ct)
  {
    if (GetCurrentHouseholdId() is not { } householdId) return NotFoundProblem(NoHousehold);
    return await _inviteService.RevokeAsync(householdId, id, ct) ? NoContent() : NotFoundProblem("No pending invite with that id.");
  }

  /// <summary>Grants I have given, one entry per member.</summary>
  [HttpGet("shares")]
  public async Task<IActionResult> GetShares(CancellationToken ct) =>
    Ok(await _householdService.GetSharesGivenAsync(GetCurrentUserId(), ct));

  /// <summary>Replaces my grants to one member with exactly these categories (an empty list revokes all).</summary>
  [HttpPut("shares/{memberPersonId:guid}")]
  public async Task<IActionResult> ReplaceShares(Guid memberPersonId, [FromBody] ReplaceSharesRequest request, CancellationToken ct) =>
    HandleServiceResponse(
      await _householdService.ReplaceSharesAsync(GetCurrentUserId(), memberPersonId, request.Categories, CurrentRequestInfo(), ct),
      "That person is not a member of your household.");

  [HttpGet("shared-with-me")]
  public async Task<IActionResult> GetSharedWithMe(CancellationToken ct) =>
    Ok(await _householdService.GetSharedWithMeAsync(GetCurrentUserId(), ct));
}
