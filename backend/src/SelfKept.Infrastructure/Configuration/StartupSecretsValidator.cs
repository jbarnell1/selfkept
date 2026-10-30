using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace SelfKept.Infrastructure.Configuration;

/// <summary>
/// Refuses to start the API with a missing, placeholder or too-short secret. The committed
/// appsettings.Development.json ships development-only keys; outside the Development environment
/// those exact values, and anything that looks like a placeholder, stop the process.
/// </summary>
public static class StartupSecretsValidator
{
  public const string JwtKeySetting = "Jwt:Key";
  public const string EncryptionKeySetting = "Encryption:Key";

  /// <summary>HMAC-SHA256 wants at least 256 bits; one character is one byte for the ASCII key.</summary>
  public const int MinKeyLength = 32;

  /// <summary>Keys that mark a value as development-only. Refused outside Development.</summary>
  public const string DevOnlyMarker = "dev-only";

  private static readonly Regex PlaceholderPattern = new(
    @"(^|[^a-z])your[_-]|change[_\- ]?me|replace[_\- ]?me|placeholder|<[^>]*>|\bTODO\b|insert[_\- ]?(key|secret)",
    RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

  public sealed record Result(IReadOnlyList<string> Errors)
  {
    public bool IsValid => Errors.Count == 0;
  }

  public static bool LooksLikePlaceholder(string? value)
  {
    if (string.IsNullOrWhiteSpace(value)) return false;
    return PlaceholderPattern.IsMatch(value) || value.Distinct().Count() < 8;
  }

  public static Result Validate(IConfiguration configuration, string environmentName)
  {
    var errors = new List<string>();
    var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

    foreach (var setting in new[] { JwtKeySetting, EncryptionKeySetting })
    {
      var value = configuration[setting];
      if (string.IsNullOrWhiteSpace(value))
      {
        errors.Add($"{setting} is not set. Set it to a random secret of at least {MinKeyLength} characters (for example `openssl rand -base64 48`).");
        continue;
      }
      if (value.Length < MinKeyLength)
      {
        errors.Add($"{setting} is too short ({value.Length} characters); it needs at least {MinKeyLength}.");
      }
      if (value.Any(c => c > 127))
      {
        errors.Add($"{setting} must contain only ASCII characters.");
      }
      if (isDevelopment) continue;
      if (value.Contains(DevOnlyMarker, StringComparison.OrdinalIgnoreCase))
      {
        errors.Add($"{setting} is the development-only key from appsettings.Development.json and cannot be used in {environmentName}. Set a real secret (environment variable {setting.Replace(":", "__")} or a file in {SecretsDirectory.ConfigKey}).");
      }
      else if (LooksLikePlaceholder(value))
      {
        errors.Add($"{setting} looks like a placeholder or has almost no variety. Set a real random secret.");
      }
    }

    return new Result(errors);
  }

  public static void ValidateOrThrow(IConfiguration configuration, string environmentName)
  {
    var result = Validate(configuration, environmentName);
    if (!result.IsValid)
    {
      throw new InvalidOperationException(
        "Startup configuration is not usable:\n  - " + string.Join("\n  - ", result.Errors));
    }
  }
}
