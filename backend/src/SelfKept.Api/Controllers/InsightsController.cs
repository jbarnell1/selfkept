using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SelfKept.Api.Extensions;
using SelfKept.Core.Auth;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The insights archive, reactions, dismissals and the on-demand recompute. Reads follow the
/// insights share category; reactions and dismissals touch the caller's own cards only.
/// </summary>
[Authorize]
public sealed class InsightsController : BaseController
{
  private const string NotShared = "No such person, or they have not shared insights with you.";
  private const string NoSuchCard = "No such insight.";

  private readonly IInsightService _insights;

  public InsightsController(IInsightService insights)
  {
    _insights = insights;
  }

  /// <summary>Newest first. Defaults to the last 30 days; dismissed cards are hidden unless includeDismissed=true.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] Guid? personId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] bool includeDismissed, CancellationToken ct) =>
    HandleServiceResponse(await _insights.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), from, to, includeDismissed, ct), NotShared);

  /// <summary>helpful, knew or wrong. A kind marked wrong twice in 30 days leaves Today.</summary>
  [HttpPost("{id:guid}/reaction")]
  public async Task<IActionResult> React(Guid id, [FromBody] ReactionRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _insights.ReactAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), id, request.Reaction, CurrentRequestInfo(), ct), NoSuchCard);

  [HttpPost("{id:guid}/dismiss")]
  public async Task<IActionResult> Dismiss(Guid id, CancellationToken ct) =>
    HandleServiceResponse(await _insights.DismissAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), id, CurrentRequestInfo(), ct), NoSuchCard);

  /// <summary>
  /// Interprets one day now (after a backfill, say). Yourself by default; an admin may name a
  /// household member. Five calls a minute.
  /// </summary>
  [HttpPost("recompute")]
  [EnableRateLimiting(RateLimitPolicies.Recompute)]
  public async Task<IActionResult> Recompute([FromBody] RecomputeRequest? request, CancellationToken ct) =>
    HandleServiceResponse(
      await _insights.RecomputeAsync(GetCurrentUserId(), GetCurrentPersonId(), GetCurrentHouseholdId(), User.IsInRole(TokenRoles.Admin), request ?? new RecomputeRequest(null, null), CurrentRequestInfo(), ct),
      "No such person in your household.");
}
