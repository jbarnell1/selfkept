using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SelfKept.Core.Auth;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Security;

namespace SelfKept.Infrastructure.Services.Connect;

/// <summary>
/// The OAuth 2.1 authorization server for /mcp (docs/connect-design.md), ported from the sibling
/// product's Connect server and extended for a home server:
/// <list type="bullet">
///   <item>Clients arrive three ways: dynamic registration (RFC 7591, how Open WebUI connects), a
///   Client ID Metadata Document (the client_id is an https URL, how Claude and ChatGPT connect),
///   or Mcp:Clients in configuration.</item>
///   <item>PKCE S256 is required for every client; a confidential client (one that registered for
///   client_secret_post or client_secret_basic) must also present its secret.</item>
///   <item>Exact redirect_uri match; an unverified client or URI never redirects.</item>
///   <item>Authorization requests live 10 minutes, codes 60 seconds and are single use (stored
///   hashed); replaying a code revokes the grant.</item>
///   <item>Access tokens are HS256 JWTs with iss = Mcp:Issuer and aud = the resource, signed with
///   the MCP key from <see cref="McpSettings"/>, so they are never valid on /api.</item>
///   <item>Refresh tokens rotate; reuse of a rotated one revokes the family.</item>
/// </list>
/// Tokens, codes, secrets and PKCE verifiers are never logged.
/// </summary>
public sealed class OAuthService : IOAuthService
{
  public const string CimdHttpClientName = "oauth-cimd";
  public const int AccessTokenSeconds = 3600;

  private static readonly TimeSpan AuthorizationRequestTtl = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan AuthorizationCodeTtl = TimeSpan.FromSeconds(60);
  private static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(60);
  private static readonly TimeSpan MetadataCacheTtl = TimeSpan.FromHours(24);
  private const long MetadataMaxBytes = 64 * 1024;

  /// <summary>Dynamic registration is anonymous; cap the table so it cannot be filled.</summary>
  public const int MaxRegisteredClients = 100;

  private static readonly IReadOnlyList<OAuthScopeDto> ScopeDescriptions =
  [
    new(ConnectScopes.HealthRead, "Read your health data, insights and lab results, and anything a household member has shared with you"),
    new(ConnectScopes.MemoryWrite, "Remember notes you ask it to keep, such as a goal or a medication (never changes your health data)"),
  ];

  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly IAuditService _audit;
  private readonly McpSettings _settings;
  private readonly TimeProvider _clock;
  private readonly ILogger<OAuthService> _logger;
  private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveHost;

  public OAuthService(
    IDbContextFactory<SelfKeptDbContext> contextFactory,
    IHttpClientFactory httpClientFactory,
    IAuditService audit,
    McpSettings settings,
    TimeProvider clock,
    ILogger<OAuthService> logger,
    Func<string, CancellationToken, Task<IPAddress[]>>? resolveHost = null)
  {
    _contextFactory = contextFactory;
    _httpClientFactory = httpClientFactory;
    _audit = audit;
    _settings = settings;
    _clock = clock;
    _logger = logger;
    _resolveHost = resolveHost ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
  }

  public bool IsConfigured => _settings.IsConfigured;

  private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
  private string Issuer => _settings.Issuer ?? throw new InvalidOperationException("Mcp:Issuer (or App:BaseUrl) is not configured.");
  private string ResourceUri => _settings.ResourceUri ?? throw new InvalidOperationException("Mcp:ResourceUri is not configured.");

  // -------------------------------------------------------------------------------------------
  // Authorize
  // -------------------------------------------------------------------------------------------

  public async Task<OAuthAuthorizeResult> AuthorizeAsync(OAuthAuthorizeInput input, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(input.ClientId))
      return OAuthAuthorizeResult.Error(400, "Invalid request", "Missing client_id.");
    if (string.IsNullOrWhiteSpace(input.RedirectUri))
      return OAuthAuthorizeResult.Error(400, "Invalid request", "Missing redirect_uri.");

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var client = await ResolveClientAsync(context, input.ClientId, ct);
    if (client == null)
      return OAuthAuthorizeResult.Error(400, "Unknown client", "This application is not registered with this server.");

