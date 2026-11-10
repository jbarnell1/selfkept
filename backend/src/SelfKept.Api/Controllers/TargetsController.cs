using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>Daily and weekly targets, and the tally that says how the last seven days went.</summary>
[Authorize]
public sealed class TargetsController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly ITargetService _targets;

  public TargetsController(ITargetService targets) => _targets = targets;

  [HttpGet]
  public async Task<IActionResult> List([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _targets.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);

  /// <summary>Each active target against today and the six days before it. Worked out on read.</summary>
  [HttpGet("tally")]
  public async Task<IActionResult> Tally([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _targets.TallyAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);

  [HttpPost]
  public async Task<IActionResult> Create([FromBody] SaveTargetRequest request, CancellationToken ct) =>
    Ok(await _targets.CreateAsync(CurrentActor(), request, ct));

  [HttpPut("{id:guid}")]
  public async Task<IActionResult> Update(Guid id, [FromBody] SaveTargetRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _targets.UpdateAsync(CurrentActor(), id, request, ct), "No such target.");

  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
    await _targets.DeleteAsync(CurrentActor(), id, ct) ? NoContent() : NotFoundProblem("No such target.");
}
