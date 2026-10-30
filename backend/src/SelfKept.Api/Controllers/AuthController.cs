using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SelfKept.Api.Extensions;
using SelfKept.Core.Configuration;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Email and password sign-in, and sign-in through the household's own provider (sso/*,
/// docs/sso-design.md). The refresh token travels in an HttpOnly cookie for web clients and in the
/// response body when the request carries <c>X-Client: mobile</c>.
/// </summary>
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController : BaseController
{
  private const string MobileClientHeader = "X-Client";
  private const string MobileClientValue = "mobile";

  /// <summary>The cookie is only ever needed by the auth endpoints, so it is scoped to them.</summary>
  private const string CookiePath = "/api/auth";

  private readonly IAuthService _authService;
  private readonly ISsoService _sso;
  private readonly BrandOptions _brand;
  private readonly IConfiguration _configuration;

  public AuthController(IAuthService authService, ISsoService sso, BrandOptions brand, IConfiguration configuration)
  {
    _authService = authService;
    _sso = sso;
    _brand = brand;
    _configuration = configuration;
  }

  private bool IsMobileClient =>
    string.Equals(Request.Headers[MobileClientHeader].ToString(), MobileClientValue, StringComparison.OrdinalIgnoreCase);

  [HttpPost("register")]
  [AllowAnonymous]
  public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
  {
    var session = await _authService.RegisterAsync(request, CurrentRequestInfo(), ct);
    return Ok(Deliver(session));
  }

  [HttpPost("login")]
  [AllowAnonymous]
  public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
  {
    var session = await _authService.LoginAsync(request, CurrentRequestInfo(), ct);
    if (session == null)
    {
      return UnauthorizedProblem("The email or password is not correct.");
    }
    return Ok(Deliver(session));
  }

  [HttpPost("refresh")]
  [AllowAnonymous]
  public async Task<IActionResult> Refresh([FromBody] RefreshRequest? request, CancellationToken ct)
  {
    var raw = ReadRefreshToken(request?.RefreshToken);
    if (string.IsNullOrWhiteSpace(raw))
    {
      return UnauthorizedProblem("No refresh token was presented.");
    }

    var session = await _authService.RefreshAsync(raw, ct);
    if (session == null)
    {
      ClearRefreshCookie();
      return UnauthorizedProblem("The refresh token is not valid any more. Sign in again.");
    }
    return Ok(Deliver(session));
  }

  [HttpPost("logout")]
  [Authorize]
  public async Task<IActionResult> Logout([FromBody] RefreshRequest? request, CancellationToken ct)
  {
    await _authService.LogoutAsync(ReadRefreshToken(request?.RefreshToken), ct);
    ClearRefreshCookie();
    return NoContent();
  }

  [HttpGet("me")]
  [Authorize]
  public async Task<IActionResult> Me(CancellationToken ct) =>
    HandleServiceResponse(await _authService.GetMeAsync(GetCurrentUserId(), ct), "The account no longer exists.");

  /// <summary>Whether provider sign-in is on, and the name for the button.</summary>
  [HttpGet("sso")]
  [AllowAnonymous]
  public IActionResult SsoInfo() => Ok(_sso.GetInfo());

  /// <summary>The sign-in button navigates here; the browser goes on to the provider.</summary>
  [HttpGet("sso/start")]
  [AllowAnonymous]
  public async Task<IActionResult> SsoStart([FromQuery] string? client, CancellationToken ct) =>
    Redirect(await _sso.StartSignInAsync(client ?? string.Empty, ct));

  /// <summary>Where the provider sends the browser back. Anonymous by protocol; the single-use state identifies the trip.</summary>
  [HttpGet("sso/callback")]
  [AllowAnonymous]
  public async Task<IActionResult> SsoCallback(
    [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
  {
    var outcome = await _sso.CompleteAsync(code, state, error, CurrentRequestInfo(), ct);
    if (outcome is { Kind: SsoOutcomeKind.SignedIn, Session: { } session })
    {
      SetRefreshCookie(session.RefreshToken, session.RefreshTokenExpiresAt);
    }
    return Redirect(outcome.Client == SsoClients.Mobile ? _sso.MobileReturnUrl(outcome) : _sso.WebReturnUrl(outcome));
  }

  /// <summary>The phone trades the code from the app link for its session.</summary>
  [HttpPost("sso/exchange")]
  [AllowAnonymous]
  public async Task<IActionResult> SsoExchange([FromBody] SsoExchangeRequest request, CancellationToken ct)
  {
    var session = await _sso.ExchangeHandoffAsync(request.Code, CurrentRequestInfo(), ct);
    return session == null
      ? UnauthorizedProblem("That sign-in has expired. Start it again.")
      : Ok(Deliver(session));
  }

  [HttpGet("sso/link")]
  [Authorize]
  public async Task<IActionResult> SsoLinkStatus(CancellationToken ct) =>
    Ok(await _sso.GetLinkStatusAsync(GetCurrentUserId(), ct));

  /// <summary>Starts tying the signed-in account to a provider login; the app opens the URL.</summary>
  [HttpPost("sso/link")]
  [Authorize]
  public async Task<IActionResult> SsoLinkStart([FromQuery] string? client, CancellationToken ct)
  {
    var url = await _sso.StartLinkAsync(GetCurrentUserId(), client ?? string.Empty, ct);
    return url == null ? NotFoundProblem("Single sign-on is not set up on this server.") : Ok(new SsoStartDto(url));
  }

  [HttpDelete("sso/link")]
  [Authorize]
  public async Task<IActionResult> SsoUnlink(CancellationToken ct) =>
    await _sso.UnlinkAsync(GetCurrentUserId(), CurrentRequestInfo(), ct)
      ? NoContent()
      : NotFoundProblem("No sign-in provider is connected to this account.");

  /// <summary>Body for mobile (refresh token included), cookie for everyone else.</summary>
  private AuthResponse Deliver(AuthSession session)
  {
    if (IsMobileClient)
    {
      return new AuthResponse(session.Access.Token, session.Access.ExpiresAt, session.RefreshToken, session.User);
    }
    SetRefreshCookie(session.RefreshToken, session.RefreshTokenExpiresAt);
    return new AuthResponse(session.Access.Token, session.Access.ExpiresAt, null, session.User);
  }

  private string? ReadRefreshToken(string? bodyToken)
  {
    if (Request.Cookies.TryGetValue(_brand.RefreshCookieName, out var cookieToken) && !string.IsNullOrEmpty(cookieToken))
    {
      return cookieToken;
    }
    return bodyToken;
  }

  // Secure is always on unless the operator turned Auth:RequireSecureCookie off for a plain-HTTP
  // LAN install; over HTTPS it is on regardless.
  private bool SecureCookie => Request.IsHttps || (_configuration.GetValue<bool?>("Auth:RequireSecureCookie") ?? true);

  private void SetRefreshCookie(string token, DateTime expiresAt) =>
    Response.Cookies.Append(_brand.RefreshCookieName, token, new CookieOptions
    {
      HttpOnly = true,
      Secure = SecureCookie,
      SameSite = SameSiteMode.Strict,
      Expires = expiresAt,
      Path = CookiePath,
      IsEssential = true,
    });

  private void ClearRefreshCookie() =>
    Response.Cookies.Delete(_brand.RefreshCookieName, new CookieOptions
    {
      HttpOnly = true,
      Secure = SecureCookie,
      SameSite = SameSiteMode.Strict,
      Path = CookiePath,
    });
}
