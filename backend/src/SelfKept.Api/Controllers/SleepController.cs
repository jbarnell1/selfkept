using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>Sleep detail read back night by night: times, stages and minutes asleep. Needs the sleep share for someone else.</summary>
[Authorize]
public sealed class SleepController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IReadBackService _readBack;

  public SleepController(IReadBackService readBack) => _readBack = readBack;

  /// <summary>Nights from..to, default the last 14, at most 92.</summary>
  [HttpGet]
  public async Task<IActionResult> Nights([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _readBack.GetSleepAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), from, to, ct), NotShared);
}
