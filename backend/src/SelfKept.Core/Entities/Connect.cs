namespace SelfKept.Core.Entities;

// Tables from 0008_connect: OAuth for MCP clients, personal access tokens, assistant memory.
// docs/connect-design.md is the design.

/// <summary>An OAuth client allowed to ask a person for access to /mcp.</summary>
public class OAuthClient
{
  public string ClientId { get; set; } = string.Empty;

  /// <summary>dcr, cimd or configured. See <see cref="OAuthClientKinds"/>.</summary>
  public string Kind { get; set; } = OAuthClientKinds.Dcr;

  public string ClientName { get; set; } = string.Empty;
  public string? ClientUri { get; set; }
  public string? LogoUri { get; set; }

  /// <summary>JSON array of exact redirect URIs.</summary>
  public string RedirectUris { get; set; } = "[]";

  /// <summary>none, client_secret_post or client_secret_basic.</summary>
  public string TokenEndpointAuthMethod { get; set; } = OAuthAuthMethods.None;

  /// <summary>SHA-256 hex of the client secret; null for a public client.</summary>
  public string? SecretHash { get; set; }

  public DateTime? MetadataFetchedAt { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime? LastUsedAt { get; set; }
}

public static class OAuthClientKinds
{
  public const string Dcr = "dcr";
  public const string Cimd = "cimd";
  public const string Configured = "configured";
}

public static class OAuthAuthMethods
{
  public const string None = "none";
  public const string ClientSecretPost = "client_secret_post";
  public const string ClientSecretBasic = "client_secret_basic";

  public static readonly IReadOnlyList<string> All = [None, ClientSecretPost, ClientSecretBasic];
}

/// <summary>One /authorize call: waits for consent, then holds a single-use code.</summary>
public class OAuthAuthorizationRequest
{
  public Guid Id { get; set; }
  public string ClientId { get; set; } = string.Empty;
  public string RedirectUri { get; set; } = string.Empty;
  public string Scope { get; set; } = string.Empty;
  public string? State { get; set; }
  public string CodeChallenge { get; set; } = string.Empty;
  public string? Resource { get; set; }

  /// <summary>Bound when the person decides; never taken from the client.</summary>
  public Guid? UserId { get; set; }

  public bool? Approved { get; set; }
  public string? CodeHash { get; set; }
  public DateTime? CodeExpiresAt { get; set; }
  public bool Consumed { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime ExpiresAt { get; set; }
}

/// <summary>A rotating refresh token for one person's grant to one client.</summary>
public class OAuthRefreshToken
{
  public Guid Id { get; set; }
  public Guid UserId { get; set; }
  public string ClientId { get; set; } = string.Empty;
  public string TokenHash { get; set; } = string.Empty;
  public string Scope { get; set; } = string.Empty;
  public string? Resource { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime ExpiresAt { get; set; }
  public DateTime? RevokedAt { get; set; }
  public Guid? ReplacedBy { get; set; }
}

/// <summary>A token the person created in the app for an assistant. Valid on /mcp only.</summary>
public class PersonalAccessToken
{
  public Guid Id { get; set; }
  public Guid UserId { get; set; }
  public string Name { get; set; } = string.Empty;
  public string Prefix { get; set; } = string.Empty;
  public string TokenHash { get; set; } = string.Empty;
  public string Scope { get; set; } = string.Empty;
  public DateTime CreatedAt { get; set; }
  public DateTime? LastUsedAt { get; set; }
  public DateTime? RevokedAt { get; set; }
}

/// <summary>A durable note an assistant keeps about a person (the remember tool).</summary>
public class AiMemory
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }

  /// <summary>See <see cref="AiMemoryTypes"/>.</summary>
  public string MemoryType { get; set; } = AiMemoryTypes.Preference;

  public string MemoryKey { get; set; } = string.Empty;

  /// <summary>The value, encrypted with IEncryptionService.</summary>
  public string ValueCipher { get; set; } = string.Empty;

  public string? DisplayText { get; set; }

  /// <summary>Which assistant wrote it (the OAuth client name or the token name).</summary>
  public string? Source { get; set; }

  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}

public static class AiMemoryTypes
{
  public const string Goal = "goal";
  public const string Condition = "condition";
  public const string Medication = "medication";
  public const string Preference = "preference";
  public const string LifeEvent = "life_event";
  public const string Household = "household";

  public static readonly IReadOnlyList<string> All = [Goal, Condition, Medication, Preference, LifeEvent, Household];
}

/// <summary>The two scopes an MCP grant can carry. Neutral names: no product name in a scope.</summary>
public static class ConnectScopes
{
  /// <summary>Read the person's health data and anything shared with them.</summary>
  public const string HealthRead = "health:read";

  /// <summary>Store notes through the remember tool. Never changes health data.</summary>
  public const string MemoryWrite = "memory:write";

  public static readonly IReadOnlyList<string> All = [HealthRead, MemoryWrite];

  public static string Default => string.Join(' ', All);
}

/// <summary>A person's link to Oura's cloud (0009_oura). Tokens are encrypted.</summary>
public class OuraConnection
{
  public Guid PersonId { get; set; }
  public Guid UserId { get; set; }
  public Guid SourceId { get; set; }
  public string AccessCipher { get; set; } = string.Empty;
  public string RefreshCipher { get; set; } = string.Empty;
  public DateTime AccessExpiresAt { get; set; }
  public string? Scope { get; set; }
  public DateTime ConnectedAt { get; set; }
  public DateTime? LastPullAt { get; set; }

  /// <summary>The last day a pull covered; the next pull starts two days before it (Oura revises recent nights).</summary>
  public DateOnly? LastPullThrough { get; set; }

  /// <summary>When the poller should pull next; null stops polling (a revoked grant).</summary>
  public DateTime? NextPullAt { get; set; }

  public string? LastError { get; set; }
}

/// <summary>One Oura authorize round trip in flight.</summary>
public class OuraAuthState
{
  public string StateHash { get; set; } = string.Empty;
  public Guid UserId { get; set; }
  public Guid PersonId { get; set; }
  public string VerifierCipher { get; set; } = string.Empty;

  /// <summary>The in-app path the browser returns to; null means the Sources screen.</summary>
  public string? ReturnPath { get; set; }

  public DateTime CreatedAt { get; set; }
  public DateTime ExpiresAt { get; set; }
}
