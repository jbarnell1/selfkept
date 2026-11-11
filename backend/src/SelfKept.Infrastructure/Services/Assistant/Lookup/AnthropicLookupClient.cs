using System.Text;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Assistant.Lookup;

/// <summary>
/// The one cloud call (docs/lookup-design.md, "The cloud call"), through the official Anthropic SDK.
/// This is the only file that references the SDK. The request holds the fixed system prompt, the
/// text from the lookup screen and the provider's server-side web search: no history, no brief,
/// no person id, no metadata. One attempt (the SDK's own retries are off), cut off after
/// Lookup:TimeoutSeconds. Stop reasons are checked before any text is read, and every failure is
/// an outcome, never a partial answer. Nothing here logs the question, the answer or the key.
///
/// The request opts in to the provider's server-side fallback ("fallbacks": "default", beta
/// server-side-fallback-2026-07-01): when a safety classifier declines the question, the
/// provider re-runs it on the model it recommends for that case inside the same call, and the
/// model that answered is what is stored. A pause_turn (the provider's search loop paused) is
/// resumed by sending the conversation back with the paused content, at most three times.
/// </summary>
public sealed class AnthropicLookupClient : ICloudLookupClient
{
  public const string ProviderName = "Anthropic";
  public const string HttpClientName = "lookup";
  public const int MaxContinuations = 3;
  public const int MaxTokens = 16000;
  public const int MaxSources = 10;

  /// <summary>Fixed, in code. The person's text is the only other thing sent.</summary>
  public const string SystemPrompt =
    "You answer one health research question for an adult reader. Search the web for good sources, " +
    "then write a short research summary in plain language, under 300 words. Cite your sources. " +
    "Say how strong the evidence is, and say plainly when it is weak, mixed or missing. " +
    "Do not diagnose the reader and do not tell them what is wrong with them. " +
    "Give no dose or schedule beyond what a cited source states, and name the source when you give one. " +
    "If the question needs a clinician, say so in one sentence.";

  private readonly LookupOptions _options;
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ILogger<AnthropicLookupClient> _logger;

  public AnthropicLookupClient(IOptions<LookupOptions> options, IHttpClientFactory httpClientFactory, ILogger<AnthropicLookupClient> logger)
  {
    _options = options.Value;
    _httpClientFactory = httpClientFactory;
    _logger = logger;
  }

  public string Provider => ProviderName;

  public string Model => _options.EffectiveModel;

  public bool IsUsable => _options.IsUsable;

