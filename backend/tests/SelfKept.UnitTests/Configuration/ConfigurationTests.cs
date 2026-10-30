using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SelfKept.Infrastructure.Configuration;
using Xunit;

namespace SelfKept.UnitTests.Configuration;

public class SecretsDirectoryTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), "selfkept-secrets-" + Guid.NewGuid().ToString("N"));

  public SecretsDirectoryTests() => Directory.CreateDirectory(_dir);

  public void Dispose() => Directory.Delete(_dir, recursive: true);

  [Fact]
  public void ReadSecretFiles_MapsDoubleUnderscoreToColon_AndSkipsEmptyAndDotFiles()
  {
    File.WriteAllText(Path.Combine(_dir, "Jwt__Key"), "the-jwt-key\n");
    File.WriteAllText(Path.Combine(_dir, "ConnectionStrings__SelfKept"), "Host=db;Database=selfkept");
    File.WriteAllText(Path.Combine(_dir, "db_password"), "pw");
    File.WriteAllText(Path.Combine(_dir, "Empty__Setting"), "   ");
    File.WriteAllText(Path.Combine(_dir, ".hidden"), "nope");

    var secrets = SecretsDirectory.ReadSecretFiles(_dir);

    secrets.Should().BeEquivalentTo(new Dictionary<string, string?>
    {
      ["Jwt:Key"] = "the-jwt-key",
      ["ConnectionStrings:SelfKept"] = "Host=db;Database=selfkept",
      ["db_password"] = "pw",
    });
  }

  [Fact]
  public void Apply_SecretsOverrideAppsettings_ButEnvironmentVariablesWin()
  {
    File.WriteAllText(Path.Combine(_dir, "Jwt__Key"), "from-secret-file");
    File.WriteAllText(Path.Combine(_dir, "Encryption__Key"), "from-secret-file");
    var envName = "SELFKEPT_TEST_" + Guid.NewGuid().ToString("N");
    Environment.SetEnvironmentVariable(envName + "Encryption__Key", "from-environment");
    try
    {
      var builder = new ConfigurationManager();
      builder.AddInMemoryCollection(new Dictionary<string, string?>
      {
        [SecretsDirectory.ConfigKey] = _dir,
        ["Jwt:Key"] = "from-appsettings",
        ["Encryption:Key"] = "from-appsettings",
      });
      builder.AddEnvironmentVariables(envName);

      var applied = SecretsDirectory.Apply(builder, builder);

      applied.Should().Be(_dir);
      builder["Jwt:Key"].Should().Be("from-secret-file");
      builder["Encryption:Key"].Should().Be("from-environment");
    }
    finally
    {
      Environment.SetEnvironmentVariable(envName + "Encryption__Key", null);
    }
  }

  [Fact]
  public void Apply_AnEmptyEnvironmentVariable_DoesNotHideASecretFile()
  {
    // compose passes every SELFKEPT_* line, set or not: an unset one arrives as an empty variable.
    File.WriteAllText(Path.Combine(_dir, "Sso__ClientSecret"), "from-secret-file");
    File.WriteAllText(Path.Combine(_dir, "Oura__ClientSecret"), "from-secret-file");
    var envName = "SELFKEPT_TEST_" + Guid.NewGuid().ToString("N");
    Environment.SetEnvironmentVariable(envName + "Sso__ClientSecret", " ");
    Environment.SetEnvironmentVariable(envName + "Oura__ClientSecret", "from-environment");
    try
    {
      var builder = new ConfigurationManager();
      builder.AddInMemoryCollection(new Dictionary<string, string?> { [SecretsDirectory.ConfigKey] = _dir });
      builder.AddEnvironmentVariables(envName);

      SecretsDirectory.Apply(builder, builder);

      builder["Sso:ClientSecret"].Should().Be("from-secret-file");
      builder["Oura:ClientSecret"].Should().Be("from-environment", "a value in the environment still wins");
    }
    finally
    {
      Environment.SetEnvironmentVariable(envName + "Sso__ClientSecret", null);
      Environment.SetEnvironmentVariable(envName + "Oura__ClientSecret", null);
    }
  }

  [Fact]
  public void Apply_WithoutSetting_DoesNothing()
  {
    var builder = new ConfigurationManager();
    SecretsDirectory.Apply(builder, builder).Should().BeNull();
  }
}

public class StartupSecretsValidatorTests
{
  private static IConfiguration Config(string? jwt, string? enc) =>
    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Jwt:Key"] = jwt,
      ["Encryption:Key"] = enc,
    }).Build();

  private const string DevKey = "dev-only-jwt-signing-key-not-for-production-0123456789abcdef";
  private const string RealKey = "k8Qm2vXz9Lp4Rt7Wn1Yb6Hc3Jf5Dg0Sa8Ue2Io4Pq";

  [Fact]
  public void DevOnlyKeys_AreAcceptedInDevelopment()
  {
    StartupSecretsValidator.Validate(Config(DevKey, DevKey), "Development").IsValid.Should().BeTrue();
  }

  [Fact]
  public void DevOnlyKeys_AreRefusedOutsideDevelopment()
  {
    var result = StartupSecretsValidator.Validate(Config(DevKey, RealKey), "Production");

    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle().Which.Should().Contain("Jwt:Key").And.Contain("development-only");
  }

  [Fact]
  public void MissingOrShortKeys_AreRefusedEverywhere()
  {
    var result = StartupSecretsValidator.Validate(Config(null, "short"), "Development");

    result.Errors.Should().HaveCount(2);
    result.Errors.Should().Contain(e => e.Contains("Jwt:Key is not set"));
    result.Errors.Should().Contain(e => e.Contains("Encryption:Key is too short"));
  }

  [Fact]
  public void PlaceholderKey_IsRefusedInProduction()
  {
    var result = StartupSecretsValidator.Validate(Config("CHANGE_ME_please_replace_this_value_now_1234", RealKey), "Production");

    result.Errors.Should().ContainSingle().Which.Should().Contain("placeholder");
  }

  [Fact]
  public void RealKeys_PassInProduction()
  {
    StartupSecretsValidator.Validate(Config(RealKey, RealKey + "x"), "Production").IsValid.Should().BeTrue();
  }

  [Fact]
  public void ValidateOrThrow_ListsEveryProblem()
  {
    var act = () => StartupSecretsValidator.ValidateOrThrow(Config(null, null), "Production");

    act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Jwt:Key").And.Contain("Encryption:Key");
  }
}
