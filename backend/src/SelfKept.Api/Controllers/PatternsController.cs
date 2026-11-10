using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Personal patterns: nights after a day log tag against the rest (docs/context-design.md,
/// "Patterns"). Every pattern is a pattern in the person's own data, not proof. Read under the
/// insights share for someone else.
/// </summary>
[Authorize]
public sealed class PatternsController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IPatternService _patterns;

  public PatternsController(IPatternService patterns) => _patterns = patterns;

  [HttpGet]
  public async Task<IActionResult> Get([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _patterns.GetAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);
}
