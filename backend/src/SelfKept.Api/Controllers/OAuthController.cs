using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SelfKept.Api.Extensions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The OAuth 2.1 endpoints for /mcp (docs/connect-design.md). authorize, token and register are
/// anonymous by protocol and answer in the RFC shapes; the consent read and decision belong to the
/// signed-in person in the app.
/// </summary>
[Route("api/oauth")]
public sealed class OAuthController : BaseController
{
  private readonly IOAuthService _oauth;

  public OAuthController(IOAuthService oauth) => _oauth = oauth;

  /// <summary>Starts a grant: validates the client and sends the browser to the app's consent page.</summary>
  [HttpGet("authorize")]
  [AllowAnonymous]
  public async Task<IActionResult> Authorize(
    [FromQuery(Name = "client_id")] string? clientId,
    [FromQuery(Name = "redirect_uri")] string? redirectUri,
    [FromQuery(Name = "response_type")] string? responseType,
    [FromQuery(Name = "state")] string? state,
    [FromQuery(Name = "code_challenge")] string? codeChallenge,
    [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
    [FromQuery(Name = "scope")] string? scope,
    [FromQuery(Name = "resource")] string? resource,
    CancellationToken ct)
  {
    if (!_oauth.IsConfigured) return NotConfigured();
    var result = await _oauth.AuthorizeAsync(
      new OAuthAuthorizeInput(clientId, redirectUri, responseType, state, codeChallenge, codeChallengeMethod, scope, resource), ct);
    return result.RedirectUrl != null
      ? Redirect(result.RedirectUrl)
      : Problem(statusCode: result.Status, title: result.Title, detail: result.Detail);
  }

  /// <summary>RFC 6749 token endpoint: authorization_code and refresh_token, form encoded.</summary>
  [HttpPost("token")]
  [AllowAnonymous]
  [Consumes("application/x-www-form-urlencoded")]
  [EnableRateLimiting(RateLimitPolicies.Auth)]
  public async Task<IActionResult> Token([FromForm] IFormCollection form, CancellationToken ct)
  {
    Response.Headers.CacheControl = "no-store";
    Response.Headers.Pragma = "no-cache";
    if (!_oauth.IsConfigured) return NotConfigured();

    var (basicId, basicSecret) = ReadBasicCredentials();
    var input = new OAuthTokenInput(
      form["grant_type"].ToString(),
      Value(form, "code"),
      Value(form, "redirect_uri"),
      Value(form, "client_id") ?? basicId,
      Value(form, "client_secret") ?? basicSecret,
      Value(form, "code_verifier"),
      Value(form, "refresh_token"),
      Value(form, "resource"));
    var result = await _oauth.ExchangeTokenAsync(input, ct);
    return result.Response != null ? Ok(result.Response) : StatusCode(result.Status, result.Error);
  }

  /// <summary>RFC 7591 dynamic client registration, how Open WebUI introduces itself.</summary>
  [HttpPost("register")]
  [AllowAnonymous]
  [EnableRateLimiting(RateLimitPolicies.OAuthRegister)]
  public async Task<IActionResult> Register([FromBody] OAuthRegistrationRequest request, CancellationToken ct)
  {
    Response.Headers.CacheControl = "no-store";
    if (!_oauth.IsConfigured) return NotConfigured();
    var result = await _oauth.RegisterAsync(request, ct);
    return result.Response != null ? StatusCode(201, result.Response) : BadRequest(result.Error);
  }

  /// <summary>What the consent page shows. 404 once the request is decided or expired.</summary>
  [HttpGet("request/{id:guid}")]
  [Authorize]
  public async Task<IActionResult> Consent(Guid id, CancellationToken ct) =>
    HandleServiceResponse(await _oauth.GetConsentAsync(id, ct), "This request has expired or was already answered. Start again from the assistant.");

  /// <summary>Approve or deny; the reply carries where to send the browser next.</summary>
  [HttpPost("request/{id:guid}/decision")]
  [Authorize]
  public async Task<IActionResult> Decide(Guid id, [FromBody] OAuthDecisionRequest request, CancellationToken ct) =>
    HandleServiceResponse(
      await _oauth.DecideAsync(id, GetCurrentUserId(), request.Approve, CurrentRequestInfo(), ct),
      "This request has expired or was already answered. Start again from the assistant.");

  private ObjectResult NotConfigured() =>
    Problem(statusCode: StatusCodes.Status404NotFound, title: "Not configured", detail: "Assistant connections are not set up on this server. Set App:BaseUrl.");

  private static string? Value(IFormCollection form, string key) =>
    form.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v.ToString()) ? v.ToString() : null;

  /// <summary>client_secret_basic: Authorization: Basic base64(urlencode(id):urlencode(secret)).</summary>
  private (string? Id, string? Secret) ReadBasicCredentials()
  {
    var header = Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
      return (null, null);
    try
    {
      var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
      var colon = decoded.IndexOf(':');
      if (colon < 0) return (null, null);
      return (Uri.UnescapeDataString(decoded[..colon]), Uri.UnescapeDataString(decoded[(colon + 1)..]));
    }
    catch (FormatException)
    {
      return (null, null);
    }
  }
}
