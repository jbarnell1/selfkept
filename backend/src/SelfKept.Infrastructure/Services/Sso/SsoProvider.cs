using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SelfKept.Infrastructure.Services.Sso;

/// <summary>
/// Provider sign-in settings. Off until <c>Sso:Authority</c> (the provider's issuer URL),
/// <c>Sso:ClientId</c> and <c>Sso:ClientSecret</c> are all set. The owner registers this server
/// at the provider with the callback below. <c>Sso:Name</c> is what the sign-in button says.
/// </summary>
public sealed class SsoSettings
{
  public string? Authority { get; init; }
  public string? ClientId { get; init; }
  public string? ClientSecret { get; init; }
  public string Name { get; init; } = DefaultName;
  public string? RedirectUri { get; init; }
  public string? AppBaseUrl { get; init; }

  /// <summary>The scheme of the app link the phone app catches.</summary>
  public string DeepLinkScheme { get; init; } = "app";

  public string Scope { get; init; } = "openid email profile";

  public const string DefaultName = "single sign-on";

  public bool Enabled => Authority != null && ClientId != null && ClientSecret != null && RedirectUri != null && AppBaseUrl != null;

  public static SsoSettings From(IConfiguration configuration)
  {
    // Blank values in appsettings.json mean "not set", the same as a missing key.
    static string? Url(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
    static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    var origin = Url(configuration["App:BaseUrl"]) ?? Url(configuration["Mcp:Issuer"]);
    return new SsoSettings
    {
      Authority = Url(configuration["Sso:Authority"]),
      ClientId = Text(configuration["Sso:ClientId"]),
      ClientSecret = Text(configuration["Sso:ClientSecret"]),
      Name = Text(configuration["Sso:Name"]) ?? DefaultName,
      RedirectUri = origin == null ? null : $"{origin}/api/auth/sso/callback",
      AppBaseUrl = origin,
      DeepLinkScheme = Text(configuration["Brand:DeepLinkScheme"]) ?? "app",
    };
  }
}

/// <summary>Who the provider says signed in, after the ID token checked out.</summary>
public sealed record SsoIdentity(string Issuer, string Subject, string? Email, bool EmailVerified, string? Name);

/// <summary>The provider did not answer, or its answer did not check out.</summary>
public sealed class SsoProviderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The calls made to the provider. An interface so tests run without one.</summary>
public interface ISsoProvider
{
  Task<string> AuthorizeUrlAsync(string state, string nonce, string codeChallenge, CancellationToken ct);

  /// <summary>Trades the code for tokens and validates the ID token: signature, issuer, audience, lifetime, nonce.</summary>
  Task<SsoIdentity> RedeemAsync(string code, string codeVerifier, string nonce, CancellationToken ct);
}

/// <summary>
/// Authorization code flow with PKCE against any OpenID Connect provider, from its discovery
/// document. A singleton: the discovery document and signing keys are cached for an hour, and the
/// keys are fetched again once when a token names a key that is not in the cache (a rotation).
/// </summary>
public sealed class SsoProvider(IHttpClientFactory httpClients, SsoSettings settings, TimeProvider clock) : ISsoProvider
{
  public const string HttpClientName = "sso";

  private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);
  private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

  private sealed record Metadata(string Issuer, string AuthorizationEndpoint, string TokenEndpoint, JsonWebKeySet Keys, DateTimeOffset FetchedAt);

  private readonly SemaphoreSlim _gate = new(1, 1);
  private Metadata? _metadata;

  private HttpClient Client() => httpClients.CreateClient(HttpClientName);

  public async Task<string> AuthorizeUrlAsync(string state, string nonce, string codeChallenge, CancellationToken ct)
  {
    var meta = await MetadataAsync(refresh: false, ct);
    var query = new Dictionary<string, string>
    {
      ["response_type"] = "code",
      ["client_id"] = settings.ClientId!,
      ["redirect_uri"] = settings.RedirectUri!,
      ["scope"] = settings.Scope,
      ["state"] = state,
      ["nonce"] = nonce,
      ["code_challenge"] = codeChallenge,
      ["code_challenge_method"] = "S256",
    };
    var separator = meta.AuthorizationEndpoint.Contains('?') ? '&' : '?';
    return meta.AuthorizationEndpoint + separator +
           string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
  }

