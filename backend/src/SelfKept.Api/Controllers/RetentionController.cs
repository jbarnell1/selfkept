using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// How long raw readings are kept before old heart rate is folded into minutes
/// (docs/retention-design.md). Household admin only; members get 403.
/// </summary>
[Authorize]
public sealed class RetentionController : BaseController
{
  private readonly IRetentionService _retention;

  public RetentionController(IRetentionService retention) => _retention = retention;

  [HttpGet]
  public async Task<IActionResult> Get(CancellationToken ct) =>
    Ok(await _retention.GetAsync(GetCurrentUserId(), ct));

  [HttpPut]
  public async Task<IActionResult> Save([FromBody] SaveRetentionRequest request, CancellationToken ct) =>
    Ok(await _retention.SaveAsync(GetCurrentUserId(), request, CurrentRequestInfo(), ct));

  /// <summary>Runs retention now instead of waiting for the night.</summary>
  [HttpPost("run")]
  public async Task<IActionResult> Run(CancellationToken ct) =>
    Ok(await _retention.RunAsync(GetCurrentUserId(), CurrentRequestInfo(), ct));
}
