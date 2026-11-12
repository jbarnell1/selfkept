using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret.Ai;

/// <summary>
/// POST {Ai:BaseUrl}/chat/completions in the OpenAI wire shape, which Ollama, LM Studio, vLLM and
/// llama.cpp all speak. The only model the app talks to, and only on the host the user set.
/// </summary>
public sealed class OpenAiCompatibleChatModel : IChatModel
{
  public const string HttpClientName = "ai";
  private const string NotEnabled = "The model is not enabled (Ai:Enabled, Ai:BaseUrl and Ai:Model).";

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

  public bool VisionEnabled => _options.IsVisionUsable;

  public Task<ChatCompletion> CompleteAsync(string system, string user, ChatOptions options, CancellationToken ct = default) =>
    CompleteAsync([new ChatTurn(ChatTurn.System, system), new ChatTurn(ChatTurn.User, user)], options, ct);

  public async Task<ChatCompletion> CompleteAsync(IReadOnlyList<ChatTurn> turns, ChatOptions options, CancellationToken ct = default)
  {
    if (!Enabled) throw new InvalidOperationException(NotEnabled);

    var client = _httpClientFactory.CreateClient(HttpClientName);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeoutFor(options));

    using var request = BuildRequest(turns, options, stream: false);
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

  /// <summary>
  /// The answer as it is written (stream: true, server-sent events). The configured timeout is the
  /// longest silence allowed: it restarts with every piece, so a slow model on a home server may
  /// take its time as long as it keeps writing.
  /// </summary>
  public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> turns, ChatOptions options, [EnumeratorCancellation] CancellationToken ct = default)
  {
    if (!Enabled) throw new InvalidOperationException(NotEnabled);

    var client = _httpClientFactory.CreateClient(HttpClientName);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeoutFor(options));

    using var request = BuildRequest(turns, options, stream: true);
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    if (!response.IsSuccessStatusCode)
    {
      throw new HttpRequestException($"The model endpoint answered {(int)response.StatusCode}.", null, response.StatusCode);
    }
    await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
    await foreach (var piece in ReadStreamAsync(body, timeout.Token))
    {
      timeout.CancelAfter(TimeoutFor(options));
      yield return piece;
    }
  }

  /// <summary>
  /// Parses an OpenAI-style event stream: "data: {json}" lines carrying choices[0].delta.content,
  /// ended by "data: [DONE]". Comment lines, blank lines and lines that are not JSON are skipped,
  /// and anything inside think tags is dropped even when a tag is split across pieces.
  /// </summary>
  public static async IAsyncEnumerable<string> ReadStreamAsync(Stream body, [EnumeratorCancellation] CancellationToken ct = default)
  {
    using var reader = new StreamReader(body);
    var filter = new ThinkFilter();
    while (await reader.ReadLineAsync(ct) is { } line)
    {
      if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
      var payload = line["data:".Length..].Trim();
      if (payload.Length == 0) continue;
      if (payload == "[DONE]") break;

      var content = DeltaContent(payload);
      if (string.IsNullOrEmpty(content)) continue;
      var visible = filter.Push(content);
      if (visible.Length > 0) yield return visible;
    }
    var rest = filter.Flush();
    if (rest.Length > 0) yield return rest;
  }

  /// <summary>GET {Ai:BaseUrl}/models with a short timeout: the server is up, nothing is generated.</summary>
  public async Task<bool> PingAsync(CancellationToken ct = default)
  {
    if (!Enabled) return false;
    var client = _httpClientFactory.CreateClient(HttpClientName);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(5, Math.Max(1, _options.TimeoutSeconds))));
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.BaseUrl.TrimEnd('/') + "/models"));
      if (!string.IsNullOrWhiteSpace(_options.ApiKey))
      {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
      }
      using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
      return response.IsSuccessStatusCode;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or UriFormatException)
    {
      return false;
    }
  }

  public static Uri Endpoint(string baseUrl) => new(baseUrl.TrimEnd('/') + "/chat/completions");

  /// <summary>Ai:TimeoutSeconds, or Ai:VisionTimeoutSeconds for a photo call.</summary>
  public TimeSpan TimeoutFor(ChatOptions options) =>
    TimeSpan.FromSeconds(Math.Max(1, options.Vision ? _options.VisionTimeoutSeconds : _options.TimeoutSeconds));

  private static string? DeltaContent(string payload)
  {
    try
    {
      var chunk = JsonSerializer.Deserialize<StreamChunk>(payload, Json);
      return chunk?.Choices is { Count: > 0 } choices ? choices[0].Delta?.Content : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  /// <summary>The request as it goes out. Public so the wire shape can be tested without a server.</summary>
  public HttpRequestMessage BuildRequest(IReadOnlyList<ChatTurn> turns, ChatOptions options, bool stream)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(_options.BaseUrl));
    if (!string.IsNullOrWhiteSpace(_options.ApiKey))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }
    if (!options.Background && SplitHeader(_options.InteractiveHeader) is { } header)
    {
      request.Headers.TryAddWithoutValidation(header.Name, header.Value);
    }
    // Serialized up front so the body goes out with a Content-Length. A streamed (chunked) body
    // arrives empty behind a small proxy that only reads the stated length. A photo makes the body
    // large, which is all the more reason to state it.
    var body = JsonSerializer.SerializeToUtf8Bytes(new ChatRequest(
      options.Vision ? _options.PhotoModel : _options.Model,
      turns.Select(RequestMessage).ToList(),
      options.Temperature,
      options.MaxTokens + Math.Max(0, _options.HeadroomTokens),
      stream,
      string.IsNullOrWhiteSpace(_options.ReasoningEffort) ? null : _options.ReasoningEffort.Trim(),
      options.JsonMode ? new ResponseFormat("json_object") : null), Json);
    request.Content = new ByteArrayContent(body);
    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
    return request;
  }

  /// <summary>
  /// A turn as it goes on the wire: a plain string, or, when it carries photos, OpenAI content
  /// parts (the text, then one image_url part per photo with a data: URL).
  /// </summary>
  private static OutgoingMessage RequestMessage(ChatTurn turn)
  {
    if (turn.Images is not { Count: > 0 } images) return new OutgoingMessage(turn.Role, turn.Content);
    var parts = new List<object> { new TextPart("text", turn.Content) };
    foreach (var image in images)
    {
      parts.Add(new ImagePart("image_url", new ImageUrl($"data:{image.MediaType};base64,{Convert.ToBase64String(image.Bytes)}")));
    }
    return new OutgoingMessage(turn.Role, parts);
  }

  /// <summary>"Name: value" as its two parts. Null when either part is missing.</summary>
  public static (string Name, string Value)? SplitHeader(string? header)
  {
    if (string.IsNullOrWhiteSpace(header)) return null;
    var at = header.IndexOf(':', StringComparison.Ordinal);
    if (at <= 0) return null;
    var name = header[..at].Trim();
    var value = header[(at + 1)..].Trim();
    return name.Length == 0 || value.Length == 0 ? null : (name, value);
  }

  /// <summary>
  /// Removes think blocks from a stream of pieces. Text that could still turn out to be the start
  /// of a tag is held back until the next piece settles it.
  /// </summary>
  public sealed class ThinkFilter
  {
    private const string Open = "<think>";
    private const string Close = "</think>";

    private readonly StringBuilder _pending = new();
    private bool _thinking;

    public string Push(string piece)
    {
      _pending.Append(piece);
      var output = new StringBuilder();
      while (true)
      {
        var text = _pending.ToString();
        var tag = _thinking ? Close : Open;
        var at = text.IndexOf(tag, StringComparison.Ordinal);
        if (at >= 0)
        {
          if (!_thinking) output.Append(text, 0, at);
          _pending.Clear().Append(text, at + tag.Length, text.Length - at - tag.Length);
          _thinking = !_thinking;
          continue;
        }
        // No whole tag: keep only a tail that could be the start of one.
        var keep = PartialTagLength(text, tag);
        if (!_thinking) output.Append(text, 0, text.Length - keep);
        _pending.Clear().Append(text, text.Length - keep, keep);
        return output.ToString();
      }
    }

    /// <summary>What is left when the stream ends: held-back text that never became a tag.</summary>
    public string Flush()
    {
      var rest = _thinking ? string.Empty : _pending.ToString();
      _pending.Clear();
      return rest;
    }

    private static int PartialTagLength(string text, string tag)
    {
      for (var length = Math.Min(tag.Length - 1, text.Length); length > 0; length--)
      {
        if (string.CompareOrdinal(text, text.Length - length, tag, 0, length) == 0) return length;
      }
      return 0;
    }
  }

  private sealed record ChatRequest(
    string Model,
    IReadOnlyList<OutgoingMessage> Messages,
    double Temperature,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    bool Stream,
    [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort,
    [property: JsonPropertyName("response_format")] ResponseFormat? ResponseFormat);

  private sealed record ResponseFormat(string Type);

  /// <summary>Content is a string or a list of content parts; both serialize by their runtime type.</summary>
  private sealed record OutgoingMessage(string Role, object Content);

  private sealed record TextPart(string Type, string Text);

  private sealed record ImagePart(string Type, [property: JsonPropertyName("image_url")] ImageUrl ImageUrl);

  private sealed record ImageUrl(string Url);

  private sealed record ChatMessage(string? Role, string? Content);

  private sealed record ChatResponse(IReadOnlyList<ChatChoice>? Choices, ChatUsage? Usage);

  private sealed record ChatChoice(ChatMessage? Message);

  private sealed record StreamChunk(IReadOnlyList<StreamChoice>? Choices);

  private sealed record StreamChoice(ChatMessage? Delta);

  private sealed record ChatUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
