using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Security;

namespace SelfKept.Infrastructure.Services.Connect;

/// <summary>
/// Tokens a person creates in the app and pastes into an assistant that speaks MCP with a bearer
/// header. Valid on /mcp only. The secret is shown once and stored as a SHA-256 hash; the first
/// characters are kept so the app can tell tokens apart.
/// </summary>
public sealed class PersonalTokenService : IPersonalTokenService
{
  /// <summary>Every personal token starts with this, which is how /mcp tells it from a JWT.</summary>
  public const string TokenPrefix = "pat_";

  public const int MaxTokensPerUser = 20;

  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;

  public PersonalTokenService(IDbContextFactory<SelfKeptDbContext> contextFactory, IAuditService audit, TimeProvider clock)
  {
    _contextFactory = contextFactory;
    _audit = audit;
    _clock = clock;
  }

  private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

  public static bool LooksLikePersonalToken(string? token) =>
    token != null && token.StartsWith(TokenPrefix, StringComparison.Ordinal) && token.Length > TokenPrefix.Length + 20;

  public async Task<CreatedPersonalTokenDto> CreateAsync(Guid userId, Guid? householdId, CreatePersonalTokenRequest request, RequestInfo info, CancellationToken ct = default)
  {
    var name = request.Name.Trim();
    if (name.Length == 0)
      throw new DomainValidationException("Give the token a name, such as the assistant it is for.");

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var live = await context.PersonalAccessTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null, ct);
    if (live >= MaxTokensPerUser)
      throw new DomainValidationException($"You have {MaxTokensPerUser} tokens already. Revoke one you no longer use first.");

    var secret = TokenPrefix + TokenCrypto.NewToken();
    var scope = request.AllowMemoryWrite == false ? ConnectScopes.HealthRead : ConnectScopes.Default;
    var token = new PersonalAccessToken
    {
      Id = Guid.CreateVersion7(),
      UserId = userId,
      Name = name,
      Prefix = secret[..(TokenPrefix.Length + 6)],
      TokenHash = TokenCrypto.Sha256Hex(secret),
      Scope = scope,
      CreatedAt = UtcNow,
    };
    context.PersonalAccessTokens.Add(token);
    await context.SaveChangesAsync(ct);

    await _audit.RecordAsync(new AuditEvent(AuditActions.McpTokenCreated, userId, householdId, "personal_access_token", token.Id.ToString(),
      new { name, scope }, info.IpAddress), ct);
    return new CreatedPersonalTokenDto(ToDto(token), secret);
  }

  public async Task<IReadOnlyList<PersonalTokenDto>> ListAsync(Guid userId, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var tokens = await context.PersonalAccessTokens.AsNoTracking()
      .Where(t => t.UserId == userId && t.RevokedAt == null)
      .OrderByDescending(t => t.CreatedAt)
      .ToListAsync(ct);
    return tokens.Select(ToDto).ToList();
  }

  public async Task<bool> RevokeAsync(Guid userId, Guid? householdId, Guid tokenId, RequestInfo info, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var token = await context.PersonalAccessTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId && t.RevokedAt == null, ct);
    if (token == null)
      return false;
    token.RevokedAt = UtcNow;
    await context.SaveChangesAsync(ct);
    await _audit.RecordAsync(new AuditEvent(AuditActions.McpTokenRevoked, userId, householdId, "personal_access_token", token.Id.ToString(),
      new { token.Name }, info.IpAddress), ct);
    return true;
  }

  public async Task<PersonalTokenIdentity?> ValidateAsync(string rawToken, CancellationToken ct = default)
  {
    if (!LooksLikePersonalToken(rawToken))
      return null;
    var hash = TokenCrypto.Sha256Hex(rawToken);

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var token = await context.PersonalAccessTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
    if (token == null)
      return null;
    var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
    if (user == null)
      return null;

    // last_used_at is for the app's list, so a minute of precision is plenty: skip the write
    // when the token was used moments ago.
    if (token.LastUsedAt == null || token.LastUsedAt < UtcNow.AddMinutes(-1))
    {
      token.LastUsedAt = UtcNow;
      await context.SaveChangesAsync(ct);
    }
    return new PersonalTokenIdentity(token.Id, user.Id, user.PersonId, user.HouseholdId, token.Scope, token.Name);
  }

  private static PersonalTokenDto ToDto(PersonalAccessToken t) => new(t.Id, t.Name, t.Prefix, t.Scope, t.CreatedAt, t.LastUsedAt);
}
