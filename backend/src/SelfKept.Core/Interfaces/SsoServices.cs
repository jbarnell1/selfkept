using SelfKept.Core.DTOs;

namespace SelfKept.Core.Interfaces;

// docs/sso-design.md: signing in through the household's own OpenID Connect provider.

/// <summary>How a trip to the provider ended, for the controller to turn into a redirect.</summary>
public enum SsoOutcomeKind
{
  /// <summary>A web sign-in: the controller sets the refresh cookie from <see cref="SsoOutcome.Session"/>.</summary>
  SignedIn,

  /// <summary>A phone sign-in: the app collects the session with <see cref="SsoOutcome.HandoffCode"/>.</summary>
  HandedOff,

  /// <summary>A signed-in person tied their provider login to their account.</summary>
  Linked,

  Failed,
}

/// <summary>Why a trip failed, as a short code the apps turn into a sentence.</summary>
public static class SsoErrors
{
  /// <summary>Provider sign-in is not set up on this server.</summary>
  public const string Disabled = "disabled";

  /// <summary>The trip was unknown, used already or too old.</summary>
  public const string Expired = "expired";

  /// <summary>The person cancelled at the provider, or the provider refused.</summary>
  public const string Cancelled = "cancelled";

  /// <summary>The provider's answer did not check out (token exchange or ID token).</summary>
  public const string Provider = "provider";

  /// <summary>No account is tied to this provider login and its email cannot be trusted to find one.</summary>
  public const string NotLinked = "not_linked";

  /// <summary>This provider login is already tied to another account.</summary>
  public const string LinkedElsewhere = "linked_elsewhere";
}

public sealed record SsoOutcome(
  SsoOutcomeKind Kind,
  string Intent,
  string Client,
  AuthSession? Session = null,
  string? HandoffCode = null,
  string? Error = null);

public interface ISsoService
{
  SsoInfoDto GetInfo();

  /// <summary>
  /// Where to send the browser for a sign-in: the provider's authorize URL, or the app's sign-in
  /// screen with the reason when provider sign-in is off or the provider does not answer.
  /// </summary>
  Task<string> StartSignInAsync(string client, CancellationToken ct = default);

  /// <summary>
  /// The provider's authorize URL for tying the signed-in account to a provider login. Null when
  /// provider sign-in is off; a <c>DomainValidationException</c> when the provider does not answer.
  /// </summary>
  Task<string?> StartLinkAsync(Guid userId, string client, CancellationToken ct = default);

  /// <summary>Finishes a trip. Never throws for a provider-side problem; it fails the outcome instead.</summary>
  Task<SsoOutcome> CompleteAsync(string? code, string? state, string? error, RequestInfo info, CancellationToken ct = default);

  /// <summary>Trades a phone handoff code for a session, once. Null when the code is unknown, used or old.</summary>
  Task<AuthSession?> ExchangeHandoffAsync(string code, RequestInfo info, CancellationToken ct = default);

  Task<SsoLinkStatusDto> GetLinkStatusAsync(Guid userId, CancellationToken ct = default);

  /// <summary>False when the account had no provider login tied to it.</summary>
  Task<bool> UnlinkAsync(Guid userId, RequestInfo info, CancellationToken ct = default);

  /// <summary>Where the browser lands in the web app after a trip.</summary>
  string WebReturnUrl(SsoOutcome outcome);

  /// <summary>The app link the phone app catches after a trip.</summary>
  string MobileReturnUrl(SsoOutcome outcome);
}
