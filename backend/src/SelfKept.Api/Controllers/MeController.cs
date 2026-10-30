using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>The signed-in person's own profile, password and first-run setup.</summary>
[Authorize]
public sealed class MeController : BaseController
{
  private readonly IProfileService _profileService;
  private readonly ISetupService _setupService;

  public MeController(IProfileService profileService, ISetupService setupService)
  {
    _profileService = profileService;
    _setupService = setupService;
  }

  [HttpGet("profile")]
  public async Task<IActionResult> GetProfile(CancellationToken ct) =>
    HandleServiceResponse(await _profileService.GetAsync(GetCurrentUserId(), ct));

  [HttpPut("profile")]
  public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _profileService.UpdateAsync(GetCurrentUserId(), request, ct));

  [HttpPut("password")]
  public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
  {
    var changed = await _profileService.ChangePasswordAsync(GetCurrentUserId(), request.CurrentPassword, request.NewPassword, ct);
    return changed == null ? NotFoundProblem() : NoContent();
  }

  /// <summary>The first-run answers; returns the updated account so the client can move on.</summary>
  [HttpPut("setup")]
  public async Task<IActionResult> SaveSetup([FromBody] SaveSetupRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _setupService.SaveAsync(GetCurrentUserId(), request, ct));
}
