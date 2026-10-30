using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services;

/// <summary>
/// Sign-up and sign-in. The first account on the server becomes the admin and creates the
/// household; after that registration is closed and a new account needs a valid invite code,
/// which places it in the inviting household as a member.
/// </summary>
public sealed class AuthService : IAuthService
{
  /// <summary>Advisory-lock key that serializes "is this the first account?" across concurrent sign-ups.</summary>
  public const long FirstUserLockKey = 0x5345_4C46_4B50_5431;

  private readonly IUnitOfWork _unitOfWork;
  private readonly IUserRepository _users;
  private readonly IPersonRepository _persons;
  private readonly IHouseholdRepository _households;
  private readonly IInviteService _invites;
  private readonly IPasswordHasher _passwordHasher;
  private readonly ITokenService _tokens;
  private readonly IRefreshTokenService _refreshTokens;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;
  private readonly ILogger<AuthService> _logger;

  public AuthService(
    IUnitOfWork unitOfWork,
    IUserRepository users,
    IPersonRepository persons,
    IHouseholdRepository households,
    IInviteService invites,
    IPasswordHasher passwordHasher,
    ITokenService tokens,
    IRefreshTokenService refreshTokens,
    IAuditService audit,
    TimeProvider clock,
    ILogger<AuthService> logger)
  {
    _unitOfWork = unitOfWork;
    _users = users;
    _persons = persons;
    _households = households;
    _invites = invites;
    _passwordHasher = passwordHasher;
    _tokens = tokens;
    _refreshTokens = refreshTokens;
    _audit = audit;
    _clock = clock;
    _logger = logger;
  }

  private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

  public async Task<SetupStatusDto> GetSetupStatusAsync(CancellationToken ct = default)
  {
    var needsFirstUser = !await _users.AnyAsync(ct);
    // Open registration exists only until the first account; everyone after that needs an invite.
    return new SetupStatusDto(needsFirstUser, needsFirstUser);
  }

