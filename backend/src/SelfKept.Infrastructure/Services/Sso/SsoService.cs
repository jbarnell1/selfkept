using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Security;

namespace SelfKept.Infrastructure.Services.Sso;

/// <summary>
/// Signing in through the household's own OpenID Connect provider (docs/sso-design.md).
/// <list type="bullet">
///   <item>A provider login reaches only the account it is tied to. It is tied either by the person,
///   signed in, choosing Connect under You, or on first sign-in when the provider vouches for the
///   email (<c>email_verified</c>) and exactly that address has an account here. An unverified email
///   never finds an account: in many providers people can edit their own address.</item>
///   <item>No account is ever created here. Accounts still come from first-run setup or an invite.</item>
///   <item>A phone gets its session through a single-use handoff code, so no token travels in a URL.</item>
/// </list>
/// </summary>
public sealed class SsoService : ISsoService
{
  private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan HandoffTtl = TimeSpan.FromMinutes(2);

  private readonly IDbContextFactory<SelfKeptDbContext> _contexts;
  private readonly SsoSettings _settings;
  private readonly ISsoProvider _provider;
  private readonly IAuthService _auth;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;
  private readonly ILogger<SsoService> _logger;

  public SsoService(
    IDbContextFactory<SelfKeptDbContext> contexts,
    SsoSettings settings,
    ISsoProvider provider,
    IAuthService auth,
    IEncryptionService encryption,
    IAuditService audit,
    TimeProvider clock,
    ILogger<SsoService> logger)
  {
    _contexts = contexts;
    _settings = settings;
    _provider = provider;
    _auth = auth;
    _encryption = encryption;
    _audit = audit;
    _clock = clock;
    _logger = logger;
  }

  private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

  public SsoInfoDto GetInfo() => new(_settings.Enabled, _settings.Enabled ? _settings.Name : null);

  public async Task<string> StartSignInAsync(string client, CancellationToken ct = default)
  {
    client = SsoClients.IsKnown(client) ? client : SsoClients.Web;
    var failed = new SsoOutcome(SsoOutcomeKind.Failed, SsoIntents.SignIn, client);
    if (!_settings.Enabled)
      return ReturnUrl(failed with { Error = SsoErrors.Disabled });
    try
    {
      return await StartAsync(SsoIntents.SignIn, client, null, ct);
    }
    catch (SsoProviderException ex)
    {
      _logger.LogWarning(ex, "The sign-in provider did not answer a sign-in start");
      return ReturnUrl(failed with { Error = SsoErrors.Provider });
    }
  }

  public async Task<string?> StartLinkAsync(Guid userId, string client, CancellationToken ct = default)
  {
    if (!_settings.Enabled) return null;
    try
    {
      return await StartAsync(SsoIntents.Link, SsoClients.IsKnown(client) ? client : SsoClients.Web, userId, ct);
    }
    catch (SsoProviderException ex)
    {
      _logger.LogWarning(ex, "The sign-in provider did not answer a link start");
      throw new DomainValidationException($"{_settings.Name} did not answer. Check that it is running, then try again.");
    }
  }

  private async Task<string> StartAsync(string intent, string client, Guid? userId, CancellationToken ct)
  {
    var state = TokenCrypto.NewToken();
    var nonce = TokenCrypto.NewToken();
    var verifier = TokenCrypto.NewToken(48);
    // The provider is asked first, so an unreachable provider leaves no state row behind.
    var url = await _provider.AuthorizeUrlAsync(state, nonce, TokenCrypto.PkceChallenge(verifier), ct);

    await using var context = await _contexts.CreateDbContextAsync(ct);
    var now = UtcNow;
    context.SsoAuthStates.RemoveRange(await context.SsoAuthStates.Where(s => s.ExpiresAt < now).ToListAsync(ct));
    context.SsoAuthStates.Add(new SsoAuthState
    {
      StateHash = TokenCrypto.Sha256Hex(state),
      Nonce = nonce,
      VerifierCipher = _encryption.Encrypt(verifier),
      Intent = intent,
      Client = client,
      UserId = userId,
      CreatedAt = now,
      ExpiresAt = now.Add(StateTtl),
    });
    await context.SaveChangesAsync(ct);
    return url;
  }

