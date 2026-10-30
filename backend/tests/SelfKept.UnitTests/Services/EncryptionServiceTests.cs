using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.UnitTests.Services;

public class EncryptionServiceTests
{
  private static EncryptionService Create(string key = "unit-test-encryption-key-with-plenty-of-entropy-123456", bool? writeV2 = null)
  {
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Encryption:Key"] = key,
      ["Encryption:WriteV2"] = writeV2?.ToString(),
    }).Build();
    return new EncryptionService(config);
  }

  [Fact]
  public void Encrypt_ThenDecrypt_RoundTrips()
  {
    var service = Create();
    const string secret = "device-token-with-unicode: éè ☃";

    var cipher = service.Encrypt(secret);

    cipher.Should().StartWith(EncryptionService.CurrentPrefix);
    service.IsCurrentFormat(cipher).Should().BeTrue();
    service.WritesCurrentFormat.Should().BeTrue();
    service.Decrypt(cipher).Should().Be(secret);
  }

  [Fact]
  public void Encrypt_SamePlaintextTwice_ProducesDifferentCiphertext()
  {
    var service = Create();

    service.Encrypt("same").Should().NotBe(service.Encrypt("same"), "every encryption uses a fresh nonce");
  }

  [Fact]
  public void Decrypt_TamperedCiphertext_Throws()
  {
    var service = Create();
    var cipher = service.Encrypt("do not touch");
    var bytes = Convert.FromBase64String(cipher[EncryptionService.CurrentPrefix.Length..]);
    bytes[^1] ^= 0x01;
    var tampered = EncryptionService.CurrentPrefix + Convert.ToBase64String(bytes);

    var act = () => service.Decrypt(tampered);

    act.Should().Throw<InvalidOperationException>();
  }

  [Fact]
  public void Decrypt_WithDifferentKey_Throws()
  {
    var cipher = Create().Encrypt("secret");

    var act = () => Create(key: "a-completely-different-key-with-plenty-of-entropy-98765").Decrypt(cipher);

    act.Should().Throw<InvalidOperationException>();
  }

  [Fact]
  public void TryDecrypt_WithDifferentKey_ReturnsNull()
  {
    var cipher = Create().Encrypt("secret");

    IEncryptionService other = Create(key: "a-completely-different-key-with-plenty-of-entropy-98765");

    other.TryDecrypt(cipher).Should().BeNull();
  }

  [Fact]
  public void TryDecrypt_WithTheRightKey_RoundTrips()
  {
    IEncryptionService service = Create();

    service.TryDecrypt(service.Encrypt("a note")).Should().Be("a note");
  }

  [Fact]
  public void TryDecrypt_OnTheInterfaceDefault_SwallowsOnlyDecryptFailures()
  {
    var fake = new Moq.Mock<IEncryptionService> { CallBase = true };
    fake.Setup(e => e.Decrypt(Moq.It.IsAny<string>())).Throws(new InvalidOperationException("Failed to decrypt data"));

    fake.Object.TryDecrypt("v2:anything").Should().BeNull();
  }

  [Fact]
  public void LegacyFormat_IsStillReadable_WhenWriteV2IsOff()
  {
    var legacyWriter = Create(writeV2: false);
    var legacy = legacyWriter.Encrypt("migrated row");

    legacy.Should().NotStartWith(EncryptionService.CurrentPrefix);
    legacyWriter.WritesCurrentFormat.Should().BeFalse();
    Create().Decrypt(legacy).Should().Be("migrated row");
  }

  [Fact]
  public void EmptyInput_RoundTripsAsEmpty()
  {
    var service = Create();
    service.Encrypt(string.Empty).Should().BeEmpty();
    service.Decrypt(string.Empty).Should().BeEmpty();
  }

  [Fact]
  public void MissingKey_RefusesToConstruct()
  {
    var config = new ConfigurationBuilder().Build();
    var act = () => new EncryptionService(config);
    act.Should().Throw<InvalidOperationException>();
  }
}
