using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>Trials: a single change with a planned check (docs/labs-design.md, "Trials"). Read under the body share.</summary>
[Authorize]
public sealed class TrialsController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";
  private const string NoSuchTrial = "No such trial.";

  private readonly ITrialService _trials;

  public TrialsController(ITrialService trials) => _trials = trials;

  /// <summary>Active trials newest first, or every trial with <c>includeEnded=true</c>.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] Guid? personId, [FromQuery] bool includeEnded, CancellationToken ct) =>
    HandleServiceResponse(await _trials.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), includeEnded, ct), NotShared);

  [HttpPost]
  public async Task<IActionResult> Create([FromBody] CreateTrialRequest request, CancellationToken ct) =>
    Ok(await _trials.CreateAsync(CurrentActor(), request, ct));

  /// <summary>Only the fields sent change. status done or dropped ends it; active reopens it.</summary>
  [HttpPatch("{id:guid}")]
  public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTrialRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _trials.UpdateAsync(CurrentActor(), id, request, ct), NoSuchTrial);

  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
    await _trials.DeleteAsync(CurrentActor(), id, ct) ? NoContent() : NotFoundProblem(NoSuchTrial);
}
