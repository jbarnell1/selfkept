using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>Exports of the caller's own data: the redacted privacy bundle and the full JSON export.</summary>
[Route("api/export")]
[Authorize]
public sealed class ExportController : BaseController
{
  private readonly IExportService _export;
  private readonly TimeProvider _clock;

  public ExportController(IExportService export, TimeProvider clock)
  {
    _export = export;
    _clock = clock;
  }

  /// <summary>Preview with export false; the copy or save the person makes calls it with export true, which is audited.</summary>
  [HttpPost("bundle")]
  public async Task<IActionResult> Bundle([FromBody] PrivacyBundleRequest request, CancellationToken ct) =>
    Ok(await _export.BuildBundleAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), request, CurrentRequestInfo(), ct));

  [HttpGet("full")]
  public async Task<IActionResult> Full(CancellationToken ct)
  {
    var data = await _export.BuildFullExportAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), CurrentRequestInfo(), ct);
    Response.Headers.ContentDisposition = $"attachment; filename=\"health-export-{_clock.GetUtcNow():yyyy-MM-dd}.json\"";
    return Ok(data);
  }
}
