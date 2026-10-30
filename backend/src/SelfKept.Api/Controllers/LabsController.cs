using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SelfKept.Api.Extensions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Lab panels (docs/labs-design.md): parse OCR text, confirm a report, read reports and markers,
/// export the clinician PDF. personId defaults to the caller; another person's labs need their
/// labs share, otherwise 404.
/// </summary>
[Authorize]
public sealed class LabsController : BaseController
{
  private const string NotShared = "No such person, or they have not shared labs with you.";
  private const string NoSuchReport = "No such report.";

  private readonly ILabService _labs;

  public LabsController(ILabService labs)
  {
    _labs = labs;
  }

  /// <summary>
  /// { text } of OCR lines from the phone, or pasted text. Server-side matching only; nothing is
  /// stored. A multipart image is accepted on the wire but answered 501: OCR runs on the device.
  /// </summary>
  [HttpPost("parse")]
  public async Task<IActionResult> Parse(CancellationToken ct)
  {
    if (Request.HasFormContentType)
    {
      return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Not implemented",
        detail: "server OCR not available in this version; run text recognition on the device and post { text }.");
    }
    ParseRequest? request;
    try
    {
      request = await Request.ReadFromJsonAsync<ParseRequest>(ct);
    }
    catch (System.Text.Json.JsonException)
    {
      return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad request", detail: "The body must be JSON: { \"text\": \"...\" }.");
    }
    var text = request?.Text ?? string.Empty;
    if (text.Length > 20000)
    {
      return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Validation failed", detail: "text is longer than 20000 characters.");
    }
    return Ok(_labs.Parse(text));
  }

  /// <summary>Stores a confirmed panel for the caller. Units are converted; the three ratios are derived when their components are present.</summary>
  [HttpPost("reports")]
  public async Task<IActionResult> CreateReport([FromBody] CreateLabReportRequest request, CancellationToken ct) =>
    Ok(await _labs.CreateReportAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), request, CurrentRequestInfo(), ct));

  /// <summary>Newest first.</summary>
  [HttpGet("reports")]
  public async Task<IActionResult> ListReports([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _labs.ListReportsAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);

  [HttpGet("reports/{id:guid}")]
  public async Task<IActionResult> GetReport(Guid id, CancellationToken ct) =>
    HandleServiceResponse(await _labs.GetReportAsync(GetCurrentPersonId(), id, ct), NoSuchReport);

  /// <summary>Own reports only. Removes the lab.* samples the report wrote.</summary>
  [HttpDelete("reports/{id:guid}")]
  public async Task<IActionResult> DeleteReport(Guid id, CancellationToken ct) =>
    await _labs.DeleteReportAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), id, CurrentRequestInfo(), ct) ? NoContent() : NotFoundProblem(NoSuchReport);

  /// <summary>One entry per marker in panel order: latest and previous value, population and optimal range for the person's sex, tier, last three values.</summary>
  [HttpGet("markers")]
  public async Task<IActionResult> ListMarkers([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _labs.GetMarkersAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);

  /// <summary>Full history for one marker plus the blurb and what moves it. The key may be given with or without the lab. prefix.</summary>
  [HttpGet("markers/{metricKey}")]
  public async Task<IActionResult> GetMarker(string metricKey, [FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _labs.GetMarkerAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), metricKey, ct), "No such marker, or labs are not shared with you.");

  /// <summary>The clinician PDF: a trends table over the range (default the last 90 days) and the latest two panels. Ten a minute.</summary>
  [HttpGet("summary.pdf")]
  [EnableRateLimiting(RateLimitPolicies.Pdf)]
  public async Task<IActionResult> SummaryPdf([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? personId, CancellationToken ct)
  {
    var pdf = await _labs.ExportSummaryAsync(GetCurrentUserId(), GetCurrentPersonId(), personId ?? GetCurrentPersonId(), GetCurrentHouseholdId(), from, to, CurrentRequestInfo(), ct);
    if (pdf == null) return NotFoundProblem(NotShared);
    return File(pdf.Content, "application/pdf", pdf.FileName);
  }
}
