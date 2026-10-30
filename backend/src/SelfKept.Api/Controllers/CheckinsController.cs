using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>Mood check-ins. The note is encrypted at rest; each check-in also writes a checkin_mood sample.</summary>
[Authorize]
public sealed class CheckinsController : BaseController
{
  private readonly ICheckinService _checkins;

  public CheckinsController(ICheckinService checkins)
  {
    _checkins = checkins;
  }

  [HttpPost]
  public async Task<IActionResult> Create([FromBody] CreateCheckinRequest request, CancellationToken ct) =>
    Ok(await _checkins.CreateAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), request, CurrentRequestInfo(), ct));

  /// <summary>Newest first, at most 500. personId must be a household member who shares check-ins with you.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _checkins.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), from, to, ct), NotShared);

  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
    await _checkins.DeleteAsync(GetCurrentPersonId(), id, ct) ? NoContent() : NotFoundProblem("No such check-in.");

  private const string NotShared = "No such person, or they have not shared this with you.";
}
