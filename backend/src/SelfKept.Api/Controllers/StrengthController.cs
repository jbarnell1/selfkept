using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>The strength log: one top set per lift per day (docs/body-design.md, "Strength").</summary>
[Authorize]
public sealed class StrengthController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IStrengthService _strength;

  public StrengthController(IStrengthService strength) => _strength = strength;

  /// <summary>Sets grouped per lift. Defaults to the last 120 days. Needs the activity share for someone else.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _strength.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), from, to, ct), NotShared);

  /// <summary>Saves the top set of the day for a lift. Saving the same lift on the same day replaces it.</summary>
  [HttpPost]
  public async Task<IActionResult> Save([FromBody] SaveStrengthSetRequest request, CancellationToken ct) =>
    Ok(await _strength.SaveAsync(CurrentActor(), request, ct));

  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
    await _strength.DeleteAsync(CurrentActor(), id, ct) ? NoContent() : NotFoundProblem("No such set.");
}
