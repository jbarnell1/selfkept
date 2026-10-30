using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Reading samples and summaries. personId defaults to the caller; another person's data needs a
/// share grant for the metric's category, otherwise 404 (never 403, so nothing is leaked).
/// </summary>
[Authorize]
public sealed class DataController : BaseController
{
  private const string NotShared = "No such metric or person, or they have not shared this with you.";

  private readonly IDataService _data;

  public DataController(IDataService data)
  {
    _data = data;
  }

  /// <summary>One row per calendar day in the person's time zone. Defaults to the last 30 days.</summary>
  [HttpGet("daily")]
  public async Task<IActionResult> Daily([FromQuery] string metric, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? personId, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(metric)) return ValidationProblem("metric is required.");
    return HandleServiceResponse(await _data.GetDailyAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), metric, from, to, ct), NotShared);
  }

  /// <summary>Raw samples, newest first, keyset paged with nextCursor. limit is at most 2,000.</summary>
  [HttpGet("samples")]
  public async Task<IActionResult> Samples(
    [FromQuery] string metric, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? limit, [FromQuery] string? cursor, [FromQuery] Guid? personId, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(metric)) return ValidationProblem("metric is required.");
    return HandleServiceResponse(await _data.GetSamplesAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), metric, from, to, limit, cursor, ct), NotShared);
  }

  /// <summary>Which metrics have data, how much, and the latest value: the Sources and Trends screens.</summary>
  [HttpGet("overview")]
  public async Task<IActionResult> Overview([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _data.GetOverviewAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);
}
