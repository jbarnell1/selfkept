using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Server-wide integrations the household admin sets up from the app (docs/setup-design.md).
/// Members get 403. Values set in the environment show here but cannot be changed here.
/// </summary>
[Authorize]
public sealed class IntegrationsController : BaseController
{
  private readonly IIntegrationService _integrations;

  public IntegrationsController(IIntegrationService integrations) => _integrations = integrations;

  [HttpGet("oura")]
  public async Task<IActionResult> GetOura(CancellationToken ct) =>
    Ok(await _integrations.GetOuraAppAsync(GetCurrentUserId(), ct));

  [HttpPut("oura")]
  public async Task<IActionResult> SaveOura([FromBody] SaveOuraAppRequest request, CancellationToken ct) =>
    Ok(await _integrations.SaveOuraAppAsync(GetCurrentUserId(), request, CurrentRequestInfo(), ct));

  [HttpDelete("oura")]
  public async Task<IActionResult> ClearOura(CancellationToken ct) =>
    Ok(await _integrations.ClearOuraAppAsync(GetCurrentUserId(), CurrentRequestInfo(), ct));
}
