using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Core.Interfaces;

/// <summary>Request facts a service may want for the audit trail.</summary>
public sealed record RequestInfo(string? IpAddress)
{
  public static readonly RequestInfo None = new((string?)null);
}

public interface IPasswordHasher
{
  string Hash(string password);
  bool Verify(string password, string hash);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
  AccessToken CreateAccessToken(User user, HouseholdRole role);
}

public sealed record IssuedRefreshToken(string Token, DateTime ExpiresAt, Guid Family);

public sealed record RotatedRefreshToken(Guid UserId, string Token, DateTime ExpiresAt);

public interface IRefreshTokenService
{
  Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken ct = default);

  /// <summary>Rotates the token. Null when it is unknown, expired or revoked (reuse revokes its family).</summary>
  Task<RotatedRefreshToken?> RotateAsync(string rawToken, CancellationToken ct = default);

  Task RevokeAsync(string rawToken, CancellationToken ct = default);
  Task<int> RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>A signed-in session: the access token plus the refresh token the controller delivers by cookie or body.</summary>
public sealed record AuthSession(UserDto User, AccessToken Access, string RefreshToken, DateTime RefreshTokenExpiresAt);

public interface IAuthService
{
  Task<SetupStatusDto> GetSetupStatusAsync(CancellationToken ct = default);

  /// <summary>
  /// Creates the account. The first account becomes the admin and creates the household; later
  /// accounts need a valid invite code and join that invite's household as members.
  /// </summary>
  Task<AuthSession> RegisterAsync(RegisterRequest request, RequestInfo info, CancellationToken ct = default);

  /// <summary>Null when the email is unknown or the password is wrong (the caller answers 401).</summary>
  Task<AuthSession?> LoginAsync(LoginRequest request, RequestInfo info, CancellationToken ct = default);

  /// <summary>Null when the refresh token is not valid any more.</summary>
  Task<AuthSession?> RefreshAsync(string rawRefreshToken, CancellationToken ct = default);

  Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default);

  Task<UserDto?> GetMeAsync(Guid userId, CancellationToken ct = default);
}

public interface IInviteService
{
  Task<InviteCreatedDto> CreateAsync(Guid householdId, Guid adminUserId, RequestInfo info, CancellationToken ct = default);
  Task<IReadOnlyList<InviteDto>> ListPendingAsync(Guid householdId, CancellationToken ct = default);

  /// <summary>False when there is no such pending invite in this household.</summary>
  Task<bool> RevokeAsync(Guid householdId, Guid inviteId, CancellationToken ct = default);

  /// <summary>The invite for this code if it is unused and not expired; otherwise null.</summary>
  Task<HouseholdInvite?> FindValidAsync(string code, CancellationToken ct = default);

  /// <summary>
  /// Spends the invite for the new user. Throws RegistrationClosedException if it was spent
  /// meanwhile. The caller audits "invite.used" once its transaction has committed.
  /// </summary>
  Task ConsumeAsync(HouseholdInvite invite, Guid usedByUserId, CancellationToken ct = default);
}

public interface IHouseholdService
{
  Task<HouseholdDto?> GetForUserAsync(Guid userId, CancellationToken ct = default);

  /// <summary>Admin only. Null when the user has no household.</summary>
  Task<HouseholdDto?> RenameAsync(Guid userId, string name, CancellationToken ct = default);

  Task<IReadOnlyList<ShareGrantDto>> GetSharesGivenAsync(Guid userId, CancellationToken ct = default);

  /// <summary>Replaces the caller's grants to one member. Null when the member is not in the caller's household.</summary>
  Task<ShareGrantDto?> ReplaceSharesAsync(Guid userId, Guid memberPersonId, IReadOnlyCollection<ShareCategory> categories, RequestInfo info, CancellationToken ct = default);

  Task<IReadOnlyList<ShareGrantDto>> GetSharedWithMeAsync(Guid userId, CancellationToken ct = default);
}

public interface IProfileService
{
  Task<PersonDto?> GetAsync(Guid userId, CancellationToken ct = default);
  Task<PersonDto?> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);

  /// <summary>Null when the user does not exist; throws DomainValidationException on a wrong current password.</summary>
  Task<bool?> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default);
}

public sealed record AuditEvent(
  string Action,
  Guid? ActorUserId = null,
  Guid? HouseholdId = null,
  string? TargetType = null,
  string? TargetId = null,
  object? Details = null,
  string? IpAddress = null);

public interface IAuditService
{
  Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default);
}

public interface IEncryptionService
{
  string Encrypt(string plainText);
  string Decrypt(string cipherText);

  /// <summary>
  /// Decrypts for display, returning null instead of throwing when the value cannot be read (a
  /// wrong or rotated key, a damaged row). Use it where one unreadable note must not take down a
  /// whole screen; use <see cref="Decrypt"/> where failing loudly is right.
  /// </summary>
  string? TryDecrypt(string cipherText)
  {
    try
    {
      return Decrypt(cipherText);
    }
    catch (InvalidOperationException)
    {
      return null;
    }
    catch (FormatException)
    {
      return null;
    }
  }

  /// <summary>True when the stored value is already in the current ("v2:") format.</summary>
  bool IsCurrentFormat(string? cipherText);

  /// <summary>True when Encrypt writes the current format.</summary>
  bool WritesCurrentFormat { get; }
}

/// <summary>Each account's first-run answers (docs/setup-design.md).</summary>
public interface ISetupService
{
  /// <summary>Saves the answers and, when asked, marks setup finished. Null when the user is gone.</summary>
  Task<UserDto?> SaveAsync(Guid userId, SaveSetupRequest request, CancellationToken ct = default);
}

/// <summary>
/// Server-wide integrations an admin sets up from the app. Every method throws
/// ForbiddenException for anyone but the household admin.
/// </summary>
public interface IIntegrationService
{
  Task<OuraAppDto> GetOuraAppAsync(Guid userId, CancellationToken ct = default);

  /// <summary>Saves the Oura application. Conflict when the environment already sets one.</summary>
  Task<OuraAppDto> SaveOuraAppAsync(Guid userId, SaveOuraAppRequest request, RequestInfo info, CancellationToken ct = default);

  /// <summary>Removes the saved Oura application. Conflict when the environment sets it.</summary>
  Task<OuraAppDto> ClearOuraAppAsync(Guid userId, RequestInfo info, CancellationToken ct = default);
}
