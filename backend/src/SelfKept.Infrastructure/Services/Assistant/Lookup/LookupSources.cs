using System.Text.Json;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Assistant.Lookup;

/// <summary>A lookup's sources as stored: a JSON list of { title, url }, encrypted like message content.</summary>
public static class LookupSources
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static string Encode(IReadOnlyList<LookupSourceDto> sources, IEncryptionService encryption) =>
    encryption.Encrypt(JsonSerializer.Serialize(sources, Json));

  /// <summary>Empty when there is nothing stored or it cannot be read.</summary>
  public static IReadOnlyList<LookupSourceDto> Decode(string? cipher, IEncryptionService encryption)
  {
    if (string.IsNullOrEmpty(cipher)) return [];
    var json = encryption.TryDecrypt(cipher);
    if (string.IsNullOrEmpty(json)) return [];
    try
    {
      return JsonSerializer.Deserialize<List<LookupSourceDto>>(json, Json) ?? [];
    }
    catch (JsonException)
    {
      return [];
    }
  }
}