  public async Task<CloudLookupResult> AskAsync(string text, CancellationToken ct = default)
  {
    if (!IsUsable) return CloudLookupResult.Failed(CloudLookupOutcome.KeyRejected, "not configured");

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
    using var client = new AnthropicClient
    {
      ApiKey = _options.ApiKey,
      BaseUrl = "https://api.anthropic.com",
      HttpClient = _httpClientFactory.CreateClient(HttpClientName),
      MaxRetries = 0,
      Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)),
      OpenTelemetry = false,
    };

    var collected = new Collected();
    var echo = new List<BetaContentBlockParam>();
    for (var continuation = 0; ; continuation++)
    {
      BetaMessage response;
      try
      {
        response = await client.Beta.Messages.Create(Parameters(text, echo), timeout.Token);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        var (outcome, error) = Classify(ex);
        _logger.LogWarning("Cloud lookup call failed: {Outcome} ({Error})", outcome, error);
        return collected.Failed(outcome, error);
      }

      collected.Add(response);
      var stop = response.StopReason;
      if (stop == BetaStopReason.Refusal) return collected.Failed(CloudLookupOutcome.Refused, "refusal");
      if (stop == BetaStopReason.MaxTokens || stop == BetaStopReason.ModelContextWindowExceeded)
      {
        return collected.Failed(CloudLookupOutcome.CutOff, stop == BetaStopReason.MaxTokens ? "max_tokens" : "context_window");
      }
      if (stop == BetaStopReason.PauseTurn)
      {
        if (continuation >= MaxContinuations) return collected.Failed(CloudLookupOutcome.CutOff, "pause_turn limit");
        echo.AddRange(Echo(response));
        continue;
      }
      break;
    }

    if (collected.SearchesFailed) return collected.Failed(CloudLookupOutcome.SearchFailed, "web search error: " + collected.SearchError);
    var answer = collected.Text.ToString().Trim();
    if (answer.Length == 0) return collected.Failed(CloudLookupOutcome.CutOff, "empty answer");
    return new CloudLookupResult(CloudLookupOutcome.Answered, answer, collected.Sources(), collected.Model ?? Model, collected.InputTokens, collected.OutputTokens);
  }

  private MessageCreateParams Parameters(string text, IReadOnlyList<BetaContentBlockParam> echo)
  {
    var messages = new List<BetaMessageParam> { new BetaUserMessageParam { Content = text } };
    if (echo.Count > 0) messages.Add(new BetaAssistantMessageParam { Content = echo.ToList() });
    // No temperature, top_p, top_k or tool_choice, and thinking left at the model's default
    // (adaptive): the model refuses the first four and cannot turn thinking off.
    return new MessageCreateParams
    {
      Model = Model,
      MaxTokens = MaxTokens,
      System = SystemPrompt,
      OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
      Tools = [new BetaToolUnion(new BetaWebSearchTool20260209 { MaxUses = Math.Max(1, _options.MaxSearches) })],
      Messages = messages,
      Fallbacks = new Default(),
      Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
    };
  }

  /// <summary>
  /// The paused content, sent back as it came so the provider resumes where it stopped. The raw
  /// JSON of each block is passed through (the shape the API returns is the shape it accepts
  /// back); a fallback marker is an audit block and is left out.
  /// </summary>
  internal static IEnumerable<BetaContentBlockParam> Echo(BetaMessage response) =>
    response.Content.Where(b => !b.TryPickFallback(out _)).Select(b => new BetaContentBlockParam(b.Json));

  /// <summary>The outcome and a short reason for ai_call: an exception type or status, never the provider's message.</summary>
  internal static (CloudLookupOutcome Outcome, string Error) Classify(Exception ex) => ex switch
  {
    AnthropicUnauthorizedException => (CloudLookupOutcome.KeyRejected, "401"),
    AnthropicForbiddenException => (CloudLookupOutcome.KeyRejected, "403"),
    AnthropicRateLimitException => (CloudLookupOutcome.RateLimited, "429"),
    OperationCanceledException => (CloudLookupOutcome.Unreachable, "timeout"),
    AnthropicIOException => (CloudLookupOutcome.Unreachable, "io"),
    _ => (CloudLookupOutcome.Unreachable, ex.GetType().Name),
  };

  /// <summary>What the responses of one lookup add up to: the answer text, sources, searches, the model and tokens.</summary>
  private sealed class Collected
  {
    private readonly List<LookupSourceDto> _cited = [];
    private readonly List<LookupSourceDto> _found = [];
    private int _searchesOk;
    private int _searchErrors;

    public StringBuilder Text { get; } = new();
    public string? Model { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public string? SearchError { get; private set; }

    /// <summary>A search was tried and none worked.</summary>
    public bool SearchesFailed => _searchErrors > 0 && _searchesOk == 0;

    public void Add(BetaMessage response)
    {
      InputTokens = (InputTokens ?? 0) + (int)Math.Min(int.MaxValue, response.Usage.InputTokens);
      OutputTokens = (OutputTokens ?? 0) + (int)Math.Min(int.MaxValue, response.Usage.OutputTokens);
      Model = ModelName(response.Model.Raw()) ?? Model;
      foreach (var block in response.Content)
      {
        if (block.TryPickText(out var text))
        {
          Text.Append(text.Text);
          foreach (var citation in text.Citations ?? [])
          {
            if (citation.TryPickCitationsWebSearchResultLocation(out var location)) Add(_cited, location.Title, location.Url);
          }
        }
        else if (block.TryPickWebSearchToolResult(out var result))
        {
          // The content is either a list of results or an error object, with HTTP 200 either way.
          if (result.Content.TryPickError(out var error))
          {
            _searchErrors++;
            SearchError = error.ErrorCode.Raw();
          }
          else if (result.Content.TryPickBetaWebSearchResultBlocks(out var results))
          {
            _searchesOk++;
            foreach (var r in results) Add(_found, r.Title, r.Url);
          }
        }
        else if (block.TryPickFallback(out var fallback))
        {
          // A classifier declined and the provider handed the question to another model.
          Model = ModelName(fallback.To.Model.Raw()) ?? Model;
        }
      }
    }

    /// <summary>The cited pages, or when nothing was cited, the pages the searches found. De-duplicated by address.</summary>
    public IReadOnlyList<LookupSourceDto> Sources() => (_cited.Count > 0 ? _cited : _found).Take(MaxSources).ToList();

    public CloudLookupResult Failed(CloudLookupOutcome outcome, string error) =>
      CloudLookupResult.Failed(outcome, error, Model, InputTokens, OutputTokens);

    private static void Add(List<LookupSourceDto> list, string? title, string? url)
    {
      if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return;
      if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return;
      var address = uri.ToString();
      if (list.Any(s => string.Equals(s.Url, address, StringComparison.OrdinalIgnoreCase))) return;
      list.Add(new LookupSourceDto(string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim(), address));
    }

    private static string? ModelName(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
  }
}
