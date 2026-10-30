using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SelfKept.Api.Extensions;
using SelfKept.Core.Auth;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Every controller inherits this. It reads the signed-in user, person and household from the
/// JWT, and maps a null service result to a 404 problem.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public abstract class BaseController : ControllerBase
{
  protected Guid GetCurrentUserId() => RequireGuidClaim(TokenClaims.Subject);

  protected Guid GetCurrentPersonId() => RequireGuidClaim(TokenClaims.Person);

  /// <summary>Null when the user is not in a household (not possible after registration, but the token says).</summary>
  protected Guid? GetCurrentHouseholdId() =>
    Guid.TryParse(User.FindFirst(TokenClaims.Household)?.Value, out var id) ? id : null;

  protected RequestInfo CurrentRequestInfo() => new(HttpContext.Connection.RemoteIpAddress?.ToString());

  /// <summary>200 with the data, or a 404 problem when the service returned null.</summary>
  protected IActionResult HandleServiceResponse<T>(T? response, string? notFoundDetail = null) =>
    response == null ? NotFoundProblem(notFoundDetail) : Ok(response);

  protected ObjectResult NotFoundProblem(string? detail = null) =>
    Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: detail ?? "The resource does not exist.");

  protected ObjectResult UnauthorizedProblem(string detail) =>
    Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: detail);

  private Guid RequireGuidClaim(string claimType)
  {
    var value = User.FindFirst(claimType)?.Value;
    if (!Guid.TryParse(value, out var id))
    {
      throw new UnauthorizedAccessException("Invalid user context");
    }
    return id;
  }
}
