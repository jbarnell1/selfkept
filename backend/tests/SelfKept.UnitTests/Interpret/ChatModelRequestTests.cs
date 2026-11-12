using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret.Ai;
using Xunit;

namespace SelfKept.UnitTests.Interpret;

/// <summary>Runs only when SELFKEPT_TEST_AI_URL and SELFKEPT_TEST_AI_MODEL name a model server you run; skips otherwise.</summary>
public sealed class RequiresModelFactAttribute : FactAttribute
{
  public const string UrlEnvVar = "SELFKEPT_TEST_AI_URL";
  public const string ModelEnvVar = "SELFKEPT_TEST_AI_MODEL";

  public RequiresModelFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlEnvVar))
      || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ModelEnvVar)))
    {
      Skip = $"{UrlEnvVar} and {ModelEnvVar} are not set; set them to a model server you run (for example http://localhost:11434/v1 and a thinking model) to run this test.";
    }
  }

  public static string Url => Environment.GetEnvironmentVariable(UrlEnvVar)!;

  public static string Model => Environment.GetEnvironmentVariable(ModelEnvVar)!;
}

/// <summary>The request the model adapter sends, and one opt-in call to a real thinking model.</summary>
public class ChatModelRequestTests
{
  private static readonly IReadOnlyList<ChatTurn> Turns =
  [
    new(ChatTurn.System, "Reply in one short sentence."),
    new(ChatTurn.User, "Say hello."),
  ];

  [Fact]
  public async Task BuildRequest_Defaults_AddsHeadroomAndNothingElse()
  {
    using var request = Model(new AiOptions()).BuildRequest(Turns, new ChatOptions(0.4, 400), stream: false);

    var body = await BodyAsync(request);
    body.GetProperty("max_tokens").GetInt32().Should().Be(1000);
    body.GetProperty("model").GetString().Should().Be("test-model");
    body.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    request.Headers.Contains("X-GPU-Priority").Should().BeFalse();
  }

  [Fact]
  public async Task BuildRequest_Body_HasAStatedLengthAndJsonType()
  {
    using var request = Model(new AiOptions()).BuildRequest(Turns, new ChatOptions(0.4, 400), stream: true);

    var bytes = await request.Content!.ReadAsByteArrayAsync();
    request.Content.Headers.ContentLength.Should().Be(bytes.Length);
    request.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    request.Headers.TransferEncodingChunked.Should().NotBe(true);
  }

