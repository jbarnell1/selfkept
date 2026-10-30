namespace SelfKept.Infrastructure.Services.Interpret.Ai;

/// <summary>
/// The Ai: section. Off by default: nothing is called until the user points the app at a model
/// they run themselves (Ollama, LM Studio, vLLM, anything OpenAI-compatible).
/// </summary>
public sealed class AiOptions
{
  public const string SectionName = "Ai";

  public bool Enabled { get; set; }

  /// <summary>Origin plus API prefix, for example http://host.docker.internal:11434/v1. The adapter appends /chat/completions.</summary>
  public string BaseUrl { get; set; } = "http://host.docker.internal:11434/v1";

  /// <summary>Pinned model name. No default: an upgrade must never silently switch models.</summary>
  public string Model { get; set; } = string.Empty;

  public int TimeoutSeconds { get; set; } = 20;

  /// <summary>Optional bearer token for servers that want one. Ollama needs none.</summary>
  public string? ApiKey { get; set; }

  public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);
}
