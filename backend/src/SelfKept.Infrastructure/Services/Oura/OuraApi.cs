using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SelfKept.Core.Formatting;

namespace SelfKept.Infrastructure.Services.Oura;

/// <summary>
/// Oura settings. The owner registers an API application at cloud.ouraring.com (Oura allows ten
/// users before review, plenty for a household) with the redirect URI below, then either enters
/// its client id and secret in the app (an admin, during setup or under Sources) or sets
/// <c>Oura:ClientId</c> and <c>Oura:ClientSecret</c> in the environment, which wins. See
/// <see cref="OuraAppCredentials"/>.
/// </summary>
public sealed class OuraSettings
{
  /// <summary>Where an owner creates the API application.</summary>
  public const string DeveloperUrl = "https://cloud.ouraring.com/oauth/applications";

  /// <summary>The application from the environment, when both halves are set there.</summary>
  public OuraApp? EnvironmentApp { get; init; }

  public string? RedirectUri { get; init; }
  public string? AppBaseUrl { get; init; }
  public string AuthorizeUrl { get; init; } = "https://cloud.ouraring.com/oauth/authorize";
  public string ApiBase { get; init; } = "https://api.ouraring.com";
  public string Scope { get; init; } = "daily heartrate spo2";
  public int BackfillDays { get; init; } = 90;
  public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(15);

  public static OuraSettings From(IConfiguration configuration)
  {
    // Blank values in appsettings.json mean "not set", the same as a missing key.
    static string? Url(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
    static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    var origin = Url(configuration["Mcp:Issuer"]) ?? Url(configuration["App:BaseUrl"]);
    var redirect = Text(configuration["Oura:RedirectUri"]);
    var clientId = Text(configuration["Oura:ClientId"]);
    var clientSecret = Text(configuration["Oura:ClientSecret"]);
    return new OuraSettings
    {
      EnvironmentApp = clientId != null && clientSecret != null ? new OuraApp(clientId, clientSecret) : null,
      RedirectUri = redirect ?? (origin == null ? null : $"{origin}/api/oura/callback"),
      AppBaseUrl = Url(configuration["App:BaseUrl"]) ?? origin,
      AuthorizeUrl = configuration["Oura:AuthorizeUrl"] ?? "https://cloud.ouraring.com/oauth/authorize",
      ApiBase = (configuration["Oura:ApiBase"] ?? "https://api.ouraring.com").TrimEnd('/'),
      BackfillDays = configuration.GetValue<int?>("Oura:BackfillDays") ?? 90,
      PollInterval = TimeSpan.FromMinutes(configuration.GetValue<int?>("Oura:PollMinutes") ?? 15),
    };
  }
}

public sealed record OuraTokens(string AccessToken, string RefreshToken, int ExpiresIn, string? Scope);

/// <summary>Oura answered 401: the grant was revoked or the token is dead after a refresh.</summary>
public sealed class OuraUnauthorizedException(string message) : Exception(message);

/// <summary>Oura answered 429; RetryAfter is how long to wait.</summary>
public sealed class OuraRateLimitedException(TimeSpan retryAfter) : Exception("Oura rate limit reached.")
{
  public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>The calls the service makes to Oura. An interface so tests run without the network.</summary>
public interface IOuraApi
{
  Task<OuraTokens> ExchangeCodeAsync(OuraApp app, string code, string codeVerifier, CancellationToken ct);
  Task<OuraTokens> RefreshAsync(OuraApp app, string refreshToken, CancellationToken ct);
  Task RevokeAsync(string accessToken, CancellationToken ct);

  /// <summary>Every document of a collection between two days, following next_token.</summary>
  Task<IReadOnlyList<JsonElement>> ListAsync(string accessToken, string collection, DateOnly start, DateOnly end, CancellationToken ct);
}

public sealed class OuraApi(IHttpClientFactory httpClients, OuraSettings settings) : IOuraApi
{
  public const string HttpClientName = "oura";

  /// <summary>A guard against a server that keeps handing back next_token.</summary>
  private const int MaxPages = 50;

  private HttpClient Client() => httpClients.CreateClient(HttpClientName);

  public Task<OuraTokens> ExchangeCodeAsync(OuraApp app, string code, string codeVerifier, CancellationToken ct) =>
    TokenAsync(new Dictionary<string, string>
    {
      ["grant_type"] = "authorization_code",
      ["code"] = code,
      ["redirect_uri"] = settings.RedirectUri!,
      ["client_id"] = app.ClientId,
      ["client_secret"] = app.ClientSecret,
      ["code_verifier"] = codeVerifier,
    }, ct);

  public Task<OuraTokens> RefreshAsync(OuraApp app, string refreshToken, CancellationToken ct) =>
    TokenAsync(new Dictionary<string, string>
    {
      ["grant_type"] = "refresh_token",
      ["refresh_token"] = refreshToken,
      ["client_id"] = app.ClientId,
      ["client_secret"] = app.ClientSecret,
    }, ct);

  private async Task<OuraTokens> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
  {
    using var response = await Client().PostAsync($"{settings.ApiBase}/oauth/token", new FormUrlEncodedContent(form), ct);
    if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
      throw new OuraUnauthorizedException("Oura did not accept the authorization.");
    ThrowIfRateLimited(response);
    response.EnsureSuccessStatusCode();
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    var root = doc.RootElement;
    return new OuraTokens(
      root.GetProperty("access_token").GetString()!,
      root.GetProperty("refresh_token").GetString()!,
      root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 86400,
      root.TryGetProperty("scope", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null);
  }

  public async Task RevokeAsync(string accessToken, CancellationToken ct)
  {
    using var response = await Client().PostAsync($"{settings.ApiBase}/oauth/revoke?access_token={Uri.EscapeDataString(accessToken)}", null, ct);
    // Best effort: a token that is already dead is the outcome we wanted.
  }

  public async Task<IReadOnlyList<JsonElement>> ListAsync(string accessToken, string collection, DateOnly start, DateOnly end, CancellationToken ct)
  {
    var documents = new List<JsonElement>();
    string? next = null;
    for (var page = 0; page < MaxPages; page++)
    {
      var url = $"{settings.ApiBase}/v2/usercollection/{collection}?start_date={start.ToIso()}&end_date={end.ToIso()}";
      if (next != null) url += $"&next_token={Uri.EscapeDataString(next)}";
      using var request = new HttpRequestMessage(HttpMethod.Get, url);
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
      using var response = await Client().SendAsync(request, ct);
      if (response.StatusCode == HttpStatusCode.Unauthorized)
        throw new OuraUnauthorizedException("Oura rejected the access token.");
      ThrowIfRateLimited(response);
      // 403 is an expired Oura membership or a scope the person did not grant: skip this collection.
      if (response.StatusCode == HttpStatusCode.Forbidden)
        return documents;
      response.EnsureSuccessStatusCode();

      using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
      if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        documents.AddRange(data.EnumerateArray().Select(d => d.Clone()));
      next = doc.RootElement.TryGetProperty("next_token", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() : null;
      if (string.IsNullOrEmpty(next))
        break;
    }
    return documents;
  }

  private static void ThrowIfRateLimited(HttpResponseMessage response)
  {
    if (response.StatusCode != HttpStatusCode.TooManyRequests) return;
    var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5);
    throw new OuraRateLimitedException(retry);
  }
}