    // The anchor of the flow: an unregistered redirect_uri never receives a redirect.
    if (!ParseRedirectUris(client.RedirectUris).Contains(input.RedirectUri, StringComparer.Ordinal))
      return OAuthAuthorizeResult.Error(400, "Invalid redirect URI", "The redirect_uri does not match a registered value.");

    if (!string.Equals(input.ResponseType, "code", StringComparison.Ordinal))
      return OAuthAuthorizeResult.RedirectError(ErrorRedirect(input.RedirectUri, "unsupported_response_type", input.State));
    if (string.IsNullOrWhiteSpace(input.CodeChallenge) || input.CodeChallenge.Length is < 43 or > 128)
      return OAuthAuthorizeResult.RedirectError(ErrorRedirect(input.RedirectUri, "invalid_request", input.State));
    if (!string.Equals(input.CodeChallengeMethod, "S256", StringComparison.Ordinal))
      return OAuthAuthorizeResult.RedirectError(ErrorRedirect(input.RedirectUri, "invalid_request", input.State));
    if (!string.IsNullOrWhiteSpace(input.Resource) && !SameResource(input.Resource, ResourceUri))
      return OAuthAuthorizeResult.RedirectError(ErrorRedirect(input.RedirectUri, "invalid_target", input.State));

    var request = new OAuthAuthorizationRequest
    {
      Id = Guid.CreateVersion7(),
      ClientId = client.ClientId,
      RedirectUri = input.RedirectUri,
      Scope = GrantedScope(input.Scope),
      State = input.State,
      CodeChallenge = input.CodeChallenge,
      Resource = string.IsNullOrWhiteSpace(input.Resource) ? null : ResourceUri,
      CreatedAt = UtcNow,
      ExpiresAt = UtcNow.Add(AuthorizationRequestTtl),
    };
    context.OAuthAuthorizationRequests.Add(request);
    await context.SaveChangesAsync(ct);

