using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Security;

namespace SelfKept.Infrastructure.Services;

/// <summary>
/// One-time invite codes. The plain code is returned once; only its SHA-256 hash is stored.
/// Codes expire after Household:InviteDays (default 7).
/// </summary>
public sealed class InviteService : IInviteService
{
  public const string InviteGoneMessage = "That invite code is not valid, has expired, or was already used.";
  private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I

  private readonly IInviteRepository _invites;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;
  private readonly int _validDays;

  public InviteService(IInviteRepository invites, IAuditService audit, TimeProvider clock, IConfiguration configuration)
  {
    _invites = invites;
    _audit = audit;
    _clock = clock;
    _validDays = Math.Clamp(configuration.GetValue<int?>("Household:InviteDays") ?? 7, 1, 90);
  }

  private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

  public async Task<InviteCreatedDto> CreateAsync(Guid householdId, Guid adminUserId, RequestInfo info, CancellationToken ct = default)
  {
    var code = NewCode();
    var now = UtcNow;
    var invite = await _invites.AddAsync(new HouseholdInvite
    {
      Id = Guid.CreateVersion7(),
      HouseholdId = householdId,
      CodeHash = HashCode(code),
      CreatedByUserId = adminUserId,
      CreatedAt = now,
      ExpiresAt = now.AddDays(_validDays),
    }, ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.InviteCreated, adminUserId, householdId, "invite", invite.Id.ToString(),
      new { expiresAt = invite.ExpiresAt }, info.IpAddress), ct);

    return new InviteCreatedDto(invite.Id, code, invite.ExpiresAt);
  }

  public async Task<IReadOnlyList<InviteDto>> ListPendingAsync(Guid householdId, CancellationToken ct = default)
  {
    var pending = await _invites.GetPendingAsync(householdId, UtcNow, ct);
    return pending.Select(i => new InviteDto(i.Id, i.CreatedAt, i.ExpiresAt)).ToList();
  }

  public async Task<bool> RevokeAsync(Guid householdId, Guid inviteId, CancellationToken ct = default)
  {
    var invite = await _invites.GetByIdAsync(inviteId, ct);
    if (invite == null || invite.HouseholdId != householdId || invite.UsedAt != null) return false;
    await _invites.RemoveAsync(invite, ct);
    return true;
  }

  public async Task<HouseholdInvite?> FindValidAsync(string code, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(code)) return null;
    var invite = await _invites.GetByCodeHashAsync(HashCode(code), ct);
    return invite != null && invite.IsPending(UtcNow) ? invite : null;
  }

  public async Task ConsumeAsync(HouseholdInvite invite, Guid usedByUserId, CancellationToken ct = default)
  {
    if (!invite.IsPending(UtcNow) || !await _invites.TryMarkUsedAsync(invite, usedByUserId, UtcNow, ct))
    {
      throw new RegistrationClosedException(InviteGoneMessage);
    }
  }

  /// <summary>Case, dashes and spaces do not matter when someone types a code.</summary>
  public static string HashCode(string code)
  {
    var normalized = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    return TokenCrypto.Sha256Hex(normalized);
  }

  /// <summary>20 characters (about 100 bits) in four groups: XXXXX-XXXXX-XXXXX-XXXXX.</summary>
  public static string NewCode()
  {
    var chars = new char[20];
    for (var i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
    var s = new string(chars);
    return $"{s[..5]}-{s[5..10]}-{s[10..15]}-{s[15..]}";
  }
}
