using System.Security.Cryptography;
using System.Text;
using Base64UrlEncoding = System.Buffers.Text.Base64Url;

namespace SelfKept.Infrastructure.Security;

/// <summary>
/// The few primitives every token in the app is built from: random values, their URL-safe text
/// form, the SHA-256 hash stored in their place, and the PKCE check. One copy, so a stored hash
/// and the value it is compared with are always made the same way.
/// </summary>
public static class TokenCrypto
{
  /// <summary>URL-safe base64 without padding (RFC 4648 section 5): safe in URLs, cookies and JSON.</summary>
  public static string Base64Url(byte[] bytes) => Base64UrlEncoding.EncodeToString(bytes);

  /// <summary>A new random value of <paramref name="bytes"/> bytes as URL-safe text.</summary>
  public static string NewToken(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

  /// <summary>Lower-case hex SHA-256 of the UTF-8 text: what is stored instead of a token or code.</summary>
  public static string Sha256Hex(string value) => Sha256Hex(Encoding.UTF8.GetBytes(value));

  public static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

  /// <summary>The S256 code challenge for a PKCE verifier.</summary>
  public static string PkceChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

  /// <summary>True when <paramref name="verifier"/> matches the S256 <paramref name="challenge"/>, compared in constant time.</summary>
  public static bool VerifyPkce(string verifier, string challenge)
  {
    var a = Encoding.ASCII.GetBytes(PkceChallenge(verifier));
    var b = Encoding.ASCII.GetBytes(challenge);
    return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
  }
}
