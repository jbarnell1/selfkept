using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The Trends screen: the metrics worth offering and one metric's daily points with its 28-day
/// band. Another person's trend needs a share for that metric's own category, otherwise 404.
/// </summary>
[Authorize]
public sealed class TrendsController : BaseController
{
  private const string NotShared = "No such metric or person, or they have not shared this with you.";

  private readonly ITrendService _trends;

  public TrendsController(ITrendService trends)
  {
    _trends = trends;
  }

  /// <summary>Metrics with at least 7 days in the latest 28-day window, with the latest value and its band position.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _trends.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);

  /// <summary>Daily points for the last 28 or 90 days plus the latest band (p10, p25, median, p75, p90).</summary>
  [HttpGet("{metric}")]
  public async Task<IActionResult> Series(string metric, [FromQuery] Guid? personId, [FromQuery] int? days, CancellationToken ct) =>
    HandleServiceResponse(await _trends.GetSeriesAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), metric, days, ct), NotShared);
}
