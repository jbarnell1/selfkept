using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The Oura card on the Sources screen (docs/connect-design.md, "Oura"). The callback is where
/// Oura sends the browser back; it is anonymous by protocol and identifies the person by the
/// single-use state it was given.
/// </summary>
[Route("api/oura")]
public sealed class OuraController : BaseController
{
  private readonly IOuraService _oura;

  public OuraController(IOuraService oura) => _oura = oura;

  [HttpGet]
  [Authorize]
  public async Task<IActionResult> Status(CancellationToken ct) =>
    Ok(await _oura.GetStatusAsync(GetCurrentPersonId(), ct));

  [HttpPost("connect")]
  [Authorize]
  public async Task<IActionResult> Connect([FromQuery] string? returnTo, CancellationToken ct)
  {
    if (!await _oura.IsConfiguredAsync(ct))
      return NotFoundProblem("Oura is not set up on this server yet. An admin can add it under You, Sources.");
    return Ok(await _oura.StartConnectAsync(GetCurrentUserId(), GetCurrentPersonId(), returnTo, ct));
  }

  [HttpGet("callback")]
  [AllowAnonymous]
  public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct) =>
    Redirect(await _oura.CompleteConnectAsync(code, state, error, CurrentRequestInfo(), ct));

  [HttpPost("sync")]
  [Authorize]
  public async Task<IActionResult> Sync(CancellationToken ct) =>
    await _oura.RequestPullAsync(GetCurrentPersonId(), ct) ? Accepted() : NotFoundProblem("Oura is not connected.");

  [HttpDelete]
  [Authorize]
  public async Task<IActionResult> Disconnect(CancellationToken ct) =>
    await _oura.DisconnectAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), CurrentRequestInfo(), ct)
      ? NoContent()
      : NotFoundProblem("Oura is not connected.");
}
