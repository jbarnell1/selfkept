using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SelfKept.Api.Extensions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The opt-in cloud lookup (docs/lookup-design.md), under /api/assistant. Every route acts on the
/// caller's own data, has no personId, and answers 404 when the server switch is off. The send
/// answers as text/event-stream: sent, waiting every 10 seconds, then done or failed.
/// </summary>
[Authorize]
[Route("api/assistant")]
public sealed class LookupController : BaseController
{
  public const string NotAvailableDetail = "Cloud lookup is not available on this server.";

  private readonly ILookupService _lookups;
  private readonly JsonSerializerOptions _json;

  public LookupController(ILookupService lookups, IOptions<JsonOptions> json)
  {
    _lookups = lookups;
    _json = json.Value.JsonSerializerOptions;
  }

  [HttpGet("lookup")]
  public async Task<IActionResult> Status(CancellationToken ct) =>
    _lookups.Available ? Ok(await _lookups.GetAsync(GetCurrentPersonId(), ct)) : NotFoundProblem(NotAvailableDetail);

  [HttpPut("lookup")]
  public async Task<IActionResult> Save([FromBody] SaveLookupSettingRequest request, CancellationToken ct) =>
    _lookups.Available ? Ok(await _lookups.SaveAsync(CurrentActor(), request, ct)) : NotFoundProblem(NotAvailableDetail);

  /// <summary>Builds the text that would be sent. Nothing is sent and nothing is stored.</summary>
  [HttpPost("lookups/draft")]
  public async Task<IActionResult> Draft([FromBody] LookupDraftRequest request, CancellationToken ct)
  {
    if (!_lookups.Available) return NotFoundProblem(NotAvailableDetail);
    return HandleServiceResponse(await _lookups.DraftAsync(CurrentActor(), request, ct), "No such message.");
  }

  /// <summary>Sends the text the person saw, once. Errors before the stream starts are problem details.</summary>
  [HttpPost("lookups")]
  [EnableRateLimiting(RateLimitPolicies.Recompute)]
  public async Task<IActionResult> Send([FromBody] SendLookupRequest request, CancellationToken ct)
  {
    if (!_lookups.Available) return NotFoundProblem(NotAvailableDetail);
    var send = await _lookups.PrepareSendAsync(CurrentActor(), request, ct);
    if (send == null) return NotFoundProblem("No such message.");

    ServerSentEvents.Start(HttpContext);
    // Once sent, the lookup runs to the end even if the app goes away: every event is read, and
    // writing simply stops when the connection is gone. The answer is in the conversation later.
    var gone = false;
    await foreach (var e in _lookups.SendAsync(send, CancellationToken.None))
    {
      if (gone) continue;
      try
      {
        await ServerSentEvents.WriteAsync(Response, e.Event, e.Data, _json, ct);
      }
      catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
      {
        gone = true;
      }
    }
    return new EmptyResult();
  }

  [HttpGet("lookups")]
  public async Task<IActionResult> List(CancellationToken ct) =>
    _lookups.Available ? Ok(await _lookups.ListAsync(GetCurrentPersonId(), ct)) : NotFoundProblem(NotAvailableDetail);

  /// <summary>Deletes the lookup and its outgoing text. The answer stays in the conversation.</summary>
  [HttpDelete("lookups/{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
  {
    if (!_lookups.Available) return NotFoundProblem(NotAvailableDetail);
    return await _lookups.DeleteAsync(CurrentActor(), id, ct) ? NoContent() : NotFoundProblem("No such lookup.");
  }
}
