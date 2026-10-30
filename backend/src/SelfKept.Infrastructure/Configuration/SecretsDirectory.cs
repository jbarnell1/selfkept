using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace SelfKept.Infrastructure.Configuration;

/// <summary>
/// File-per-setting secrets. <c>SelfKept:SecretsDirectory</c> names a folder (the container image
/// sets /secrets); every file in it is a setting named by the file, with "__" standing for ":"
/// (so <c>Jwt__Key</c> sets <c>Jwt:Key</c>). The source is inserted just below environment
/// variables, so appsettings files are overridden by a secret file and a secret file is overridden
/// by an environment variable.
/// </summary>
public static class SecretsDirectory
{
  public const string ConfigKey = "SelfKept:SecretsDirectory";

  /// <summary>Adds the secrets directory named in <paramref name="current"/>, if it exists. Returns the directory read, or null.</summary>
  public static string? Apply(IConfigurationBuilder builder, IConfiguration current)
  {
    var dir = current[ConfigKey];
    if (string.IsNullOrWhiteSpace(dir)) return null;

    var secrets = ReadSecretFiles(dir);
    if (secrets.Count == 0) return Directory.Exists(dir) ? dir : null;

    var source = new MemoryConfigurationSource { InitialData = secrets };
    var envIndex = IndexOfFirstEnvironmentSource(builder);
    if (envIndex >= 0) builder.Sources.Insert(envIndex, source);
    else builder.Sources.Add(source);
    return dir;
  }

  /// <summary>One setting per file. Trailing whitespace is trimmed; empty files and dot files are skipped.</summary>
  public static Dictionary<string, string?> ReadSecretFiles(string directory)
  {
    var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    if (!Directory.Exists(directory)) return result;
    foreach (var file in Directory.EnumerateFiles(directory))
    {
      var name = Path.GetFileName(file);
      if (name.StartsWith('.')) continue;
      var value = File.ReadAllText(file).Trim();
      if (value.Length == 0) continue;
      result[name.Replace("__", ":")] = value;
    }
    return result;
  }

  private static int IndexOfFirstEnvironmentSource(IConfigurationBuilder builder)
  {
    for (var i = 0; i < builder.Sources.Count; i++)
    {
      if (builder.Sources[i] is EnvironmentVariablesConfigurationSource) return i;
    }
    return -1;
  }
}
