using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SelfKept.Core.Auth;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services;

/// <summary>
/// Short-lived first-party access tokens (Jwt:AccessTokenMinutes, default 15). Claims are the
/// compact JWT names ("sub", "email", "role", "household_id", "person_id"); the bearer handler is
/// configured not to remap them, so controllers read the same names.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
  private readonly SigningCredentials _credentials;
  private readonly string _issuer;
  private readonly string _audience;
  private readonly TimeSpan _lifetime;
  private readonly TimeProvider _clock;

  public JwtTokenService(IConfiguration configuration, TimeProvider clock)
  {
    var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured");
    _credentials = new SigningCredentials(
      new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
    _issuer = configuration["Jwt:Issuer"] ?? "api";
    _audience = configuration["Jwt:Audience"] ?? "clients";
    _lifetime = TimeSpan.FromMinutes(configuration.GetValue<int?>("Jwt:AccessTokenMinutes") ?? 15);
    _clock = clock;
  }

  public AccessToken CreateAccessToken(User user, HouseholdRole role)
  {
    var now = _clock.GetUtcNow().UtcDateTime;
    var expires = now.Add(_lifetime);
    var claims = new List<Claim>
    {
      new(TokenClaims.Subject, user.Id.ToString()),
      new(TokenClaims.Email, user.Email),
      new(TokenClaims.Role, TokenRoles.For(role)),
      new(TokenClaims.Person, user.PersonId.ToString()),
      new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
    };
    if (user.HouseholdId is { } householdId)
    {
      claims.Add(new Claim(TokenClaims.Household, householdId.ToString()));
    }

    var token = new JwtSecurityToken(
      issuer: _issuer,
      audience: _audience,
      claims: claims,
      notBefore: now,
      expires: expires,
      signingCredentials: _credentials);

    return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
  }
}
