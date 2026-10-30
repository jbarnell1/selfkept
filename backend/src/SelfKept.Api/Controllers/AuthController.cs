using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SelfKept.Api.Extensions;
using SelfKept.Core.Configuration;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Email and password sign-in. The refresh token travels in an HttpOnly cookie for web clients and
/// in the response body when the request carries <c>X-Client: mobile</c>.
/// </summary>
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController : BaseController
{
  private const string MobileClientHeader = "X-Client";
  private const string MobileClientValue = "mobile";

  /// <summary>The cookie is only ever needed by the auth endpoints, so it is scoped to them.</summary>
  private const string CookiePath = "/api/auth";

  private readonly IAuthService _authService;
  private readonly BrandOptions _brand;
  private readonly IConfiguration _configuration;

  public AuthController(IAuthService authService, BrandOptions brand, IConfiguration configuration)
  {
    _authService = authService;
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
