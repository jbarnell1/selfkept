using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Body measurements typed in by hand: weight, body fat, waist. Each becomes an ordinary sample,
/// so reading them back for someone else goes through the usual sample and summary endpoints.
/// </summary>
[Authorize]
public sealed class BodyController : BaseController
{
  private readonly IMeasurementService _measurements;

  public BodyController(IMeasurementService measurements) => _measurements = measurements;

  /// <summary>The typed-in readings of one metric that belong to the caller, newest first.</summary>
  [HttpGet("measurements")]
  public async Task<IActionResult> List([FromQuery] string metricKey, CancellationToken ct) =>
    Ok(await _measurements.ListOwnAsync(GetCurrentPersonId(), metricKey, ct));

  [HttpPost("measurements")]
  public async Task<IActionResult> Create([FromBody] CreateMeasurementRequest request, CancellationToken ct) =>
    Ok(await _measurements.CreateAsync(CurrentActor(), request, ct));

  [HttpDelete("measurements/{metricKey}/{id:guid}")]
  public async Task<IActionResult> Delete(string metricKey, Guid id, CancellationToken ct) =>
    await _measurements.DeleteAsync(CurrentActor(), metricKey, id, ct) ? NoContent() : NotFoundProblem("No such measurement.");
}
