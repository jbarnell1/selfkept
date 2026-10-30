using SelfKept.Core.DTOs;

namespace SelfKept.Core.Interfaces;

// docs/connect-design.md: the OAuth 2.1 authorization server for /mcp, personal access tokens and
// assistant memory.

public sealed record OAuthAuthorizeInput(
  string? ClientId,
  string? RedirectUri,
  string? ResponseType,
  string? State,
  string? CodeChallenge,
  string? CodeChallengeMethod,
  string? Scope,
  string? Resource);

/// <summary>
/// What /authorize does next. Error: show a problem page (never redirect to an unverified URI).
/// RedirectError: the redirect_uri is verified, so bounce the error to the client. Consent: send the
/// browser to the app's consent page.
/// </summary>
public sealed record OAuthAuthorizeResult(int Status, string? Title, string? Detail, string? RedirectUrl)
{
  public static OAuthAuthorizeResult Error(int status, string title, string detail) => new(status, title, detail, null);
  public static OAuthAuthorizeResult RedirectError(string url) => new(302, null, null, url);
  public static OAuthAuthorizeResult Consent(string url) => new(302, null, null, url);
}

public sealed record OAuthTokenInput(
  string? GrantType,
  string? Code,
  string? RedirectUri,
  string? ClientId,
  string? ClientSecret,
  string? CodeVerifier,
  string? RefreshToken,
  string? Resource);

public sealed record OAuthTokenResult(int Status, OAuthTokenResponse? Response, OAuthErrorResponse? Error)
{
  public static OAuthTokenResult Ok(OAuthTokenResponse response) => new(200, response, null);
  public static OAuthTokenResult Fail(int status, string error, string description) => new(status, null, new OAuthErrorResponse(error, description));
}

public sealed record OAuthRegistrationResult(int Status, OAuthRegistrationResponse? Response, OAuthErrorResponse? Error)
{
  public static OAuthRegistrationResult Ok(OAuthRegistrationResponse response) => new(201, response, null);
  public static OAuthRegistrationResult Fail(string error, string description) => new(400, null, new OAuthErrorResponse(error, description));
}

public interface IOAuthService
{
  /// <summary>The issuer and the resource URI this server signs for; null when MCP is not configured.</summary>
  bool IsConfigured { get; }

  Task<OAuthAuthorizeResult> AuthorizeAsync(OAuthAuthorizeInput input, CancellationToken ct = default);

  /// <summary>Null when the request is unknown, expired or already decided.</summary>
  Task<OAuthConsentDto?> GetConsentAsync(Guid requestId, CancellationToken ct = default);

  /// <summary>Binds the request to the signed-in user. Null when the request is unknown, expired or already decided.</summary>
  Task<OAuthDecisionDto?> DecideAsync(Guid requestId, Guid userId, bool approve, RequestInfo info, CancellationToken ct = default);

  Task<OAuthTokenResult> ExchangeTokenAsync(OAuthTokenInput input, CancellationToken ct = default);

  Task<OAuthRegistrationResult> RegisterAsync(OAuthRegistrationRequest request, CancellationToken ct = default);

  /// <summary>Writes Mcp:Clients into oauth_client at startup.</summary>
  Task UpsertConfiguredClientsAsync(CancellationToken ct = default);

  /// <summary>Clients the user has a live grant with (an unrevoked, unexpired refresh token).</summary>
  Task<IReadOnlyList<ConnectedAssistantDto>> ListGrantsAsync(Guid userId, CancellationToken ct = default);

  /// <summary>Revokes every refresh token of the user's grant to one client. False when there was none.</summary>
  Task<bool> RevokeGrantAsync(Guid userId, Guid? householdId, string clientId, RequestInfo info, CancellationToken ct = default);
}

/// <summary>Who a personal access token speaks for.</summary>
public sealed record PersonalTokenIdentity(Guid TokenId, Guid UserId, Guid PersonId, Guid? HouseholdId, string Scope, string Name);

public interface IPersonalTokenService
{
  Task<CreatedPersonalTokenDto> CreateAsync(Guid userId, Guid? householdId, CreatePersonalTokenRequest request, RequestInfo info, CancellationToken ct = default);

  Task<IReadOnlyList<PersonalTokenDto>> ListAsync(Guid userId, CancellationToken ct = default);

  /// <summary>False when the token is not the user's or already revoked.</summary>
  Task<bool> RevokeAsync(Guid userId, Guid? householdId, Guid tokenId, RequestInfo info, CancellationToken ct = default);

  /// <summary>Null for an unknown, revoked or malformed token, or one whose user no longer exists.</summary>
  Task<PersonalTokenIdentity?> ValidateAsync(string rawToken, CancellationToken ct = default);
}

public interface IAiMemoryService
{
  /// <summary>Newest first. With a topic, only items whose type, key, value or display text mention it.</summary>
  Task<IReadOnlyList<AiMemoryDto>> RecallAsync(Guid personId, string? topic, CancellationToken ct = default);

  /// <summary>Creates or replaces the item for (type, key). Throws DomainValidationException for a bad type or oversize value.</summary>
  Task<AiMemoryDto> RememberAsync(Guid personId, string type, string key, string value, string? displayText, string? source, CancellationToken ct = default);

  Task<bool> DeleteAsync(Guid personId, Guid id, CancellationToken ct = default);
}

public interface IOuraService
{
  /// <summary>True when an Oura application is set up (environment or app) and the server knows its own address.</summary>
  Task<bool> IsConfiguredAsync(CancellationToken ct = default);

  Task<OuraStatusDto> GetStatusAsync(Guid personId, CancellationToken ct = default);

  /// <summary>
  /// Starts the Oura authorize round trip for the signed-in person; returns the URL to open.
  /// <paramref name="returnPath"/> is where the browser comes back to ("/sources" or "/welcome/steps").
  /// </summary>
  Task<OuraConnectDto> StartConnectAsync(Guid userId, Guid personId, string? returnPath = null, CancellationToken ct = default);

  /// <summary>The browser lands here from Oura. Returns where to send it next (Sources, or the setup steps).</summary>
  Task<string> CompleteConnectAsync(string? code, string? state, string? error, RequestInfo info, CancellationToken ct = default);

  Task<bool> DisconnectAsync(Guid userId, Guid personId, Guid? householdId, RequestInfo info, CancellationToken ct = default);

  /// <summary>Asks the poller to pull this person on its next tick. False when not connected.</summary>
  Task<bool> RequestPullAsync(Guid personId, CancellationToken ct = default);

  /// <summary>Pulls every connection that is due. Returns how many were pulled.</summary>
  Task<int> PullDueAsync(CancellationToken ct = default);
}

public interface IExportService
{
  /// <summary>The caller's own data as a redacted Markdown bundle. Audited when request.Export is true.</summary>
  Task<PrivacyBundleDto> BuildBundleAsync(Guid userId, Guid personId, Guid? householdId, PrivacyBundleRequest request, RequestInfo info, CancellationToken ct = default);

  /// <summary>Everything the caller owns as one JSON document. Audited.</summary>
  Task<object> BuildFullExportAsync(Guid userId, Guid personId, Guid? householdId, RequestInfo info, CancellationToken ct = default);
}