  public async Task<SsoIdentity> RedeemAsync(string code, string codeVerifier, string nonce, CancellationToken ct)
  {
    var meta = await MetadataAsync(refresh: false, ct);
    var idToken = await ExchangeAsync(meta, code, codeVerifier, ct);

    var result = await ValidateAsync(idToken, meta);
    if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
    {
      meta = await MetadataAsync(refresh: true, ct);
      result = await ValidateAsync(idToken, meta);
    }
    if (!result.IsValid)
      throw new SsoProviderException("The ID token did not validate.", result.Exception);

    var claims = result.ClaimsIdentity;
    if (claims.FindFirst("nonce")?.Value != nonce)
      throw new SsoProviderException("The ID token's nonce does not match the request.");
    var subject = claims.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(subject))
      throw new SsoProviderException("The ID token has no subject.");

    var verified = claims.FindFirst("email_verified")?.Value;
    return new SsoIdentity(
      meta.Issuer,
      subject,
      claims.FindFirst("email")?.Value,
      string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase),
      claims.FindFirst("name")?.Value ?? claims.FindFirst("preferred_username")?.Value);
  }

  private async Task<string> ExchangeAsync(Metadata meta, string code, string codeVerifier, CancellationToken ct)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, meta.TokenEndpoint)
    {
      Content = new FormUrlEncodedContent(new Dictionary<string, string>
      {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["redirect_uri"] = settings.RedirectUri!,
        ["code_verifier"] = codeVerifier,
      }),
    };
    // client_secret_basic, the default every provider supports (RFC 6749 section 2.3.1).
    var credentials = $"{Uri.EscapeDataString(settings.ClientId!)}:{Uri.EscapeDataString(settings.ClientSecret!)}";
    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));

    using var response = await Client().SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
      throw new SsoProviderException($"The provider refused the code ({(int)response.StatusCode}).");
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    if (!doc.RootElement.TryGetProperty("id_token", out var idToken) || idToken.GetString() is not { Length: > 0 } token)
      throw new SsoProviderException("The provider sent no ID token.");
    return token;
  }

  private Task<TokenValidationResult> ValidateAsync(string idToken, Metadata meta) =>
    new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
    {
      ValidIssuer = meta.Issuer,
      ValidAudience = settings.ClientId,
      IssuerSigningKeys = meta.Keys.GetSigningKeys(),
      RequireSignedTokens = true,
      RequireExpirationTime = true,
      ValidateLifetime = true,
      ClockSkew = ClockSkew,
      LifetimeValidator = (notBefore, expires, _, _) =>
      {
        var now = clock.GetUtcNow().UtcDateTime;
        return (notBefore == null || notBefore.Value - ClockSkew <= now) && expires != null && now <= expires.Value + ClockSkew;
      },
    });

  private async Task<Metadata> MetadataAsync(bool refresh, CancellationToken ct)
  {
    var cached = _metadata;
    if (!refresh && cached != null && clock.GetUtcNow() - cached.FetchedAt < CacheFor)
      return cached;

    await _gate.WaitAsync(ct);
    try
    {
      cached = _metadata;
      if (!refresh && cached != null && clock.GetUtcNow() - cached.FetchedAt < CacheFor)
        return cached;
      _metadata = await FetchMetadataAsync(ct);
      return _metadata;
    }
    finally
    {
      _gate.Release();
    }
  }

  private async Task<Metadata> FetchMetadataAsync(CancellationToken ct)
  {
    try
    {
      var client = Client();
      using var discovery = JsonDocument.Parse(await client.GetStringAsync($"{settings.Authority}/.well-known/openid-configuration", ct));
      var root = discovery.RootElement;
      string Required(string name) =>
        root.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text
          ? text
          : throw new SsoProviderException($"The provider's discovery document has no {name}.");

      var issuer = Required("issuer");
      // OpenID Connect Discovery 1.0 section 4.3: the issuer must be the URL the document came from.
      if (!string.Equals(issuer.TrimEnd('/'), settings.Authority, StringComparison.Ordinal))
        throw new SsoProviderException($"The provider calls itself {issuer}, not {settings.Authority}.");

      var keys = new JsonWebKeySet(await client.GetStringAsync(Required("jwks_uri"), ct));
      return new Metadata(issuer, Required("authorization_endpoint"), Required("token_endpoint"), keys, clock.GetUtcNow());
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or ArgumentException
                                  || (ex is TaskCanceledException && !ct.IsCancellationRequested))
    {
      throw new SsoProviderException("The provider's discovery document could not be read.", ex);
    }
  }
}
