using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>The weekly review (docs/context-design.md, "Weekly review"). Each section follows its own share.</summary>
[Authorize]
public sealed class ReviewController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IWeeklyReviewService _review;

  public ReviewController(IWeeklyReviewService review) => _review = review;

  /// <summary>The Monday to Sunday week holding weekOf; by default the week ending today on a Sunday, else the last complete week.</summary>
  [HttpGet("weekly")]
  public async Task<IActionResult> Weekly([FromQuery] DateOnly? weekOf, [FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _review.GetAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), weekOf, ct), NotShared);
}
