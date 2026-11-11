using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Context: what is going on that explains a reading (docs/context-design.md). Read under the
/// insights share for someone else; writes are always the caller's own.
/// </summary>
[Authorize]
public sealed class ContextController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IContextService _context;

  public ContextController(IContextService context) => _context = context;

  /// <summary>Events overlapping from..to (default the last 30 days) and what is active today.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _context.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), from, to, ct), NotShared);

  [HttpPost]
  public async Task<IActionResult> Create([FromBody] SaveContextEventRequest request, CancellationToken ct) =>
    Ok(await _context.CreateAsync(CurrentActor(), request, ct));

  [HttpPatch("{id:guid}")]
  public async Task<IActionResult> Update(Guid id, [FromBody] UpdateContextEventRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _context.UpdateAsync(CurrentActor(), id, request, ct), "No such context event.");

  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
    await _context.DeleteAsync(CurrentActor(), id, ct) ? NoContent() : NotFoundProblem("No such context event.");
}
