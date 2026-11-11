using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>Goals: one main and one secondary at a time (docs/body-design.md, "Goals").</summary>
[Authorize]
public sealed class GoalsController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IGoalService _goals;

  public GoalsController(IGoalService goals) => _goals = goals;

  /// <summary>Active goals, or every goal with <c>includeEnded=true</c>. Needs the body share for someone else.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] Guid? personId, [FromQuery] bool includeEnded, CancellationToken ct) =>
    HandleServiceResponse(await _goals.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), includeEnded, ct), NotShared);

  /// <summary>Sets a goal in a slot. A goal already active in that slot is ended as dropped.</summary>
  [HttpPost]
  public async Task<IActionResult> Create([FromBody] SaveGoalRequest request, CancellationToken ct) =>
    Ok(await _goals.CreateAsync(CurrentActor(), request, ct));

  [HttpPut("{id:guid}")]
  public async Task<IActionResult> Update(Guid id, [FromBody] SaveGoalRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _goals.UpdateAsync(CurrentActor(), id, request, ct), "No such goal.");

  /// <summary>Ends a goal as done or dropped. Its targets stay and can be kept, edited or removed.</summary>
  [HttpPost("{id:guid}/end")]
  public async Task<IActionResult> End(Guid id, [FromBody] EndGoalRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _goals.EndAsync(CurrentActor(), id, request, ct), "No such goal.");
}
