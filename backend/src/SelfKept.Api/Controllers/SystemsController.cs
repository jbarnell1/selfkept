using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The Systems tab (docs/context-design.md, "Systems"): heart and fitness, sleep, metabolism,
/// hormones, body and cycle, each with its readings, lab results and cards. Each part is read under
/// its own share; a system with nothing shared is left out.
/// </summary>
[Authorize]
public sealed class SystemsController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly ISystemsService _systems;

  public SystemsController(ISystemsService systems) => _systems = systems;

  [HttpGet]
  public async Task<IActionResult> List([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _systems.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);

  /// <summary>One system with the last 28 days of each reading, lab history and the last 30 days of cards.</summary>
  [HttpGet("{key}")]
  public async Task<IActionResult> Get(string key, [FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _systems.GetAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), key, ct), "No such system or person, or they have not shared this with you.");
}
