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

  /// <summary>
  /// Sent as reasoning_effort when set. "none" tells a thinking model (qwen3, gemma) served by
  /// Ollama to answer without thinking first, which is what these short replies want. Empty
  /// sends nothing, because some servers refuse a field they do not know.
  /// </summary>
  public string ReasoningEffort { get; set; } = string.Empty;

  /// <summary>
  /// Added to every max_tokens. Servers count a model's thinking against the limit, so a model
  /// that thinks anyway still has room left for its answer.
  /// </summary>
  public int HeadroomTokens { get; set; } = 600;

  /// <summary>
  /// One "Name: value" header sent with calls a person is waiting on, for a gateway that queues
  /// by priority (X-GPU-Priority: interactive). Background calls go without it. Empty sends nothing.
  /// </summary>
  public string InteractiveHeader { get; set; } = string.Empty;

  public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);
}
