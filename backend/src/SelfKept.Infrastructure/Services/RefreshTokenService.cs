using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Security;

namespace SelfKept.Infrastructure.Services;

/// <summary>
/// Refresh-token rotation with reuse detection. The raw token is a 256-bit random value; only its
/// SHA-256 hash is stored. Every use rotates the token (old revoked, replacement issued in the same
/// family); presenting an already-revoked token means a rotated or stolen token was replayed, so
/// the whole family is revoked. A short grace window after a rotation tolerates two browser tabs
/// refreshing at once, which would otherwise look like a replay.
///
/// Each operation runs on its own short-lived context from the <see cref="IDbContextFactory{T}"/>
/// so token writes never share (and race) the request-scoped context.
/// </summary>
public class RefreshTokenService : IRefreshTokenService
{
  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly ILogger<RefreshTokenService> _logger;
  private readonly TimeProvider _clock;
  private readonly int _lifetimeDays;
  private readonly TimeSpan _reuseGraceWindow;

  public RefreshTokenService(
    IDbContextFactory<SelfKeptDbContext> contextFactory,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<RefreshTokenService> logger)
  {
    _contextFactory = contextFactory;
    _logger = logger;
    _clock = clock;
    _lifetimeDays = configuration.GetValue<int?>("Jwt:RefreshTokenDays") ?? 30;
    _reuseGraceWindow = TimeSpan.FromSeconds(configuration.GetValue<int?>("Jwt:RefreshReuseGraceSeconds") ?? 60);
  }

  private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

  public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var (rawToken, expiresAt, family) = await AddTokenAsync(context, userId, null, ct);
    return new IssuedRefreshToken(rawToken, expiresAt, family);
  }

  public async Task<RotatedRefreshToken?> RotateAsync(string rawToken, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(rawToken))
      return null;

    await using var context = await _contextFactory.CreateDbContextAsync(ct);

    var hash = TokenCrypto.Sha256Hex(rawToken);
    var existing = await context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    if (existing == null)
      return null;

    var now = UtcNow;
    if (existing.RevokedAt != null)
    {
      // Revoked BY ROTATION (it has a successor) and re-presented within the grace window: a
      // multi-tab race, not a replay. Give that tab its own live leaf in the same family.
      var revokedByRotation = existing.ReplacedByTokenHash != null;
      var withinGrace = now - existing.RevokedAt.Value <= _reuseGraceWindow;
      if (revokedByRotation && withinGrace && now < existing.ExpiresAt)
      {
        var familyAlive = await context.RefreshTokens.AnyAsync(t =>
          t.Family == existing.Family && t.RevokedAt == null && t.ExpiresAt > now, ct);
        if (familyAlive)
        {
          var (graceRaw, graceExpiresAt, _) = await AddTokenAsync(context, existing.UserId, existing.Family, ct);
          _logger.LogInformation("Refresh-token grace rotation for user {UserId} (family {Family})", existing.UserId, existing.Family);
          return new RotatedRefreshToken(existing.UserId, graceRaw, graceExpiresAt);
        }
      }

      await RevokeFamilyAsync(context, existing.Family, ct);
      _logger.LogWarning("Refresh-token reuse detected for user {UserId}; revoked family {Family}", existing.UserId, existing.Family);
      return null;
    }

    if (now >= existing.ExpiresAt)
      return null;

    var (issuedRaw, issuedExpiresAt, _) = await AddTokenAsync(context, existing.UserId, existing.Family, ct);
    existing.RevokedAt = now;
    existing.ReplacedByTokenHash = TokenCrypto.Sha256Hex(issuedRaw);
    await context.SaveChangesAsync(ct);

    return new RotatedRefreshToken(existing.UserId, issuedRaw, issuedExpiresAt);
  }

  public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(rawToken))
      return;

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var hash = TokenCrypto.Sha256Hex(rawToken);
    var existing = await context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    if (existing is { RevokedAt: null })
    {
      existing.RevokedAt = UtcNow;
      await context.SaveChangesAsync(ct);
    }
  }

  public async Task<int> RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var now = UtcNow;
    var active = await context.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
    foreach (var token in active)
      token.RevokedAt = now;
    if (active.Count > 0)
      await context.SaveChangesAsync(ct);
    return active.Count;
  }

  private async Task<(string RawToken, DateTime ExpiresAt, Guid Family)> AddTokenAsync(
    SelfKeptDbContext context, Guid userId, Guid? family, CancellationToken ct)
  {
    var rawToken = TokenCrypto.NewToken();
    var now = UtcNow;
    var expiresAt = now.AddDays(_lifetimeDays);
    var resolvedFamily = family ?? Guid.NewGuid();

    context.RefreshTokens.Add(new RefreshToken
    {
      Id = Guid.CreateVersion7(),
      UserId = userId,
      TokenHash = TokenCrypto.Sha256Hex(rawToken),
      Family = resolvedFamily,
      ExpiresAt = expiresAt,
      CreatedAt = now,
    });
    await context.SaveChangesAsync(ct);

    return (rawToken, expiresAt, resolvedFamily);
  }

  private async Task RevokeFamilyAsync(SelfKeptDbContext context, Guid family, CancellationToken ct)
  {
    var now = UtcNow;
    var active = await context.RefreshTokens.Where(t => t.Family == family && t.RevokedAt == null).ToListAsync(ct);
    foreach (var token in active)
      token.RevokedAt = now;
    if (active.Count > 0)
      await context.SaveChangesAsync(ct);
  }

}
