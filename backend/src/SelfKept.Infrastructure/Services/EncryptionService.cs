using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services;

/// <summary>
/// Encrypts secrets stored in the database (future device tokens, export credentials).
///
/// Current format ("v2:"): <c>v2:</c> + base64(nonce[12] | tag[16] | ciphertext), AES-256-GCM.
/// The 32-byte key is derived from <c>Encryption:Key</c> with HKDF-SHA256, so the setting can be
/// any string of reasonable length (a base64 or hex secret, a passphrase) and is not truncated
/// or padded. GCM authenticates the data: any change to the stored value fails to decrypt.
///
/// Legacy format (no prefix): base64(iv[16] | AES-CBC ciphertext), key = the setting padded with
/// spaces / truncated to 32 characters, no authentication. Decrypt still reads it so a database
/// migrated from the sibling product is never stranded. <c>Encryption:WriteV2</c> (default true
/// here; this product never wrote the legacy format) selects what Encrypt writes.
/// </summary>
public class EncryptionService : IEncryptionService
{
  private readonly Microsoft.Extensions.Logging.ILogger<EncryptionService>? _logger;

  public const string CurrentPrefix = "v2:";

  private const int NonceSize = 12;
  private const int TagSize = 16;
  private static readonly byte[] HkdfSalt = Encoding.UTF8.GetBytes("EncryptionService.v2");
  private static readonly byte[] HkdfInfo = Encoding.UTF8.GetBytes("aes-256-gcm data encryption key");

  private readonly string _encryptionKey;
  private readonly byte[] _gcmKey;

  /// <summary>True when Encrypt writes the v2 format (Encryption:WriteV2). Default true.</summary>
  public bool WriteV2 { get; }

  /// <inheritdoc />
  public bool WritesCurrentFormat => WriteV2;

  public EncryptionService(IConfiguration configuration, Microsoft.Extensions.Logging.ILogger<EncryptionService>? logger = null)
  {
    _logger = logger;
    _encryptionKey = configuration["Encryption:Key"]
      ?? throw new InvalidOperationException("Encryption key not configured");
    WriteV2 = !bool.TryParse(configuration["Encryption:WriteV2"], out var writeV2) || writeV2;
    _gcmKey = HKDF.DeriveKey(
      HashAlgorithmName.SHA256,
      Encoding.UTF8.GetBytes(_encryptionKey),
      32,
      HkdfSalt,
      HkdfInfo);
  }

  /// <inheritdoc />
  public bool IsCurrentFormat(string? cipherText) =>
    !string.IsNullOrEmpty(cipherText) &&
    cipherText.StartsWith(CurrentPrefix, StringComparison.Ordinal);

  public string Encrypt(string plainText)
  {
    if (string.IsNullOrEmpty(plainText))
      return string.Empty;

    if (!WriteV2)
      return EncryptLegacy(plainText);

    var plain = Encoding.UTF8.GetBytes(plainText);
    var nonce = RandomNumberGenerator.GetBytes(NonceSize);
    var cipher = new byte[plain.Length];
    var tag = new byte[TagSize];

    using (var gcm = new AesGcm(_gcmKey, TagSize))
    {
      gcm.Encrypt(nonce, plain, cipher, tag);
    }

    var blob = new byte[NonceSize + TagSize + cipher.Length];
    Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
    Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
    Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);
    return CurrentPrefix + Convert.ToBase64String(blob);
  }

  // The legacy format, kept only so a migrated database can be read.
  private string EncryptLegacy(string plainText)
  {
    using var aes = Aes.Create();
    aes.Key = Encoding.UTF8.GetBytes(_encryptionKey.PadRight(32).Substring(0, 32));
    aes.GenerateIV();
    using var encryptor = aes.CreateEncryptor();
    using var ms = new MemoryStream();
    ms.Write(aes.IV, 0, aes.IV.Length);
    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
    using (var sw = new StreamWriter(cs))
    {
      sw.Write(plainText);
    }
    return Convert.ToBase64String(ms.ToArray());
  }

  public string? TryDecrypt(string cipherText)
  {
    try
    {
      return Decrypt(cipherText);
    }
    catch (InvalidOperationException)
    {
      // Never log the value: only that a stored field could not be read with the current key.
      if (_logger != null)
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger, "A stored encrypted field could not be decrypted with the current key; it is shown as empty.");
      return null;
    }
  }

  public string Decrypt(string cipherText)
  {
    if (string.IsNullOrEmpty(cipherText))
      return string.Empty;

    try
    {
      return IsCurrentFormat(cipherText)
        ? DecryptCurrent(cipherText)
        : DecryptLegacy(cipherText);
    }
    catch
    {
      throw new InvalidOperationException("Failed to decrypt data");
    }
  }

  private string DecryptCurrent(string cipherText)
  {
    var blob = Convert.FromBase64String(cipherText[CurrentPrefix.Length..]);
    if (blob.Length < NonceSize + TagSize)
      throw new CryptographicException("Ciphertext too short");

    var nonce = blob.AsSpan(0, NonceSize);
    var tag = blob.AsSpan(NonceSize, TagSize);
    var cipher = blob.AsSpan(NonceSize + TagSize);
    var plain = new byte[cipher.Length];

    using var gcm = new AesGcm(_gcmKey, TagSize);
    gcm.Decrypt(nonce, cipher, tag, plain);
    return Encoding.UTF8.GetString(plain);
  }

  // Read-only support for rows written in the legacy format. Do not use for new data: CBC without a MAC.
  private string DecryptLegacy(string cipherText)
  {
    var fullCipher = Convert.FromBase64String(cipherText);

    using var aes = Aes.Create();
    aes.Key = Encoding.UTF8.GetBytes(_encryptionKey.PadRight(32).Substring(0, 32));

    var iv = new byte[aes.BlockSize / 8];
    var cipher = new byte[fullCipher.Length - iv.Length];
    Array.Copy(fullCipher, 0, iv, 0, iv.Length);
    Array.Copy(fullCipher, iv.Length, cipher, 0, cipher.Length);
    aes.IV = iv;

    using var decryptor = aes.CreateDecryptor();
    using var msDecrypt = new MemoryStream(cipher);
    using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
    using var srDecrypt = new StreamReader(csDecrypt);
    return srDecrypt.ReadToEnd();
  }
}
