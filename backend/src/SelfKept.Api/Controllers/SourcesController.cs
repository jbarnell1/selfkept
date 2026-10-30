using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>The caller's data sources: one row per device install. Sources are never shared.</summary>
[Authorize]
public sealed class SourcesController : BaseController
{
  private const string NotYours = "No such source.";

  private readonly ISourceService _sources;

  public SourcesController(ISourceService sources)
  {
    _sources = sources;
  }

  [HttpGet]
  public async Task<IActionResult> List(CancellationToken ct) =>
    Ok(await _sources.ListAsync(GetCurrentPersonId(), ct));

  /// <summary>Registers this device install, or returns the existing row for (kind, deviceName, platform).</summary>
  [HttpPost]
  public async Task<IActionResult> Upsert([FromBody] CreateSourceRequest request, CancellationToken ct) =>
    Ok(await _sources.UpsertAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), request, CurrentRequestInfo(), ct));

  [HttpPatch("{id:guid}")]
  public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSourceRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _sources.UpdateAsync(GetCurrentPersonId(), id, request, ct), NotYours);

  /// <summary>Soft delete: the source disappears from the list, its samples stay.</summary>
  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
    await _sources.DeleteAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), id, CurrentRequestInfo(), ct)
      ? NoContent()
      : NotFoundProblem(NotYours);
}
