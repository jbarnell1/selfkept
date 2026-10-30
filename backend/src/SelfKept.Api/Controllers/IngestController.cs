using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SelfKept.Api.Extensions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>Phones post here. Idempotent on (sourceId, sourceUid): resending a batch changes nothing.</summary>
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Ingest)]
public sealed class IngestController : BaseController
{
  private readonly IIngestService _ingest;

  public IngestController(IIngestService ingest)
  {
    _ingest = ingest;
  }

  [HttpPost("batch")]
  public async Task<IActionResult> Batch([FromBody] IngestBatchRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _ingest.IngestAsync(GetCurrentPersonId(), request, ct), "No such source.");
}
