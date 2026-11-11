using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>The Progress tab in one read: goals, the target tally, the body snapshot and its charts.</summary>
[Authorize]
public sealed class ProgressController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IProgressService _progress;

  public ProgressController(IProgressService progress) => _progress = progress;

  /// <summary>Needs the body share for someone else. The strength log appears only when activity is shared too.</summary>
  [HttpGet]
  public async Task<IActionResult> Get([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _progress.GetAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);
}
