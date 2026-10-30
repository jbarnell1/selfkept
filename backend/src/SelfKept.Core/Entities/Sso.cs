namespace SelfKept.Core.Entities;

// Tables from 0011_sso: signing in through the household's own OpenID Connect provider (Pocket ID,
// Authelia, Authentik and the like). docs/sso-design.md is the design.

/// <summary>A provider login tied to one account. A login reaches only the account it is tied to.</summary>
public class UserIdentity
{
  public Guid Id { get; set; }
  public Guid UserId { get; set; }

  /// <summary>The provider's issuer URL, exactly as the ID token states it.</summary>
  public string Issuer { get; set; } = string.Empty;

  /// <summary>The provider's stable id for the person (the ID token's <c>sub</c>).</summary>
  public string Subject { get; set; } = string.Empty;

  /// <summary>The email the provider reported when the link was made, for display only.</summary>
  public string? Email { get; set; }

  public DateTime CreatedAt { get; set; }
  public DateTime? LastUsedAt { get; set; }
}

/// <summary>One trip to the provider and back, in flight. Single use.</summary>
public class SsoAuthState
{
  public string StateHash { get; set; } = string.Empty;
  public string Nonce { get; set; } = string.Empty;
  public string VerifierCipher { get; set; } = string.Empty;

  /// <summary>signin or link. See <see cref="SsoIntents"/>.</summary>
  public string Intent { get; set; } = SsoIntents.SignIn;

  /// <summary>web or mobile: where the answer goes. See <see cref="SsoClients"/>.</summary>
  public string Client { get; set; } = SsoClients.Web;

  /// <summary>The signed-in account a link trip belongs to; null for a sign-in.</summary>
  public Guid? UserId { get; set; }

  public DateTime CreatedAt { get; set; }
  public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// A finished phone sign-in waiting for the app to collect it. The browser hands the app a short
/// code; the app trades it for tokens once, so no token ever travels in a URL.
/// </summary>
public class SsoHandoff
{
  public string CodeHash { get; set; } = string.Empty;
  public Guid UserId { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime ExpiresAt { get; set; }
}

public static class SsoIntents
{
  public const string SignIn = "signin";
  public const string Link = "link";
}

public static class SsoClients
{
  public const string Web = "web";
  public const string Mobile = "mobile";

  public static bool IsKnown(string? value) => value is Web or Mobile;
}
