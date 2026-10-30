using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The Today screen: one calm sentence, the readiness bands and up to two cards. personId
/// defaults to the caller; another person's Today needs their insights share, otherwise 404.
/// </summary>
[Authorize]
public sealed class TodayController : BaseController
{
  private const string NotShared = "No such person, or they have not shared insights with you.";

  private readonly ITodayService _today;

  public TodayController(ITodayService today)
  {
    _today = today;
  }

  [HttpGet]
  public async Task<IActionResult> Get([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _today.GetAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);
}