  public async Task<AuthSession> RegisterAsync(RegisterRequest request, RequestInfo info, CancellationToken ct = default)
  {
    var email = NormalizeEmail(request.Email);
    var displayName = request.DisplayName.Trim();
    if (displayName.Length == 0) throw new DomainValidationException("Display name is required.");

    var (user, role, usedInvite) = await _unitOfWork.RunInTransactionAsync(async () =>
    {
      if (await _users.GetByEmailAsync(email, ct) != null)
      {
        throw new ConflictException("An account with this email already exists.");
      }

      var isFirst = !await _users.AnyAsync(ct);
      var now = UtcNow;
      var personId = Guid.CreateVersion7();
      var userId = Guid.CreateVersion7();

      HouseholdInvite? invite = null;
      Guid householdId;
      HouseholdRole newRole;

      if (isFirst)
      {
        newRole = HouseholdRole.Admin;
        householdId = Guid.CreateVersion7();
      }
      else
      {
        invite = await _invites.FindValidAsync(request.InviteCode ?? string.Empty, ct);
        if (invite == null)
        {
          throw new RegistrationClosedException(string.IsNullOrWhiteSpace(request.InviteCode)
            ? "This server is not accepting new accounts. Ask the person who runs it for an invite code."
            : InviteService.InviteGoneMessage);
        }
        newRole = HouseholdRole.Member;
        householdId = invite.HouseholdId;
      }

      await _persons.AddAsync(new Person
      {
        Id = personId,
        DisplayName = displayName,
        CreatedAt = now,
        UpdatedAt = now,
      }, ct);

      if (isFirst)
      {
        await _households.AddAsync(new Household
        {
          Id = householdId,
          Name = DefaultHouseholdName(displayName),
          CreatedByUserId = userId,
          CreatedAt = now,
          UpdatedAt = now,
        }, ct);
      }

      var created = await _users.AddAsync(new User
      {
        Id = userId,
        Email = email,
        PasswordHash = _passwordHasher.Hash(request.Password),
        DisplayName = displayName,
        PersonId = personId,
        HouseholdId = householdId,
        CreatedAt = now,
        UpdatedAt = now,
      }, ct);

      await _households.AddMemberAsync(new HouseholdMember
      {
        Id = Guid.CreateVersion7(),
        HouseholdId = householdId,
        PersonId = personId,
        UserId = userId,
        Role = newRole,
        JoinedAt = now,
      }, ct);

      if (invite != null)
      {
        await _invites.ConsumeAsync(invite, userId, ct);
      }

      return (created, newRole, invite);
    }, FirstUserLockKey, ct);

    _logger.LogInformation("Registered {Role} account {UserId}", role.ToString().ToLowerInvariant(), user.Id);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.Register, user.Id, user.HouseholdId, "user", user.Id.ToString(),
      new { role = role.ToString().ToLowerInvariant(), firstUser = usedInvite == null }, info.IpAddress), ct);
    if (usedInvite != null)
    {
      await _audit.RecordAsync(new AuditEvent(
        AuditActions.InviteUsed, user.Id, usedInvite.HouseholdId, "invite", usedInvite.Id.ToString(), null, info.IpAddress), ct);
    }

    // Reload so the person navigation is populated regardless of the repository implementation.
    var loaded = await _users.GetByIdAsync(user.Id, ct) ?? user;
    return await CreateSessionAsync(loaded, role, ct);
  }

  public async Task<AuthSession?> LoginAsync(LoginRequest request, RequestInfo info, CancellationToken ct = default)
  {
    var user = await _users.GetByEmailAsync(NormalizeEmail(request.Email), ct);
    if (user == null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
    {
      // Same answer for an unknown email and a wrong password.
      return null;
    }

    user.LastLoginAt = UtcNow;
    await _users.UpdateAsync(user, ct);

    var role = await ResolveRoleAsync(user.Id, ct);
    await _audit.RecordAsync(new AuditEvent(AuditActions.Login, user.Id, user.HouseholdId, "user", user.Id.ToString(), null, info.IpAddress), ct);
    return await CreateSessionAsync(user, role, ct);
  }

  public async Task<AuthSession?> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
  {
    var rotated = await _refreshTokens.RotateAsync(rawRefreshToken, ct);
    if (rotated == null) return null;

    var user = await _users.GetByIdAsync(rotated.UserId, ct);
    if (user == null) return null;

    var role = await ResolveRoleAsync(user.Id, ct);
    var access = _tokens.CreateAccessToken(user, role);
    return new AuthSession(user.ToDto(role), access, rotated.Token, rotated.ExpiresAt);
  }

  public Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default) =>
    string.IsNullOrWhiteSpace(rawRefreshToken) ? Task.CompletedTask : _refreshTokens.RevokeAsync(rawRefreshToken, ct);

  public async Task<UserDto?> GetMeAsync(Guid userId, CancellationToken ct = default)
  {
    var user = await _users.GetByIdAsync(userId, ct);
    if (user == null) return null;
    return user.ToDto(await ResolveRoleAsync(userId, ct));
  }

  private async Task<AuthSession> CreateSessionAsync(User user, HouseholdRole role, CancellationToken ct)
  {
    var access = _tokens.CreateAccessToken(user, role);
    var refresh = await _refreshTokens.IssueAsync(user.Id, ct);
    return new AuthSession(user.ToDto(role), access, refresh.Token, refresh.ExpiresAt);
  }

  private async Task<HouseholdRole> ResolveRoleAsync(Guid userId, CancellationToken ct)
  {
    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    return membership?.Role ?? HouseholdRole.Member;
  }

  private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

  private static string DefaultHouseholdName(string displayName) =>
    displayName.EndsWith('s') || displayName.EndsWith('S') ? $"{displayName}' household" : $"{displayName}'s household";
}
