using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Assistant.Lookup;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

/// <summary>
/// The provider client against canned provider responses on a fake HTTP handler, through the
/// real SDK: what the request holds, how stop reasons, search errors and HTTP failures map to
/// outcomes, the pause_turn continuation, the server-side fallback, and what is never logged.
/// No call leaves the machine.
/// </summary>
public class AnthropicLookupClientTests
{
  private const string Key = "sk-ant-test-0123456789abcdef";
  private const string Text = "Does magnesium glycinate help adults with restless legs sleep?";

  private sealed class Handler : HttpMessageHandler
  {
    public Queue<Func<HttpResponseMessage>> Responses { get; } = new();
    public List<(string Body, HttpRequestMessage Request)> Requests { get; } = [];
    public bool Hang { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Requests.Add((request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(ct), request));
      if (Hang) await Task.Delay(Timeout.Infinite, ct);
      return Responses.Dequeue()();
    }
  }

  private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
  {
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
  }

  private readonly Handler _handler = new();
  private readonly CapturingLogger<AnthropicLookupClient> _logger = new();

  private AnthropicLookupClient Client(Action<LookupOptions>? configure = null)
  {
    var options = new LookupOptions { Enabled = true, ApiKey = Key, Model = "" };
    configure?.Invoke(options);
    return new AnthropicLookupClient(Options.Create(options), new Factory(_handler), _logger);
  }

  private void Respond(string json, HttpStatusCode status = HttpStatusCode.OK) =>
    _handler.Responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

  private static string Message(string stopReason, string content, string model = "claude-opus-5-5") =>
    $$"""{"id":"msg_1","type":"message","role":"assistant","model":"{{model}}","stop_reason":"{{stopReason}}","stop_sequence":null,"usage":{"input_tokens":100,"output_tokens":40},"content":[{{content}}]}""";

  private const string Search =
    """{"type":"server_tool_use","id":"srvtoolu_1","name":"web_search","input":{"query":"magnesium sleep"}},""" +
    """{"type":"web_search_tool_result","tool_use_id":"srvtoolu_1","content":[{"type":"web_search_result","url":"https://journals.example.org/a","title":"Trial A","encrypted_content":"e1","page_age":null},{"type":"web_search_result","url":"https://journals.example.org/b","title":"Trial B","encrypted_content":"e2","page_age":null}]}""";

  private const string FailedSearch =
    """{"type":"server_tool_use","id":"srvtoolu_2","name":"web_search","input":{"query":"magnesium"}},""" +
    """{"type":"web_search_tool_result","tool_use_id":"srvtoolu_2","content":{"type":"web_search_tool_result_error","error_code":"unavailable"}}""";

  private const string CitedText =
    """{"type":"text","text":"Small trials suggest a modest benefit","citations":[{"type":"web_search_result_location","url":"https://journals.example.org/a","title":"Trial A","encrypted_index":"i1","cited_text":"c"}]},""" +
    """{"type":"text","text":", but the evidence is mixed.","citations":[{"type":"web_search_result_location","url":"https://journals.example.org/a","title":"Trial A","encrypted_index":"i2","cited_text":"c"},{"type":"web_search_result_location","url":"https://journals.example.org/b","title":"Trial B","encrypted_index":"i3","cited_text":"c"}]}""";

  [Fact]
  public async Task AskAsync_EndTurnWithCitations_ReturnsTheTextAndDeduplicatedSources()
  {
    Respond(Message("end_turn", Search + "," + CitedText));

    var result = await Client().AskAsync(Text);

    result.Outcome.Should().Be(CloudLookupOutcome.Answered);
    result.Text.Should().Be("Small trials suggest a modest benefit, but the evidence is mixed.");
    result.Sources.Select(s => (s.Title, s.Url)).Should().Equal(("Trial A", "https://journals.example.org/a"), ("Trial B", "https://journals.example.org/b"));
    result.Model.Should().Be("claude-opus-5-5");
    result.InputTokens.Should().Be(100);
    result.OutputTokens.Should().Be(40);
  }