  public async Task<SsoOutcome> CompleteAsync(string? code, string? state, string? error, RequestInfo info, CancellationToken ct = default)
  {
    var unknown = new SsoOutcome(SsoOutcomeKind.Failed, SsoIntents.SignIn, SsoClients.Web, Error: SsoErrors.Expired);
    if (!_settings.Enabled)
      return unknown with { Error = SsoErrors.Disabled };
    if (string.IsNullOrWhiteSpace(state))
      return unknown;

    await using var context = await _contexts.CreateDbContextAsync(ct);
    var stateHash = TokenCrypto.Sha256Hex(state);
    var pending = await context.SsoAuthStates.FirstOrDefaultAsync(s => s.StateHash == stateHash, ct);
    if (pending == null)
      return unknown;
    context.SsoAuthStates.Remove(pending); // single use, whatever happens next
    await context.SaveChangesAsync(ct);

    var failed = new SsoOutcome(SsoOutcomeKind.Failed, pending.Intent, pending.Client);
    if (pending.ExpiresAt < UtcNow)
      return failed with { Error = SsoErrors.Expired };
    if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code))
      return failed with { Error = SsoErrors.Cancelled };

    SsoIdentity identity;
    try
    {
      identity = await _provider.RedeemAsync(code, _encryption.Decrypt(pending.VerifierCipher), pending.Nonce, ct);
    }
    catch (Exception ex) when (ex is SsoProviderException or HttpRequestException
                                  || (ex is TaskCanceledException && !ct.IsCancellationRequested))
    {
      _logger.LogWarning(ex, "The sign-in provider's answer did not check out");
      return failed with { Error = SsoErrors.Provider };
    }

    var existing = await context.UserIdentities
      .FirstOrDefaultAsync(i => i.Issuer == identity.Issuer && i.Subject == identity.Subject, ct);

    if (pending.Intent == SsoIntents.Link)
      return await LinkAsync(context, pending, identity, existing, info, failed, ct);

    var userId = existing?.UserId;
    if (existing == null)
    {
      var claimed = await ClaimByVerifiedEmailAsync(context, identity, info, ct);
      if (claimed == null)
        return failed with { Error = SsoErrors.NotLinked };
      userId = claimed;
    }
    else
    {
      existing.LastUsedAt = UtcNow;
      existing.Email = identity.Email ?? existing.Email;
      await context.SaveChangesAsync(ct);
    }

    if (pending.Client == SsoClients.Mobile)
    {
      var handoff = TokenCrypto.NewToken();
      context.SsoHandoffs.Add(new SsoHandoff
      {
        CodeHash = TokenCrypto.Sha256Hex(handoff),
        UserId = userId!.Value,
        CreatedAt = UtcNow,
        ExpiresAt = UtcNow.Add(HandoffTtl),
      });
      await context.SaveChangesAsync(ct);
      return new SsoOutcome(SsoOutcomeKind.HandedOff, pending.Intent, pending.Client, HandoffCode: handoff);
    }

    var session = await _auth.SignInAsync(userId!.Value, AuthMethods.Sso, info, ct);
    return session == null
      ? failed with { Error = SsoErrors.NotLinked }
      : new SsoOutcome(SsoOutcomeKind.SignedIn, pending.Intent, pending.Client, Session: session);
  }

  private async Task<SsoOutcome> LinkAsync(
    SelfKeptDbContext context, SsoAuthState pending, SsoIdentity identity, UserIdentity? existing,
    RequestInfo info, SsoOutcome failed, CancellationToken ct)
  {
    var userId = pending.UserId!.Value;
    if (existing != null && existing.UserId != userId)
      return failed with { Error = SsoErrors.LinkedElsewhere };

    if (existing == null)
    {
      // One login per provider per account: connecting again replaces the old one.
      context.UserIdentities.RemoveRange(await context.UserIdentities
        .Where(i => i.UserId == userId && i.Issuer == identity.Issuer).ToListAsync(ct));
      context.UserIdentities.Add(NewIdentity(userId, identity));
    }
    else
    {
      existing.Email = identity.Email ?? existing.Email;
    }
    await context.SaveChangesAsync(ct);

    var householdId = await context.Users.Where(u => u.Id == userId).Select(u => u.HouseholdId).FirstOrDefaultAsync(ct);
    await _audit.RecordAsync(new AuditEvent(
      AuditActions.SsoLinked, userId, householdId, "user", userId.ToString(), new { automatic = false }, info.IpAddress), ct);
    return new SsoOutcome(SsoOutcomeKind.Linked, pending.Intent, pending.Client);
  }

  /// <summary>
  /// First sign-in with a provider login that is not tied to anything yet: when the provider
  /// vouches for the email and exactly that address has an account with no login from this
  /// provider, tie them. Null otherwise.
  /// </summary>
  private async Task<Guid?> ClaimByVerifiedEmailAsync(SelfKeptDbContext context, SsoIdentity identity, RequestInfo info, CancellationToken ct)
  {
    if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
      return null;
    var email = identity.Email.Trim().ToLowerInvariant();
    var user = await context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
    if (user == null)
      return null;
    if (await context.UserIdentities.AnyAsync(i => i.UserId == user.Id && i.Issuer == identity.Issuer, ct))
      return null; // already tied to a different login at this provider: never move it silently

    var link = NewIdentity(user.Id, identity);
    link.LastUsedAt = UtcNow;
    context.UserIdentities.Add(link);
    await context.SaveChangesAsync(ct);
    await _audit.RecordAsync(new AuditEvent(
      AuditActions.SsoLinked, user.Id, user.HouseholdId, "user", user.Id.ToString(), new { automatic = true }, info.IpAddress), ct);
    return user.Id;
  }

  private UserIdentity NewIdentity(Guid userId, SsoIdentity identity) => new()
  {
    Id = Guid.CreateVersion7(),
    UserId = userId,
    Issuer = identity.Issuer,
    Subject = identity.Subject,
    Email = identity.Email,
    CreatedAt = UtcNow,
  };

  public async Task<AuthSession?> ExchangeHandoffAsync(string code, RequestInfo info, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(code)) return null;
    await using var context = await _contexts.CreateDbContextAsync(ct);
    var hash = TokenCrypto.Sha256Hex(code);
    var handoff = await context.SsoHandoffs.FirstOrDefaultAsync(h => h.CodeHash == hash, ct);
    if (handoff == null) return null;
    context.SsoHandoffs.Remove(handoff); // single use
    await context.SaveChangesAsync(ct);
    if (handoff.ExpiresAt < UtcNow) return null;
    return await _auth.SignInAsync(handoff.UserId, AuthMethods.Sso, info, ct);
  }

  public async Task<SsoLinkStatusDto> GetLinkStatusAsync(Guid userId, CancellationToken ct = default)
  {
    await using var context = await _contexts.CreateDbContextAsync(ct);
    var link = await context.UserIdentities.AsNoTracking()
      .Where(i => i.UserId == userId).OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(ct);
    return new SsoLinkStatusDto(
      _settings.Enabled,
      _settings.Enabled ? _settings.Name : null,
      link != null,
      link?.Email,
      link?.CreatedAt);
  }

  public async Task<bool> UnlinkAsync(Guid userId, RequestInfo info, CancellationToken ct = default)
  {
    await using var context = await _contexts.CreateDbContextAsync(ct);
    var links = await context.UserIdentities.Where(i => i.UserId == userId).ToListAsync(ct);
    if (links.Count == 0) return false;
    context.UserIdentities.RemoveRange(links);
    await context.SaveChangesAsync(ct);
    var householdId = await context.Users.Where(u => u.Id == userId).Select(u => u.HouseholdId).FirstOrDefaultAsync(ct);
    await _audit.RecordAsync(new AuditEvent(AuditActions.SsoUnlinked, userId, householdId, "user", userId.ToString(), IpAddress: info.IpAddress), ct);
    return true;
  }

  private string ReturnUrl(SsoOutcome outcome) =>
    outcome.Client == SsoClients.Mobile ? MobileReturnUrl(outcome) : WebReturnUrl(outcome);

  public string WebReturnUrl(SsoOutcome outcome)
  {
    var origin = _settings.AppBaseUrl ?? string.Empty;
    return outcome.Kind switch
    {
      SsoOutcomeKind.SignedIn => $"{origin}/",
      SsoOutcomeKind.Linked => $"{origin}/you?sso=linked",
      _ when outcome.Intent == SsoIntents.Link => $"{origin}/you?sso={Uri.EscapeDataString(outcome.Error ?? SsoErrors.Provider)}",
      _ => $"{origin}/login?sso={Uri.EscapeDataString(outcome.Error ?? SsoErrors.Provider)}",
    };
  }

  public string MobileReturnUrl(SsoOutcome outcome)
  {
    var target = $"{_settings.DeepLinkScheme}://sso";
    return outcome.Kind switch
    {
      SsoOutcomeKind.HandedOff => $"{target}?code={Uri.EscapeDataString(outcome.HandoffCode!)}",
      SsoOutcomeKind.Linked => $"{target}?linked=1",
      _ => $"{target}?error={Uri.EscapeDataString(outcome.Error ?? SsoErrors.Provider)}",
    };
  }
}