    return OAuthAuthorizeResult.Consent(_settings.ConsentUrl(request.Id));
  }

  // -------------------------------------------------------------------------------------------
  // Consent
  // -------------------------------------------------------------------------------------------

  public async Task<OAuthConsentDto?> GetConsentAsync(Guid requestId, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var request = await context.OAuthAuthorizationRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
    if (request == null || !IsPending(request))
      return null;

    var client = await context.OAuthClients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == request.ClientId, ct);
    var scopes = ScopeDescriptions.Where(s => request.Scope.Split(' ').Contains(s.Name)).ToList();
    var redirectHost = Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var uri) ? uri.Authority : request.RedirectUri;
    return new OAuthConsentDto(
      request.Id,
      client?.ClientName ?? request.ClientId,
      client?.ClientUri,
      client?.LogoUri,
      redirectHost,
      scopes,
      request.ExpiresAt);
  }

  public async Task<OAuthDecisionDto?> DecideAsync(Guid requestId, Guid userId, bool approve, RequestInfo info, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var request = await context.OAuthAuthorizationRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
    if (request == null || !IsPending(request))
      return null;
    var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
    if (user == null)
      return null;

    request.UserId = userId;
    if (!approve)
    {
      request.Approved = false;
      await context.SaveChangesAsync(ct);
      await AuditAsync(AuditActions.McpGrantDenied, user, request.ClientId, info, ct);
      return new OAuthDecisionDto(ErrorRedirect(request.RedirectUri, "access_denied", request.State));
    }

    var code = TokenCrypto.NewToken();
    request.Approved = true;
    request.CodeHash = TokenCrypto.Sha256Hex(code);
    request.CodeExpiresAt = UtcNow.Add(AuthorizationCodeTtl);
    await context.SaveChangesAsync(ct);
    await AuditAsync(AuditActions.McpGrantApproved, user, request.ClientId, info, ct);

    return new OAuthDecisionDto(AppendQuery(request.RedirectUri, [new("code", code), new("state", request.State)]));
  }

  private bool IsPending(OAuthAuthorizationRequest request) =>
    !request.Consumed && request.Approved == null && request.ExpiresAt > UtcNow;

  // -------------------------------------------------------------------------------------------
  // Token endpoint
  // -------------------------------------------------------------------------------------------

  public async Task<OAuthTokenResult> ExchangeTokenAsync(OAuthTokenInput input, CancellationToken ct = default) =>
    input.GrantType switch
    {
      "authorization_code" => await ExchangeCodeAsync(input, ct),
      "refresh_token" => await ExchangeRefreshAsync(input, ct),
      null or "" => OAuthTokenResult.Fail(400, "invalid_request", "grant_type is required."),
      _ => OAuthTokenResult.Fail(400, "unsupported_grant_type", "Only authorization_code and refresh_token are supported."),
    };

  private async Task<OAuthTokenResult> ExchangeCodeAsync(OAuthTokenInput input, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.ClientId)
        || string.IsNullOrWhiteSpace(input.RedirectUri) || string.IsNullOrWhiteSpace(input.CodeVerifier))
      return OAuthTokenResult.Fail(400, "invalid_request", "code, client_id, redirect_uri and code_verifier are required.");

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var codeHash = TokenCrypto.Sha256Hex(input.Code);
    var request = await context.OAuthAuthorizationRequests.FirstOrDefaultAsync(r => r.CodeHash == codeHash, ct);
    if (request == null)
      return OAuthTokenResult.Fail(400, "invalid_grant", "Invalid or expired authorization code.");

    // A replayed code is a theft signal: revoke what this grant holds and refuse.
    if (request.Consumed)
    {
      if (request.UserId is { } replayUser)
        await RevokeRefreshTokensAsync(context, replayUser, request.ClientId, ct);
      _logger.LogWarning("Authorization code reuse for client {ClientId}; the grant's refresh tokens were revoked.", request.ClientId);
      return OAuthTokenResult.Fail(400, "invalid_grant", "Authorization code already used.");
    }

    if (request.Approved != true || request.UserId is not { } userId)
      return OAuthTokenResult.Fail(400, "invalid_grant", "Authorization code is not usable.");
    if (request.CodeExpiresAt == null || request.CodeExpiresAt <= UtcNow)
      return OAuthTokenResult.Fail(400, "invalid_grant", "Authorization code expired.");
    if (!string.Equals(request.ClientId, input.ClientId, StringComparison.Ordinal))
      return OAuthTokenResult.Fail(400, "invalid_grant", "client_id does not match the authorization request.");
    if (!string.Equals(request.RedirectUri, input.RedirectUri, StringComparison.Ordinal))
      return OAuthTokenResult.Fail(400, "invalid_grant", "redirect_uri does not match the authorization request.");
    if (!TokenCrypto.VerifyPkce(input.CodeVerifier, request.CodeChallenge))
      return OAuthTokenResult.Fail(400, "invalid_grant", "PKCE verification failed.");
    if (!string.IsNullOrWhiteSpace(input.Resource) && !SameResource(input.Resource, ResourceUri))
      return OAuthTokenResult.Fail(400, "invalid_target", "resource does not match this server.");

    var client = await context.OAuthClients.FirstOrDefaultAsync(c => c.ClientId == request.ClientId, ct);
    if (client == null)
      return OAuthTokenResult.Fail(400, "invalid_client", "Unknown client.");
    if (!ClientSecretMatches(client, input.ClientSecret))
      return OAuthTokenResult.Fail(401, "invalid_client", "Client authentication failed.");

    request.Consumed = true;
    client.LastUsedAt = UtcNow;
    await context.SaveChangesAsync(ct);

    var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
    if (user == null)
      return OAuthTokenResult.Fail(400, "invalid_grant", "The account no longer exists.");

    var refresh = await IssueRefreshTokenAsync(context, userId, client.ClientId, request.Scope, ct);
    var access = MintAccessToken(user, client.ClientId, client.ClientName, request.Scope);
    return OAuthTokenResult.Ok(new OAuthTokenResponse(access, "Bearer", AccessTokenSeconds, refresh, request.Scope));
  }

  private async Task<OAuthTokenResult> ExchangeRefreshAsync(OAuthTokenInput input, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(input.RefreshToken))
      return OAuthTokenResult.Fail(400, "invalid_request", "refresh_token is required.");

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var presentedHash = TokenCrypto.Sha256Hex(input.RefreshToken);
    var existing = await context.OAuthRefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == presentedHash, ct);
    if (existing == null)
      return OAuthTokenResult.Fail(400, "invalid_grant", "Invalid or expired refresh token.");

    // A client_id presented on refresh must be the token's own; a mismatch burns nothing.
    if (!string.IsNullOrWhiteSpace(input.ClientId) && !string.Equals(input.ClientId, existing.ClientId, StringComparison.Ordinal))
      return OAuthTokenResult.Fail(400, "invalid_grant", "Invalid or expired refresh token.");

    var client = await context.OAuthClients.FirstOrDefaultAsync(c => c.ClientId == existing.ClientId, ct);
    if (client == null)
      return OAuthTokenResult.Fail(400, "invalid_grant", "Invalid or expired refresh token.");
    if (!ClientSecretMatches(client, input.ClientSecret))
      return OAuthTokenResult.Fail(401, "invalid_client", "Client authentication failed.");

    if (existing.RevokedAt != null)
    {
      await RevokeChainAsync(context, existing, ct);
      _logger.LogWarning("Refresh token reuse for client {ClientId}; the token family was revoked.", existing.ClientId);
      return OAuthTokenResult.Fail(400, "invalid_grant", "Invalid or expired refresh token.");
    }
    if (existing.ExpiresAt <= UtcNow)
      return OAuthTokenResult.Fail(400, "invalid_grant", "Invalid or expired refresh token.");

    var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
    if (user == null)
      return OAuthTokenResult.Fail(400, "invalid_grant", "The account no longer exists.");

    var raw = TokenCrypto.NewToken();
    var replacement = new OAuthRefreshToken
    {
      Id = Guid.CreateVersion7(),
      UserId = existing.UserId,
      ClientId = existing.ClientId,
      TokenHash = TokenCrypto.Sha256Hex(raw),
      Scope = existing.Scope,
      Resource = existing.Resource,
      CreatedAt = UtcNow,
      ExpiresAt = UtcNow.Add(RefreshTokenTtl),
    };
    context.OAuthRefreshTokens.Add(replacement);
    existing.RevokedAt = UtcNow;
    existing.ReplacedBy = replacement.Id;
    client.LastUsedAt = UtcNow;
    await context.SaveChangesAsync(ct);

    var access = MintAccessToken(user, client.ClientId, client.ClientName, existing.Scope);
    return OAuthTokenResult.Ok(new OAuthTokenResponse(access, "Bearer", AccessTokenSeconds, raw, existing.Scope));
  }

  private static bool ClientSecretMatches(OAuthClient client, string? presented)
  {
    if (client.SecretHash == null)
      return true; // public client: PKCE carries the proof
    if (string.IsNullOrEmpty(presented))
      return false;
    var a = Encoding.ASCII.GetBytes(TokenCrypto.Sha256Hex(presented));
    var b = Encoding.ASCII.GetBytes(client.SecretHash);
    return CryptographicOperations.FixedTimeEquals(a, b);
  }

  // -------------------------------------------------------------------------------------------
  // Dynamic client registration (RFC 7591)
  // -------------------------------------------------------------------------------------------

  public async Task<OAuthRegistrationResult> RegisterAsync(OAuthRegistrationRequest request, CancellationToken ct = default)
  {
    var redirectUris = (request.RedirectUris ?? []).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct().ToList();
    if (redirectUris.Count == 0)
      return OAuthRegistrationResult.Fail("invalid_redirect_uri", "At least one redirect_uri is required.");
    if (redirectUris.Count > 10)
      return OAuthRegistrationResult.Fail("invalid_redirect_uri", "At most 10 redirect_uris.");
    foreach (var uri in redirectUris)
    {
      if (!IsAcceptableRedirect(uri))
        return OAuthRegistrationResult.Fail("invalid_redirect_uri", "Each redirect_uri must be an absolute http or https URL without a fragment.");
    }

    // RFC 7591 says an omitted method means client_secret_basic.
    var method = string.IsNullOrWhiteSpace(request.TokenEndpointAuthMethod) ? OAuthAuthMethods.ClientSecretBasic : request.TokenEndpointAuthMethod;
    if (!OAuthAuthMethods.All.Contains(method))
      return OAuthRegistrationResult.Fail("invalid_client_metadata", "token_endpoint_auth_method must be none, client_secret_post or client_secret_basic.");
    if (request.GrantTypes is { Count: > 0 } grants && (!grants.Contains("authorization_code") || grants.Any(g => g is not ("authorization_code" or "refresh_token"))))
      return OAuthRegistrationResult.Fail("invalid_client_metadata", "grant_types may only be authorization_code and refresh_token.");
    if (request.ResponseTypes is { Count: > 0 } responses && responses.Any(r => r != "code"))
      return OAuthRegistrationResult.Fail("invalid_client_metadata", "response_types may only be code.");

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    if (await context.OAuthClients.CountAsync(c => c.Kind == OAuthClientKinds.Dcr, ct) >= MaxRegisteredClients)
      return OAuthRegistrationResult.Fail("invalid_client_metadata", "This server has reached its limit of registered applications. Remove an old one in the app first.");

    var clientId = "dcr_" + TokenCrypto.NewToken()[..24];
    string? secret = method == OAuthAuthMethods.None ? null : TokenCrypto.NewToken();
    var name = Truncate(string.IsNullOrWhiteSpace(request.ClientName) ? "An assistant" : request.ClientName.Trim(), 200);
    var client = new OAuthClient
    {
      ClientId = clientId,
      Kind = OAuthClientKinds.Dcr,
      ClientName = name,
      ClientUri = SafeHttpUrl(request.ClientUri),
      LogoUri = SafeHttpUrl(request.LogoUri),
      RedirectUris = JsonSerializer.Serialize(redirectUris),
      TokenEndpointAuthMethod = method,
      SecretHash = secret == null ? null : TokenCrypto.Sha256Hex(secret),
      CreatedAt = UtcNow,
    };
    context.OAuthClients.Add(client);
    await context.SaveChangesAsync(ct);

    await _audit.RecordAsync(new AuditEvent(AuditActions.McpClientRegistered, TargetType: "oauth_client", TargetId: clientId,
      Details: new { name, method, redirectHosts = redirectUris.Select(u => new Uri(u).Authority).Distinct() }), ct);

    return OAuthRegistrationResult.Ok(new OAuthRegistrationResponse(
      clientId,
      secret,
      new DateTimeOffset(UtcNow).ToUnixTimeSeconds(),
      secret == null ? null : 0,
      name,
      redirectUris,
      method,
      ["authorization_code", "refresh_token"],
      ["code"],
      ConnectScopes.Default));
  }

  private static bool IsAcceptableRedirect(string uri) =>
    Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
    && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
    && string.IsNullOrEmpty(parsed.Fragment);

  private static string? SafeHttpUrl(string? url) =>
    url != null && Uri.TryCreate(url, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
      ? Truncate(url, 512)
      : null;

  // -------------------------------------------------------------------------------------------
  // Client resolution: registered, configured, or a metadata document URL
  // -------------------------------------------------------------------------------------------

  private async Task<OAuthClient?> ResolveClientAsync(SelfKeptDbContext context, string clientId, CancellationToken ct)
  {
    var existing = await context.OAuthClients.FirstOrDefaultAsync(c => c.ClientId == clientId, ct);
    if (existing != null && existing.Kind != OAuthClientKinds.Cimd)
      return existing;
    if (existing != null && existing.MetadataFetchedAt is { } fetched && fetched.Add(MetadataCacheTtl) > UtcNow)
      return existing;
    if (!IsMetadataDocumentUrl(clientId))
      return existing;

    var metadata = await FetchMetadataAsync(clientId, ct);
    if (metadata == null)
      return existing; // stale cache beats nothing; nothing beats a hostile document

    var redirectJson = JsonSerializer.Serialize(metadata.RedirectUris);
    if (existing == null)
    {
      existing = new OAuthClient { ClientId = clientId, Kind = OAuthClientKinds.Cimd, CreatedAt = UtcNow };
      context.OAuthClients.Add(existing);
    }
    existing.ClientName = Truncate(metadata.ClientName ?? new Uri(clientId).Host, 200);
    existing.ClientUri = SafeHttpUrl(metadata.ClientUri);
    existing.LogoUri = SafeHttpUrl(metadata.LogoUri);
    existing.RedirectUris = redirectJson;
    existing.TokenEndpointAuthMethod = OAuthAuthMethods.None;
    existing.MetadataFetchedAt = UtcNow;
    await context.SaveChangesAsync(ct);
    return existing;
  }

  private static bool IsMetadataDocumentUrl(string clientId) =>
    Uri.TryCreate(clientId, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.Fragment);

  private sealed record ClientMetadata(string? ClientName, string? ClientUri, string? LogoUri, List<string> RedirectUris);

  private async Task<ClientMetadata?> FetchMetadataAsync(string url, CancellationToken ct)
  {
    try
    {
      // SSRF guard: the client_id comes from an anonymous caller. Refuse unless every address the
      // host resolves to is public; the named HttpClient re-checks the address it actually dials.
      if (!Uri.TryCreate(url, UriKind.Absolute, out var target) || !await IsPublicHostAsync(target, ct))
      {
        _logger.LogWarning("Client metadata fetch refused: the host does not resolve to a public address.");
        return null;
      }

      var http = _httpClientFactory.CreateClient(CimdHttpClientName);
      http.Timeout = TimeSpan.FromSeconds(10);
      using var message = new HttpRequestMessage(HttpMethod.Get, url);
      message.Headers.Accept.ParseAdd("application/json");
      using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
      if (!response.IsSuccessStatusCode)
        return null;
      if (response.Content.Headers.ContentType?.MediaType is not { } media || !media.Contains("json", StringComparison.OrdinalIgnoreCase))
        return null;

      await using var stream = await response.Content.ReadAsStreamAsync(ct);
      using var buffer = new MemoryStream();
      var chunk = new byte[8192];
      int read;
      while ((read = await stream.ReadAsync(chunk, ct)) > 0)
      {
        if (buffer.Length + read > MetadataMaxBytes)
          return null;
        buffer.Write(chunk, 0, read);
      }
      buffer.Position = 0;
      using var doc = await JsonDocument.ParseAsync(buffer, cancellationToken: ct);
      var root = doc.RootElement;
      if (root.ValueKind != JsonValueKind.Object)
        return null;

      // The document must describe itself: its client_id must be the URL it was fetched from.
      if (root.TryGetProperty("client_id", out var declared) && declared.ValueKind == JsonValueKind.String
          && !string.Equals(declared.GetString(), url, StringComparison.Ordinal))
        return null;

      var redirects = new List<string>();
      if (root.TryGetProperty("redirect_uris", out var uris) && uris.ValueKind == JsonValueKind.Array)
      {
        foreach (var item in uris.EnumerateArray())
        {
          if (item.ValueKind == JsonValueKind.String && item.GetString() is { } value && IsAcceptableRedirect(value))
            redirects.Add(value);
        }
      }
      if (redirects.Count == 0)
        return null;

      return new ClientMetadata(StringOrNull(root, "client_name"), StringOrNull(root, "client_uri"), StringOrNull(root, "logo_uri"), redirects);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Client metadata fetch failed.");
      return null;
    }
  }

  private static string? StringOrNull(JsonElement obj, string name) =>
    obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

  private async Task<bool> IsPublicHostAsync(Uri uri, CancellationToken ct)
  {
    if (IPAddress.TryParse(uri.DnsSafeHost, out var literal))
      return PublicAddresses.IsPublic(literal);
    IPAddress[] addresses;
    try
    {
      addresses = await _resolveHost(uri.DnsSafeHost, ct);
    }
    catch (Exception)
    {
      return false;
    }
    return addresses.Length > 0 && addresses.All(PublicAddresses.IsPublic);
  }

  // -------------------------------------------------------------------------------------------
  // Configured clients
  // -------------------------------------------------------------------------------------------

  public async Task UpsertConfiguredClientsAsync(CancellationToken ct = default)
  {
    if (_settings.Clients.Count == 0)
      return;
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    foreach (var configured in _settings.Clients)
    {
      if (string.IsNullOrWhiteSpace(configured.ClientId) || configured.RedirectUris.Count == 0)
      {
        _logger.LogWarning("Skipping an Mcp:Clients entry without ClientId or RedirectUris.");
        continue;
      }
      var existing = await context.OAuthClients.FirstOrDefaultAsync(c => c.ClientId == configured.ClientId, ct);
      if (existing == null)
      {
        existing = new OAuthClient { ClientId = configured.ClientId, CreatedAt = UtcNow };
        context.OAuthClients.Add(existing);
      }
      existing.Kind = OAuthClientKinds.Configured;
      existing.ClientName = Truncate(string.IsNullOrWhiteSpace(configured.ClientName) ? configured.ClientId : configured.ClientName, 200);
      existing.ClientUri = SafeHttpUrl(configured.ClientUri);
      existing.RedirectUris = JsonSerializer.Serialize(configured.RedirectUris);
      existing.TokenEndpointAuthMethod = OAuthAuthMethods.None;
      existing.SecretHash = null;
    }
    await context.SaveChangesAsync(ct);
  }

  // -------------------------------------------------------------------------------------------
  // Grants as the app lists them
  // -------------------------------------------------------------------------------------------

  public async Task<IReadOnlyList<ConnectedAssistantDto>> ListGrantsAsync(Guid userId, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var now = UtcNow;
    var live = await context.OAuthRefreshTokens.AsNoTracking()
      .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
      .GroupBy(t => t.ClientId)
      .Select(g => new { ClientId = g.Key, First = g.Min(t => t.CreatedAt), Last = g.Max(t => t.CreatedAt) })
      .ToListAsync(ct);
    if (live.Count == 0)
      return [];

    var ids = live.Select(l => l.ClientId).ToList();
    // The first token of a grant was revoked when it rotated, so "connected at" is the oldest
    // token of the client for this user, live or not.
    var firsts = await context.OAuthRefreshTokens.AsNoTracking()
      .Where(t => t.UserId == userId && ids.Contains(t.ClientId))
      .GroupBy(t => t.ClientId)
      .Select(g => new { ClientId = g.Key, First = g.Min(t => t.CreatedAt) })
      .ToDictionaryAsync(x => x.ClientId, x => x.First, ct);
    var clients = await context.OAuthClients.AsNoTracking().Where(c => ids.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);

    return live
      .Select(l =>
      {
        clients.TryGetValue(l.ClientId, out var client);
        return new ConnectedAssistantDto(
          l.ClientId,
          client?.ClientName ?? l.ClientId,
          client?.ClientUri,
          client?.Kind ?? OAuthClientKinds.Dcr,
          firsts.TryGetValue(l.ClientId, out var first) ? first : l.First,
          l.Last);
      })
      .OrderByDescending(a => a.LastUsedAt)
      .ToList();
  }

  public async Task<bool> RevokeGrantAsync(Guid userId, Guid? householdId, string clientId, RequestInfo info, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var revoked = await RevokeRefreshTokensAsync(context, userId, clientId, ct);
    if (revoked == 0)
      return false;
    await _audit.RecordAsync(new AuditEvent(AuditActions.McpGrantRevoked, userId, householdId, "oauth_client", clientId, IpAddress: info.IpAddress), ct);
    return true;
  }

  // -------------------------------------------------------------------------------------------
  // Tokens
  // -------------------------------------------------------------------------------------------

  private async Task<string> IssueRefreshTokenAsync(SelfKeptDbContext context, Guid userId, string clientId, string scope, CancellationToken ct)
  {
    var raw = TokenCrypto.NewToken();
    context.OAuthRefreshTokens.Add(new OAuthRefreshToken
    {
      Id = Guid.CreateVersion7(),
      UserId = userId,
      ClientId = clientId,
      TokenHash = TokenCrypto.Sha256Hex(raw),
      Scope = scope,
      Resource = ResourceUri,
      CreatedAt = UtcNow,
      ExpiresAt = UtcNow.Add(RefreshTokenTtl),
    });
    await context.SaveChangesAsync(ct);
    return raw;
  }

  private static async Task<int> RevokeRefreshTokensAsync(SelfKeptDbContext context, Guid userId, string clientId, CancellationToken ct)
  {
    var active = await context.OAuthRefreshTokens.Where(t => t.UserId == userId && t.ClientId == clientId && t.RevokedAt == null).ToListAsync(ct);
    var now = DateTime.UtcNow;
    foreach (var token in active)
      token.RevokedAt = now;
    if (active.Count > 0)
      await context.SaveChangesAsync(ct);
    return active.Count;
  }

  private static async Task RevokeChainAsync(SelfKeptDbContext context, OAuthRefreshToken start, CancellationToken ct)
  {
    var now = DateTime.UtcNow;
    var cursor = start;
    for (var guard = 0; cursor != null && guard < 1000; guard++)
    {
      cursor.RevokedAt ??= now;
      if (cursor.ReplacedBy is not { } next)
        break;
      cursor = await context.OAuthRefreshTokens.FirstOrDefaultAsync(t => t.Id == next, ct);
    }
    await context.SaveChangesAsync(ct);
  }

  private string MintAccessToken(User user, string clientId, string clientName, string scope)
  {
    var claims = new List<Claim>
    {
      new(TokenClaims.Subject, user.Id.ToString()),
      new(TokenClaims.Person, user.PersonId.ToString()),
      new(TokenClaims.Scope, scope),
      new(TokenClaims.ClientId, clientId),
      new(TokenClaims.ClientName, clientName),
    };
    if (user.HouseholdId is { } household)
      claims.Add(new Claim(TokenClaims.Household, household.ToString()));

    var descriptor = new SecurityTokenDescriptor
    {
      Subject = new ClaimsIdentity(claims),
      IssuedAt = UtcNow,
      NotBefore = UtcNow,
      Expires = UtcNow.AddSeconds(AccessTokenSeconds),
      Issuer = Issuer,
      Audience = ResourceUri,
      SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_settings.SigningKey), SecurityAlgorithms.HmacSha256),
    };
    var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
    handler.OutboundClaimTypeMap.Clear();
    return handler.WriteToken(handler.CreateToken(descriptor));
  }

  // -------------------------------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------------------------------

  /// <summary>The scopes granted: what was asked for, narrowed to ours; everything when none were named.</summary>
  private static string GrantedScope(string? requested)
  {
    if (string.IsNullOrWhiteSpace(requested))
      return ConnectScopes.Default;
    var asked = requested.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var granted = ConnectScopes.All.Where(asked.Contains).ToList();
    // A client that names only scopes we do not know still gets read access, which the consent
    // page shows; asking for nothing we know is not a reason to fail the flow.
    return granted.Count == 0 ? ConnectScopes.HealthRead : string.Join(' ', granted);
  }

  private static bool SameResource(string presented, string resource) =>
    string.Equals(presented.TrimEnd('/'), resource.TrimEnd('/'), StringComparison.Ordinal);

  private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

  private static List<string> ParseRedirectUris(string json)
  {
    try
    {
      return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }
    catch (JsonException)
    {
      return [];
    }
  }

  private static string ErrorRedirect(string redirectUri, string error, string? state) =>
    AppendQuery(redirectUri, [new("error", error), new("state", state)]);

  private static string AppendQuery(string uri, IEnumerable<KeyValuePair<string, string?>> parameters)
  {
    var builder = new StringBuilder(uri);
    var separator = uri.Contains('?') ? '&' : '?';
    foreach (var (key, value) in parameters)
    {
      if (value == null) continue;
      builder.Append(separator).Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
      separator = '&';
    }
    return builder.ToString();
  }

  private Task AuditAsync(string action, User user, string clientId, RequestInfo info, CancellationToken ct) =>
    _audit.RecordAsync(new AuditEvent(action, user.Id, user.HouseholdId, "oauth_client", clientId, IpAddress: info.IpAddress), ct);
}
