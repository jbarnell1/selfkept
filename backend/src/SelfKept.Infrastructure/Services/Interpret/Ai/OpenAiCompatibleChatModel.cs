using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret.Ai;

/// <summary>
/// POST {Ai:BaseUrl}/chat/completions in the OpenAI wire shape, which Ollama, LM Studio, vLLM and
/// llama.cpp all speak. The only outbound HTTP in the app, and only to the host the user set.
/// </summary>
public sealed class OpenAiCompatibleChatModel : IChatModel
{
  public const string HttpClientName = "ai";

  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };

  // Reasoning models (qwen3, deepseek-r1) may wrap their thinking in <think> tags; only the answer counts.
  private static readonly Regex ThinkBlock = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled);

  private readonly IHttpClientFactory _httpClientFactory;
  private readonly AiOptions _options;

  public OpenAiCompatibleChatModel(IHttpClientFactory httpClientFactory, IOptions<AiOptions> options)
  {
    _httpClientFactory = httpClientFactory;
    _options = options.Value;
  }

  public bool Enabled => _options.IsUsable;

  public string ModelName => _options.Model;

  public async Task<ChatCompletion> CompleteAsync(string system, string user, ChatOptions options, CancellationToken ct = default)
  {
    if (!Enabled) throw new InvalidOperationException("The model is not enabled (Ai:Enabled, Ai:BaseUrl and Ai:Model).");

    var client = _httpClientFactory.CreateClient(HttpClientName);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

    using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(_options.BaseUrl));
    if (!string.IsNullOrWhiteSpace(_options.ApiKey))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }
    request.Content = JsonContent.Create(new ChatRequest(
      _options.Model,
      [new ChatMessage("system", system), new ChatMessage("user", user)],
      options.Temperature,
      options.MaxTokens,
      false), options: Json);

    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    if (!response.IsSuccessStatusCode)
    {
      throw new HttpRequestException($"The model endpoint answered {(int)response.StatusCode}.", null, response.StatusCode);
    }
    var body = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, timeout.Token)
      ?? throw new InvalidOperationException("The model endpoint returned an empty body.");
    var content = body.Choices is { Count: > 0 } choices ? choices[0].Message?.Content ?? string.Empty : string.Empty;
    content = ThinkBlock.Replace(content, string.Empty).Trim();
    return new ChatCompletion(content, body.Usage?.PromptTokens, body.Usage?.CompletionTokens);
  }

  public static Uri Endpoint(string baseUrl) => new(baseUrl.TrimEnd('/') + "/chat/completions");

  private sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    double Temperature,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    bool Stream);

  private sealed record ChatMessage(string Role, string Content);

  private sealed record ChatResponse(IReadOnlyList<ChatChoice>? Choices, ChatUsage? Usage);

  private sealed record ChatChoice(ChatMessage? Message);

  private sealed record ChatUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
