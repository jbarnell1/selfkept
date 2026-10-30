using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SelfKept.Core.Enums;

namespace SelfKept.Core.DTOs;

// Wire shapes for docs/connect-design.md. OAuth endpoints use the snake_case names the RFCs fix
// (JsonPropertyName below); the app's own endpoints are camelCase like the rest of the API.

/// <summary>What the consent page shows: who is asking and for what.</summary>
public sealed record OAuthConsentDto(
  Guid RequestId,
  string ClientName,
  string? ClientUri,
  string? LogoUri,
  /// <summary>Where the browser goes after consent. Shown so a look-alike client name cannot hide a stranger's address.</summary>
  string RedirectHost,
  IReadOnlyList<OAuthScopeDto> Scopes,
  DateTime ExpiresAt);

public sealed record OAuthScopeDto(string Name, string Description);

public sealed record OAuthDecisionRequest(bool Approve);

public sealed record OAuthDecisionDto(string RedirectUrl);

/// <summary>RFC 6749 token response.</summary>
public sealed record OAuthTokenResponse(
  [property: JsonPropertyName("access_token")] string AccessToken,
  [property: JsonPropertyName("token_type")] string TokenType,
  [property: JsonPropertyName("expires_in")] int ExpiresIn,
  [property: JsonPropertyName("refresh_token")] string RefreshToken,
  [property: JsonPropertyName("scope")] string Scope);

/// <summary>RFC 6749 error response.</summary>
public sealed record OAuthErrorResponse(
  [property: JsonPropertyName("error")] string Error,
  [property: JsonPropertyName("error_description")] string? ErrorDescription);

/// <summary>RFC 7591 client registration request (the fields this server reads).</summary>
public sealed class OAuthRegistrationRequest
{
  [JsonPropertyName("redirect_uris")]
  public List<string>? RedirectUris { get; set; }

  [JsonPropertyName("client_name")]
  public string? ClientName { get; set; }

  [JsonPropertyName("client_uri")]
  public string? ClientUri { get; set; }

  [JsonPropertyName("logo_uri")]
  public string? LogoUri { get; set; }

  [JsonPropertyName("token_endpoint_auth_method")]
  public string? TokenEndpointAuthMethod { get; set; }

  [JsonPropertyName("grant_types")]
  public List<string>? GrantTypes { get; set; }

  [JsonPropertyName("response_types")]
  public List<string>? ResponseTypes { get; set; }

  [JsonPropertyName("scope")]
  public string? Scope { get; set; }
}

/// <summary>RFC 7591 client information response.</summary>
public sealed record OAuthRegistrationResponse(
  [property: JsonPropertyName("client_id")] string ClientId,
  [property: JsonPropertyName("client_secret"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ClientSecret,
  [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
  [property: JsonPropertyName("client_secret_expires_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ClientSecretExpiresAt,
  [property: JsonPropertyName("client_name")] string ClientName,
  [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string> RedirectUris,
  [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod,
  [property: JsonPropertyName("grant_types")] IReadOnlyList<string> GrantTypes,
  [property: JsonPropertyName("response_types")] IReadOnlyList<string> ResponseTypes,
  [property: JsonPropertyName("scope")] string Scope);

/// <summary>A connected assistant as the app lists it: an OAuth client with a live grant.</summary>
public sealed record ConnectedAssistantDto(
  string ClientId,
  string ClientName,
  string? ClientUri,
  string Kind,
  DateTime ConnectedAt,
  DateTime? LastUsedAt);

public sealed record PersonalTokenDto(
  Guid Id,
  string Name,
  string Prefix,
  string Scope,
  DateTime CreatedAt,
  DateTime? LastUsedAt);

/// <summary>Returned once, when the token is created. The token is never shown again.</summary>
public sealed record CreatedPersonalTokenDto(PersonalTokenDto Token, string Secret);

public sealed record CreatePersonalTokenRequest(
  [Required, MinLength(1), MaxLength(80)] string Name,
  bool? AllowMemoryWrite);

public sealed record ConnectionsDto(
  IReadOnlyList<ConnectedAssistantDto> Assistants,
  IReadOnlyList<PersonalTokenDto> Tokens,
  string McpUrl);

public sealed record AiMemoryDto(
  Guid Id,
  string Type,
  string Key,
  string Value,
  string? DisplayText,
  string? Source,
  DateTime CreatedAt,
  DateTime UpdatedAt);

/// <summary>The Oura card on the Sources screen. Configured is false when the server has no Oura app credentials.</summary>
public sealed record OuraStatusDto(
  bool Configured,
  bool Connected,
  DateTime? ConnectedAt,
  DateTime? LastPullAt,
  string? LastError,
  long SampleCount);

public sealed record OuraConnectDto(string AuthorizeUrl);

/// <summary>
/// What goes into a privacy bundle: the person's own data, for pasting into a model they choose.
/// Everything that could identify them is off unless they turn it on.
/// </summary>
public sealed record PrivacyBundleRequest(
  IReadOnlyList<ShareCategory>? Categories,
  [Range(7, 180)] int? Days,
  bool KeepDates,
  bool IncludeNotes,
  bool IncludeName,
  /// <summary>False for a preview; true when the person copies or saves it, which is audited.</summary>
  bool Export);

public sealed record PrivacyBundleDto(
  string Text,
  int Characters,
  int ApproximateTokens,
  IReadOnlyList<string> Included,
  DateOnly From,
  DateOnly To);

/// <summary>
/// The Oura application as an admin sees it during setup. The secret is never sent back.
/// <see cref="RedirectUri"/> is what the owner pastes into Oura's form; null when the server does
/// not know its own address (App:BaseUrl), which Oura needs.
/// </summary>
public sealed record OuraAppDto(
  bool Configured,
  CredentialSource Source,
  string? ClientId,
  string? RedirectUri,
  string DeveloperUrl);

public sealed record SaveOuraAppRequest(
  [Required, MinLength(1), MaxLength(200)] string ClientId,
  [Required, MinLength(1), MaxLength(400)] string ClientSecret);
