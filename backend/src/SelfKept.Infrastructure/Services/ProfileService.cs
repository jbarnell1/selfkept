using System.Text.RegularExpressions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services;

public sealed class ProfileService : IProfileService
{
  private readonly IUserRepository _users;
  private readonly IPersonRepository _persons;
  private readonly IPasswordHasher _passwordHasher;
  private readonly IRefreshTokenService _refreshTokens;
  private readonly TimeProvider _clock;

  public ProfileService(
    IUserRepository users,
    IPersonRepository persons,
    IPasswordHasher passwordHasher,
    IRefreshTokenService refreshTokens,
    TimeProvider clock)
  {
    _users = users;
    _persons = persons;
    _passwordHasher = passwordHasher;
    _refreshTokens = refreshTokens;
    _clock = clock;
  }

  public async Task<PersonDto?> GetAsync(Guid userId, CancellationToken ct = default)
  {
    var user = await _users.GetByIdAsync(userId, ct);
    return user?.Person.ToDto();
  }

  public async Task<PersonDto?> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
  {
    var user = await _users.GetByIdAsync(userId, ct);
    if (user == null) return null;

    var timeZone = request.TimeZone.Trim();
    if (!IsPlausibleTimeZone(timeZone))
    {
      throw new DomainValidationException($"Unknown time zone '{timeZone}'. Use an IANA id such as Europe/London.");
    }

    var now = _clock.GetUtcNow().UtcDateTime;
    if (request.BirthDate is { } birthDate && birthDate > DateOnly.FromDateTime(now))
    {
      throw new DomainValidationException("Birth date cannot be in the future.");
    }

    var person = user.Person;
    person.DisplayName = request.DisplayName.Trim();
    person.SexAtBirth = request.SexAtBirth;
    person.BirthDate = request.BirthDate;
    person.Units = request.Units;
    person.TimeZone = timeZone;
    person.UpdatedAt = now;
    await _persons.UpdateAsync(person, ct);

    // The account's display name follows the person's so both read the same everywhere.
    if (user.DisplayName != person.DisplayName)
    {
      user.DisplayName = person.DisplayName;
      user.UpdatedAt = now;
      await _users.UpdateAsync(user, ct);
    }

    return person.ToDto();
  }

  public async Task<bool?> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
  {
    var user = await _users.GetByIdAsync(userId, ct);
    if (user == null) return null;

    if (!_passwordHasher.Verify(currentPassword, user.PasswordHash))
    {
      throw new DomainValidationException("The current password is not correct.");
    }
    if (currentPassword == newPassword)
    {
      throw new DomainValidationException("The new password must differ from the current one.");
    }

    user.PasswordHash = _passwordHasher.Hash(newPassword);
    user.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
    await _users.UpdateAsync(user, ct);

    // Other sessions must sign in again with the new password.
    await _refreshTokens.RevokeAllForUserAsync(userId, ct);
    return true;
  }

  private static readonly Regex IanaShape = new("^[A-Za-z][A-Za-z0-9_+-]*(/[A-Za-z0-9_+-]+){0,2}$", RegexOptions.Compiled);

  // The host's zone database decides when it has one (ICU on Linux images, Windows mappings on
  // Windows); a host built with invariant globalization still accepts a well-formed IANA id.
  private static bool IsPlausibleTimeZone(string timeZone) =>
    timeZone.Length <= 64 && (TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _) || IanaShape.IsMatch(timeZone));
}