  [Fact]
  public async Task BuildRequest_ReasoningEffortSet_SendsIt()
  {
    var options = new AiOptions { ReasoningEffort = " none ", HeadroomTokens = 0 };

    using var request = Model(options).BuildRequest(Turns, new ChatOptions(0.4, 400), stream: true);

    var body = await BodyAsync(request);
    body.GetProperty("reasoning_effort").GetString().Should().Be("none");
    body.GetProperty("max_tokens").GetInt32().Should().Be(400);
    body.GetProperty("stream").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task BuildRequest_NegativeHeadroom_IsIgnored()
  {
    var options = new AiOptions { HeadroomTokens = -50 };

    using var request = Model(options).BuildRequest(Turns, new ChatOptions(0.4, 80), stream: false);

    (await BodyAsync(request)).GetProperty("max_tokens").GetInt32().Should().Be(80);
  }

  [Fact]
  public void BuildRequest_InteractiveHeader_GoesOnlyWithCallsSomeoneWaitsOn()
  {
    var model = Model(new AiOptions { InteractiveHeader = "X-GPU-Priority: interactive" });

    using var waiting = model.BuildRequest(Turns, new ChatOptions(0.4, 400), stream: true);
    using var background = model.BuildRequest(Turns, new ChatOptions(0.2, 240, Background: true), stream: false);

    waiting.Headers.GetValues("X-GPU-Priority").Should().Equal("interactive");
    background.Headers.Contains("X-GPU-Priority").Should().BeFalse();
  }

  [Theory]
  [InlineData("X-GPU-Priority: interactive", "X-GPU-Priority", "interactive")]
  [InlineData("  X-Queue :a:b ", "X-Queue", "a:b")]
  public void SplitHeader_NameAndValue_AreSplitAtTheFirstColon(string header, string name, string value)
  {
    OpenAiCompatibleChatModel.SplitHeader(header).Should().Be((name, value));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("no colon")]
  [InlineData(": value only")]
  [InlineData("Name only:")]
  public void SplitHeader_MissingPart_IsNull(string? header)
  {
    OpenAiCompatibleChatModel.SplitHeader(header).Should().BeNull();
  }

  [Fact]
  public async Task BuildRequest_TextOnlyTurns_AreStillPlainStrings()
  {
    using var request = Model(new AiOptions()).BuildRequest(Turns, new ChatOptions(0.4, 400), stream: false);

    var messages = (await BodyAsync(request)).GetProperty("messages");
    messages.EnumerateArray().Should().OnlyContain(m => m.GetProperty("content").ValueKind == JsonValueKind.String);
    messages[1].GetProperty("content").GetString().Should().Be("Say hello.");
    (await BodyAsync(request)).TryGetProperty("response_format", out _).Should().BeFalse();
  }

  [Fact]
  public async Task BuildRequest_TurnWithAPhoto_IsSentAsATextPartAndADataUrl()
  {
    byte[] photo = [0xFF, 0xD8, 0xFF, 0x01, 0x02];
    IReadOnlyList<ChatTurn> turns =
    [
      new(ChatTurn.System, "Answer with JSON only."),
      new(ChatTurn.User, "Here is the photo.", [new ChatImage("image/jpeg", photo)]),
    ];

    using var request = Model(new AiOptions()).BuildRequest(turns, new ChatOptions(0.1, 200, JsonMode: true, Vision: true), stream: false);

    var messages = (await BodyAsync(request)).GetProperty("messages");
    messages[0].GetProperty("content").GetString().Should().Be("Answer with JSON only.");
    var parts = messages[1].GetProperty("content");
    parts.GetArrayLength().Should().Be(2);
    parts[0].GetProperty("type").GetString().Should().Be("text");
    parts[0].GetProperty("text").GetString().Should().Be("Here is the photo.");
    parts[1].GetProperty("type").GetString().Should().Be("image_url");
    parts[1].GetProperty("image_url").GetProperty("url").GetString().Should().Be("data:image/jpeg;base64," + Convert.ToBase64String(photo));
  }

  [Fact]
  public async Task BuildRequest_PhotoCall_StillHasAStatedLength()
  {
    IReadOnlyList<ChatTurn> turns = [new(ChatTurn.User, "Here is the photo.", [new ChatImage("image/png", new byte[200_000])])];

    using var request = Model(new AiOptions()).BuildRequest(turns, new ChatOptions(0.1, 200, JsonMode: true, Vision: true), stream: false);

    var bytes = await request.Content!.ReadAsByteArrayAsync();
    request.Content.Should().BeOfType<ByteArrayContent>();
    request.Content.Headers.ContentLength.Should().Be(bytes.Length);
    bytes.Length.Should().BeGreaterThan(200_000);
  }

  [Fact]
  public async Task BuildRequest_JsonMode_AsksForAJsonObject()
  {
    using var request = Model(new AiOptions()).BuildRequest(Turns, new ChatOptions(0.1, 200, JsonMode: true), stream: false);

    (await BodyAsync(request)).GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
  }

  [Fact]
  public async Task BuildRequest_VisionModelSet_GoesOnlyWithPhotoCalls()
  {
    var model = Model(new AiOptions { VisionModel = " qwen-vl " });

    using var photo = model.BuildRequest(Turns, new ChatOptions(0.1, 200, Vision: true), stream: false);
    using var text = model.BuildRequest(Turns, new ChatOptions(0.1, 200), stream: false);

    (await BodyAsync(photo)).GetProperty("model").GetString().Should().Be("qwen-vl");
    (await BodyAsync(text)).GetProperty("model").GetString().Should().Be("test-model");
  }

  [Fact]
  public async Task BuildRequest_VisionModelEmpty_PhotoCallsUseTheModel()
  {
    using var request = Model(new AiOptions()).BuildRequest(Turns, new ChatOptions(0.1, 200, Vision: true), stream: false);

    (await BodyAsync(request)).GetProperty("model").GetString().Should().Be("test-model");
  }

  [Fact]
  public void BuildRequest_PhotoCall_SendsTheInteractiveHeader()
  {
    using var request = Model(new AiOptions { InteractiveHeader = "X-GPU-Priority: interactive" })
      .BuildRequest(Turns, new ChatOptions(0.1, 200, JsonMode: true, Vision: true), stream: false);

    request.Headers.GetValues("X-GPU-Priority").Should().Equal("interactive");
  }

  [Fact]
  public void TimeoutFor_PhotoCall_UsesTheVisionTimeout()
  {
    var model = Model(new AiOptions { TimeoutSeconds = 20, VisionTimeoutSeconds = 90 });

    model.TimeoutFor(new ChatOptions(Vision: true)).Should().Be(TimeSpan.FromSeconds(90));
    model.TimeoutFor(new ChatOptions()).Should().Be(TimeSpan.FromSeconds(20));
  }

  [Theory]
  [InlineData(true, true, true)]
  [InlineData(false, true, false)]
  [InlineData(true, false, false)]
  public void VisionEnabled_NeedsVisionAndAUsableModel(bool vision, bool enabled, bool expected)
  {
    var options = new AiOptions { Vision = vision, Model = "test-model" };
    options.Enabled = enabled;

    new OpenAiCompatibleChatModel(new Factory(), Options.Create(options)).VisionEnabled.Should().Be(expected);
  }

  [Fact]
  public void ChatImage_ToString_NeverShowsTheBytes()
  {
    new ChatImage("image/jpeg", [1, 2, 3]).ToString().Should().Be("image/jpeg, 3 bytes");
  }

  [RequiresModelFact]
  public async Task CompleteAsync_ThinkingModelWithReasoningOff_AnswersInsideASmallLimit()
  {
    var options = new AiOptions
    {
      Enabled = true,
      BaseUrl = RequiresModelFactAttribute.Url,
      Model = RequiresModelFactAttribute.Model,
      TimeoutSeconds = 60,
      ReasoningEffort = "none",
      HeadroomTokens = 0,
      InteractiveHeader = "X-GPU-Priority: interactive",
    };
    var model = new OpenAiCompatibleChatModel(new Factory(), Options.Create(options));

    var whole = await model.CompleteAsync(Turns, new ChatOptions(0.2, 80));
    var pieces = new List<string>();
    await foreach (var piece in model.StreamAsync(Turns, new ChatOptions(0.2, 80))) pieces.Add(piece);

    whole.Content.Should().NotBeNullOrWhiteSpace();
    whole.CompletionTokens.Should().BeLessThan(80);
    string.Concat(pieces).Should().NotBeNullOrWhiteSpace();
    (await model.PingAsync()).Should().BeTrue();
  }

  private static OpenAiCompatibleChatModel Model(AiOptions options)
  {
    options.Enabled = true;
    options.Model = "test-model";
    return new OpenAiCompatibleChatModel(new Factory(), Options.Create(options));
  }

  private static async Task<JsonElement> BodyAsync(HttpRequestMessage request)
  {
    var text = await request.Content!.ReadAsStringAsync();
    return JsonDocument.Parse(text).RootElement.Clone();
  }

  private sealed class Factory : IHttpClientFactory
  {
    public HttpClient CreateClient(string name) => new();
  }
}
