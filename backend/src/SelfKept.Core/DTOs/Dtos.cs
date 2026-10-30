using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SelfKept.Core.Enums;

namespace SelfKept.Core.DTOs;

// Wire shapes from docs/api-contract.md. Property names serialize camelCase; enums lowercase.

public sealed record PersonDto(
  Guid Id,
  string DisplayName,
  SexAtBirth SexAtBirth,
  DateOnly? BirthDate,
  UnitSystem Units,
  string TimeZone);

public sealed record UserDto(
  Guid Id,
  string Email,
  string DisplayName,
  HouseholdRole Role,
  Guid? HouseholdId,
  PersonDto Person,
  IReadOnlyList<SetupItem> SetupPlan,
  DateTime? SetupCompletedAt);

public sealed record AuthResponse(
  string AccessToken,
  DateTime AccessTokenExpiresAt,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RefreshToken,
  UserDto User);

public sealed record MemberDto(
  Guid PersonId,
  Guid? UserId,
  string DisplayName,
  HouseholdRole Role,
  DateTime JoinedAt);

public sealed record HouseholdDto(Guid Id, string Name, IReadOnlyList<MemberDto> Members);

/// <summary>Returned once, at creation. The code is not stored and cannot be shown again.</summary>
public sealed record InviteCreatedDto(Guid Id, string Code, DateTime ExpiresAt);

public sealed record InviteDto(Guid Id, DateTime CreatedAt, DateTime ExpiresAt);

/// <summary>
/// Sharing between two people, summarised per counterpart. For "shares" the person is the
/// member I share with; for "shared-with-me" it is the member sharing with me.
/// </summary>
public sealed record ShareGrantDto(Guid PersonId, string DisplayName, IReadOnlyList<ShareCategory> Categories);

public sealed record SetupStatusDto(bool NeedsFirstUser, bool RegistrationOpen);

// Requests

public sealed record RegisterRequest(
  [Required, EmailAddress, MaxLength(254)] string Email,
  [Required, MinLength(8), MaxLength(256)] string Password,
  [Required, MinLength(1), MaxLength(80)] string DisplayName,
  string? InviteCode);

public sealed record LoginRequest(
  [Required] string Email,
  [Required] string Password);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record UpdateProfileRequest(
  [Required, MinLength(1), MaxLength(80)] string DisplayName,
  SexAtBirth SexAtBirth,
  DateOnly? BirthDate,
  UnitSystem Units,
  [Required, MaxLength(64)] string TimeZone);

public sealed record ChangePasswordRequest(
  [Required] string CurrentPassword,
  [Required, MinLength(8), MaxLength(256)] string NewPassword);

public sealed record UpdateHouseholdRequest([Required, MinLength(1), MaxLength(80)] string Name);

public sealed record ReplaceSharesRequest([Required] IReadOnlyList<ShareCategory> Categories);

/// <summary>
/// The first-run answers. <see cref="Completed"/> true marks setup finished (or skipped); false
/// only saves the answers, and never reopens a finished setup.
/// </summary>
public sealed record SaveSetupRequest(
  [Required] IReadOnlyList<SetupItem> Plan,
  bool Completed);