  [Fact]
  public async Task AskAsync_Request_HoldsOnlyTheSystemPromptTheTextAndWebSearch()
  {
    Respond(Message("end_turn", CitedText));

    await Client(o => o.MaxSearches = 4).AskAsync(Text);

    var (body, request) = _handler.Requests.Single();
    request.RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
    request.Headers.GetValues("anthropic-beta").Should().Contain("server-side-fallback-2026-07-01");
    using var json = JsonDocument.Parse(body);
    var root = json.RootElement;
    root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["model", "max_tokens", "system", "output_config", "tools", "messages", "fallbacks"]);
    root.GetProperty("model").GetString().Should().Be(LookupOptions.DefaultModel, "an empty model falls back to the default");
    root.GetProperty("system").GetString().Should().Be(AnthropicLookupClient.SystemPrompt);
    root.GetProperty("output_config").GetProperty("effort").GetString().Should().Be("medium");
    root.GetProperty("fallbacks").GetString().Should().Be("default");
    var tool = root.GetProperty("tools").EnumerateArray().Single();
    tool.GetProperty("type").GetString().Should().Be("web_search_20260209");
    tool.GetProperty("max_uses").GetInt32().Should().Be(4);
    var message = root.GetProperty("messages").EnumerateArray().Single();
    message.GetProperty("role").GetString().Should().Be("user");
    message.GetProperty("content").GetString().Should().Be(Text);
  }

  [Fact]
  public async Task AskAsync_PauseTurn_SendsThePausedContentBackAndContinues()
  {
    Respond(Message("pause_turn", Search));
    Respond(Message("end_turn", CitedText));

    var result = await Client().AskAsync(Text);

    result.Outcome.Should().Be(CloudLookupOutcome.Answered);
    result.Text.Should().Be("Small trials suggest a modest benefit, but the evidence is mixed.");
    result.InputTokens.Should().Be(200);
    _handler.Requests.Should().HaveCount(2);
    using var second = JsonDocument.Parse(_handler.Requests[1].Body);
    var messages = second.RootElement.GetProperty("messages").EnumerateArray().ToList();
    messages.Should().HaveCount(2);
    messages[1].GetProperty("role").GetString().Should().Be("assistant");
    var echoed = messages[1].GetProperty("content").EnumerateArray().Select(b => b.GetProperty("type").GetString()).ToList();
    echoed.Should().Equal("server_tool_use", "web_search_tool_result");
    messages[1].GetProperty("content")[1].GetProperty("content")[0].GetProperty("encrypted_content").GetString().Should().Be("e1");
  }

  [Fact]
  public async Task AskAsync_PausedMoreThanThreeTimes_IsCutOff()
  {
    for (var i = 0; i < 4; i++) Respond(Message("pause_turn", Search));

    var result = await Client().AskAsync(Text);

    result.Outcome.Should().Be(CloudLookupOutcome.CutOff);
    _handler.Requests.Should().HaveCount(AnthropicLookupClient.MaxContinuations + 1);
  }

  [Fact]
  public async Task AskAsync_Refusal_IsRefusedWithNoText()
  {
    Respond(Message("refusal", """{"type":"text","text":"partial"}"""));

    var result = await Client().AskAsync(Text);

    result.Outcome.Should().Be(CloudLookupOutcome.Refused);
    result.Text.Should().BeEmpty();
  }

  [Fact]
  public async Task AskAsync_MaxTokens_IsCutOffNotAPartialAnswer()
  {
    Respond(Message("max_tokens", CitedText));

    var result = await Client().AskAsync(Text);

    result.Outcome.Should().Be(CloudLookupOutcome.CutOff);
    result.Text.Should().BeEmpty();
    result.Sources.Should().BeEmpty();
  }

  [Fact]
  public async Task AskAsync_EverySearchFailed_IsSearchFailed()
  {
    Respond(Message("end_turn", FailedSearch + """,{"type":"text","text":"I could not search."}"""));

    var result = await Client().AskAsync(Text);

    result.Outcome.Should().Be(CloudLookupOutcome.SearchFailed);
    result.Error.Should().Contain("unavailable");
  }

  [Fact]
  public async Task AskAsync_OneSearchFailedAnotherWorked_IsAnswered()
  {
    Respond(Message("end_turn", FailedSearch + "," + Search + "," + CitedText));

    (await Client().AskAsync(Text)).Outcome.Should().Be(CloudLookupOutcome.Answered);
  }

  [Fact]
  public async Task AskAsync_NothingCited_FallsBackToTheSearchResults()
  {
    Respond(Message("end_turn", Search + """,{"type":"text","text":"The evidence is weak."}"""));

    var result = await Client().AskAsync(Text);

    result.Sources.Select(s => s.Url).Should().Equal("https://journals.example.org/a", "https://journals.example.org/b");
  }

  [Fact]
  public async Task AskAsync_ServerSideFallback_ReportsTheModelThatAnswered()
  {
    Respond(Message("end_turn",
      """{"type":"fallback","from":{"model":"claude-opus-5-5"},"to":{"model":"claude-opus-4-8"}},""" + CitedText, model: "claude-opus-4-8"));

    var result = await Client().AskAsync(Text);

    result.Outcome.Should().Be(CloudLookupOutcome.Answered);
    result.Model.Should().Be("claude-opus-4-8");
  }

  [Theory]
  [InlineData(HttpStatusCode.Unauthorized, CloudLookupOutcome.KeyRejected)]
  [InlineData(HttpStatusCode.Forbidden, CloudLookupOutcome.KeyRejected)]
  [InlineData(HttpStatusCode.TooManyRequests, CloudLookupOutcome.RateLimited)]
  [InlineData(HttpStatusCode.InternalServerError, CloudLookupOutcome.Unreachable)]
  [InlineData(HttpStatusCode.BadRequest, CloudLookupOutcome.Unreachable)]
  public async Task AskAsync_HttpError_MapsToAnOutcome(HttpStatusCode status, CloudLookupOutcome expected)
  {
    Respond("""{"type":"error","error":{"type":"some_error","message":"nope"}}""", status);

    var result = await Client().AskAsync(Text);

    result.Outcome.Should().Be(expected);
    _handler.Requests.Should().ContainSingle("one attempt, no retries");
  }

  [Fact]
  public async Task AskAsync_NetworkFailure_IsUnreachable()
  {
    _handler.Responses.Enqueue(() => throw new HttpRequestException("no route to host"));

    (await Client().AskAsync(Text)).Outcome.Should().Be(CloudLookupOutcome.Unreachable);
  }

  [Fact]
  public async Task AskAsync_ProviderTooSlow_IsUnreachableAfterTheTimeout()
  {
    _handler.Hang = true;

    var result = await Client(o => o.TimeoutSeconds = 1).AskAsync(Text);

    result.Outcome.Should().Be(CloudLookupOutcome.Unreachable);
    _handler.Requests.Should().ContainSingle();
  }

  [Fact]
  public async Task AskAsync_NoKey_IsKeyRejectedWithoutACall()
  {
    var result = await Client(o => o.ApiKey = "").AskAsync(Text);

    result.Outcome.Should().Be(CloudLookupOutcome.KeyRejected);
    _handler.Requests.Should().BeEmpty();
  }

  [Fact]
  public async Task AskAsync_Failure_LogsNeitherTheTextNorTheKey()
  {
    Respond("""{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", HttpStatusCode.Unauthorized);

    await Client().AskAsync(Text);

    _logger.Lines.Should().ContainSingle();
    _logger.Lines[0].Should().NotContain(Key).And.NotContain("restless").And.NotContain("invalid x-api-key");
  }

  [Fact]
  public void Options_EmptyModel_FallsBackToTheDefault_AndNoKeyIsNotUsable()
  {
    new LookupOptions { Model = " " }.EffectiveModel.Should().Be("claude-opus-5-5");
    new LookupOptions { Enabled = true, ApiKey = "" }.IsUsable.Should().BeFalse();
    new LookupOptions { Enabled = false, ApiKey = "k" }.IsUsable.Should().BeFalse();
    new LookupOptions { Enabled = true, ApiKey = "k" }.IsUsable.Should().BeTrue();
  }
}
