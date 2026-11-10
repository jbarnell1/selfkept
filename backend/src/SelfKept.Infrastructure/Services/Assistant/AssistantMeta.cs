using System.Text.Json;
using SelfKept.Core.DTOs;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// The meta JSON on a message: what it saved (ids, kinds and tags only), whether that was undone,
/// the check-in topic it answered and why a reply was replaced. Never free text.
/// </summary>
public sealed record AssistantMeta(
  IReadOnlyList<CaptureRefDto>? Captures = null,
  bool? Undone = null,
  string? Topic = null,
  string? Reason = null)
{
  public const string Journal = "journal";
  public const string Memory = "memory";
  public const string Mood = "mood";

  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
  };

  public string ToJson() => JsonSerializer.Serialize(this, Json);

  public static AssistantMeta From(string? json)
  {
    if (string.IsNullOrWhiteSpace(json)) return new AssistantMeta();
    try
    {
      return JsonSerializer.Deserialize<AssistantMeta>(json, Json) ?? new AssistantMeta();
    }
    catch (JsonException)
    {
      return new AssistantMeta();
    }
  }
}
