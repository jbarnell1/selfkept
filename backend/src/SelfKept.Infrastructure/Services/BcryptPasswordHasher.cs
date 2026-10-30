using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services;

/// <summary>BCrypt with a work factor of 12 (about 250 ms on a small home server).</summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
  private const int WorkFactor = 12;

  public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

  public bool Verify(string password, string hash)
  {
    if (string.IsNullOrEmpty(hash)) return false;
    try
    {
      return BCrypt.Net.BCrypt.Verify(password, hash);
    }
    catch (BCrypt.Net.SaltParseException)
    {
      return false;
    }
  }
}
