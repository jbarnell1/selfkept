using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>Workouts read back with duration and heart rate, and weekly totals. Needs the activity share for someone else.</summary>
[Authorize]
public sealed class WorkoutsController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IReadBackService _readBack;

  public WorkoutsController(IReadBackService readBack) => _readBack = readBack;

  /// <summary>Workouts from..to, default the last 28 days, at most 92.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _readBack.GetWorkoutsAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), from, to, ct), NotShared);
}
