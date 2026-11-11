using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// The cloud lookup over HTTP with a fake provider client: the server switch (404 everywhere and
/// no feature flag when off), the person switch, the offer under a chat reply, the draft, the
/// streamed send, sources on the stored answer, the "Sent to the cloud" list and delete, and a
/// household member with every grant who still reaches none of it.
/// </summary>
public class LookupFlowTests
{
  private sealed class FakeCloud : ICloudLookupClient
  {
    public bool IsUsable { get; set; } = true;
    public string Provider => "Anthropic";
    public string Model => "claude-opus-5-5";
    public List<string> Asked { get; } = [];

    public Task<CloudLookupResult> AskAsync(string text, CancellationToken ct = default)
    {
      Asked.Add(text);
      return Task.FromResult(new CloudLookupResult(CloudLookupOutcome.Answered,
        "Small trials suggest magnesium may modestly help sleep; the evidence is mixed.",
        [new LookupSourceDto("Magnesium and sleep", "https://journals.example.org/magnesium")], "claude-opus-5-5", 900, 200));
    }
  }

  private sealed class OffModel : IChatModel
  {
    public bool Enabled => false;
    public string ModelName => string.Empty;
    public Task<ChatCompletion> CompleteAsync(string system, string user, ChatOptions options, CancellationToken ct = default) =>
      throw new InvalidOperationException("The model is off.");

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> turns, ChatOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
      await Task.CompletedTask;
      yield break;
    }
  }

  private sealed record SseEvent(string Name, JsonElement Data);

  private static (ApiFactory Root, WebApplicationFactory<Program> App, FakeCloud Cloud) Create(bool usable = true)
  {
    var cloud = new FakeCloud { IsUsable = usable };
    var root = new ApiFactory();
    var app = root.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
      s.RemoveAll<ICloudLookupClient>();
      s.AddSingleton<ICloudLookupClient>(cloud);
      s.RemoveAll<IChatModel>();
      s.AddSingleton<IChatModel>(new OffModel());
    }));
    return (root, app, cloud);
  }

  private static async Task<(HttpClient Ada, HttpClient Bob)> HouseholdAsync(WebApplicationFactory<Program> app, bool shareEverything = false)
  {
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var ada = app.CreateClient();
    await RegisterAsync(ada, $"ada-{suffix}@example.com", "Ada");
    var invite = await (await ada.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    var bob = app.CreateClient();
    var bobPerson = await RegisterAsync(bob, $"bob-{suffix}@example.com", "Bob", invite.GetProperty("code").GetString());
    if (shareEverything)
    {
      // Every category Ada can share: none of them opens her lookups.
      var shared = await ada.PutJsonAsync($"/api/household/shares/{bobPerson}",
        new { categories = new[] { "activity", "sleep", "vitals", "body", "labs", "cycle", "checkins", "insights" } });
      shared.StatusCode.Should().Be(HttpStatusCode.OK, await shared.Content.ReadAsStringAsync());
    }
    return (ada, bob);
  }

  private static async Task<Guid> RegisterAsync(HttpClient client, string email, string name, string? inviteCode = null)
  {
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = name, inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var auth = await response.ReadJsonAsync();
    client.UseBearer(auth.GetProperty("accessToken").GetString()!);
    return auth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid();
  }

  private static async Task<List<SseEvent>> EventsAsync(HttpResponseMessage response)
  {
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    var body = await response.Content.ReadAsStringAsync();
    var events = new List<SseEvent>();
    foreach (var frame in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
    {
      var lines = frame.Split('\n');
      var name = lines.Single(l => l.StartsWith("event: ", StringComparison.Ordinal))["event: ".Length..];
      var data = lines.Single(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
      events.Add(new SseEvent(name, JsonDocument.Parse(data).RootElement.Clone()));
    }
    return events;
  }

  /// <summary>A conversation with one question; returns the conversation, the question's id and the chat's done event.</summary>
  private static async Task<(Guid Conversation, Guid Message, JsonElement Done)> AskAsync(HttpClient client, string text)
  {
    var conversation = (await (await client.PostJsonAsync("/api/assistant/conversations", new { })).ReadJsonAsync()).GetProperty("id").GetGuid();
    var events = await EventsAsync(await client.PostJsonAsync($"/api/assistant/conversations/{conversation}/messages", new { text }));
    return (conversation, events[0].Data.GetProperty("messageId").GetGuid(), events[^1].Data);
  }

  [Fact]
  public async Task Routes_ServerSwitchOff_AreNotFound_AndTheFeatureIsNotListed()
  {
    var (root, app, _) = Create(usable: false);
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);
    var id = Guid.NewGuid();

    (await ada.GetAsync("/api/assistant/lookup")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ada.PutJsonAsync("/api/assistant/lookup", new { enabled = true, acknowledged = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ada.PostJsonAsync("/api/assistant/lookups/draft", new { messageId = id, mode = "anonymized" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ada.PostJsonAsync("/api/assistant/lookups", new { messageId = id, mode = "anonymized", text = "Is zinc good?", acknowledged = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ada.GetAsync("/api/assistant/lookups")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ada.DeleteAsync($"/api/assistant/lookups/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    var version = await (await ada.GetAsync("/api/system/version")).ReadJsonAsync();
    version.GetProperty("features").EnumerateArray().Select(f => f.GetString()).Should().NotContain("assistant.lookup");
    var (_, _, done) = await AskAsync(ada, "Does magnesium help sleep?");
    done.GetProperty("lookup").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task Version_LookupEnabledWithAKey_ListsTheFeature()
  {
    using var root = new ApiFactory();
    var app = root.WithWebHostBuilder(b =>
    {
      b.UseSetting("Lookup:Enabled", "true");
      b.UseSetting("Lookup:ApiKey", "sk-ant-not-a-real-key");
    });

    var version = await (await app.CreateClient().GetAsync("/api/system/version")).ReadJsonAsync();

    version.GetProperty("features").EnumerateArray().Select(f => f.GetString()).Should().Contain("assistant.lookup");
  }

  [Fact]
  public async Task Version_LookupEnabledWithoutAKey_DoesNotListTheFeature()
  {
    using var root = new ApiFactory();
    var app = root.WithWebHostBuilder(b => b.UseSetting("Lookup:Enabled", "true"));

    var version = await (await app.CreateClient().GetAsync("/api/system/version")).ReadJsonAsync();

    version.GetProperty("features").EnumerateArray().Select(f => f.GetString()).Should().NotContain("assistant.lookup");
  }

  [Fact]
  public async Task Lookup_TurnOnDraftSendListAndDelete_Works()
  {
    var (root, app, cloud) = Create();
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);

    var status = await (await ada.GetAsync("/api/assistant/lookup")).ReadJsonAsync();
    status.GetProperty("available").GetBoolean().Should().BeTrue();
    status.GetProperty("enabled").GetBoolean().Should().BeFalse();
    status.GetProperty("provider").GetString().Should().Be("Anthropic");
    status.GetProperty("model").GetString().Should().Be("claude-opus-5-5");
    status.GetProperty("localModel").GetBoolean().Should().BeFalse();

    // A question before the person switch is on gets no offer, and drafting is a conflict.
    var (conversation, question, before) = await AskAsync(ada, "Does magnesium help Ada sleep? I weigh 187.4 lbs.");
    before.GetProperty("lookup").ValueKind.Should().Be(JsonValueKind.Null);
    (await ada.PostJsonAsync("/api/assistant/lookups/draft", new { messageId = question, mode = "anonymized" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

    var refused = await ada.PutJsonAsync("/api/assistant/lookup", new { enabled = true, acknowledged = false });
    refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    refused.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    var on = await (await ada.PutJsonAsync("/api/assistant/lookup", new { enabled = true, acknowledged = true })).ReadJsonAsync();
    on.GetProperty("enabled").GetBoolean().Should().BeTrue();

    var (_, _, offered) = await AskAsync(ada, "Does magnesium help with sleep?");
    offered.GetProperty("lookup").GetProperty("offered").GetBoolean().Should().BeTrue();
    offered.GetProperty("lookup").GetProperty("reason").GetString().Should().Be("model_off");
    var (_, _, statement) = await AskAsync(ada, "Had oats for breakfast.");
    statement.GetProperty("lookup").ValueKind.Should().Be(JsonValueKind.Null);

    var draft = await (await ada.PostJsonAsync("/api/assistant/lookups/draft", new { messageId = question, mode = "anonymized" })).ReadJsonAsync();
    draft.GetProperty("text").GetString().Should().Be("Does magnesium help someone sleep? I weigh 190 lbs.");
    draft.GetProperty("mode").GetString().Should().Be("anonymized");
    draft.GetProperty("reworded").GetBoolean().Should().BeFalse();
    draft.GetProperty("changes").EnumerateArray().Select(c => (c.GetProperty("kind").GetString(), c.GetProperty("count").GetInt32()))
      .Should().Equal(("name", 1), ("number", 1));
    cloud.Asked.Should().BeEmpty();

    var noAck = await ada.PostJsonAsync("/api/assistant/lookups", new { messageId = question, mode = "anonymized", text = "Does magnesium help sleep?", acknowledged = false });
    noAck.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var named = await ada.PostJsonAsync("/api/assistant/lookups", new { messageId = question, mode = "anonymized", text = "Does magnesium help Ada sleep?", acknowledged = true });
    named.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var findings = (await named.ReadJsonAsync()).GetProperty("findings");
    findings[0].GetProperty("kind").GetString().Should().Be("name");
    findings[0].GetProperty("count").GetInt32().Should().Be(1);
    cloud.Asked.Should().BeEmpty();

    var events = await EventsAsync(await ada.PostJsonAsync("/api/assistant/lookups",
      new { messageId = question, mode = "anonymized", text = "Does magnesium help adults sleep?", acknowledged = true }));
    events.Select(e => e.Name).Should().Equal("sent", "done");
    var lookupId = events[0].Data.GetProperty("lookupId").GetGuid();
    var done = events[1].Data;
    done.GetProperty("lookupId").GetGuid().Should().Be(lookupId);
    done.GetProperty("message").GetProperty("origin").GetString().Should().Be("cloud");
    done.GetProperty("message").GetProperty("lookupId").GetGuid().Should().Be(lookupId);
    done.GetProperty("message").GetProperty("sources")[0].GetProperty("url").GetString().Should().Be("https://journals.example.org/magnesium");
    done.GetProperty("applied").ValueKind.Should().Be(JsonValueKind.Null);
    cloud.Asked.Should().Equal("Does magnesium help adults sleep?");

    var thread = await (await ada.GetAsync($"/api/assistant/conversations/{conversation}")).ReadJsonAsync();
    var messages = thread.GetProperty("messages").EnumerateArray().ToList();
    var answer = messages.Single(m => m.GetProperty("origin").GetString() == "cloud");
    answer.GetProperty("lookupId").GetGuid().Should().Be(lookupId);
    answer.GetProperty("sources")[0].GetProperty("title").GetString().Should().Be("Magnesium and sleep");
    messages.Where(m => m.GetProperty("origin").GetString() != "cloud").Should().OnlyContain(m =>
      m.GetProperty("sources").GetArrayLength() == 0 && m.GetProperty("lookupId").ValueKind == JsonValueKind.Null);

    var list = await (await ada.GetAsync("/api/assistant/lookups")).ReadJsonAsync();
    list.GetArrayLength().Should().Be(1);
    list[0].GetProperty("id").GetGuid().Should().Be(lookupId);
    list[0].GetProperty("conversationId").GetGuid().Should().Be(conversation);
    list[0].GetProperty("status").GetString().Should().Be("answered");
    list[0].GetProperty("text").GetString().Should().Be("Does magnesium help adults sleep?");
    list[0].GetProperty("sentAt").GetDateTime().Should().BeAfter(DateTime.UtcNow.AddMinutes(-5));

    (await ada.DeleteAsync($"/api/assistant/lookups/{lookupId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await (await ada.GetAsync("/api/assistant/lookups")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
    var after = await (await ada.GetAsync($"/api/assistant/conversations/{conversation}")).ReadJsonAsync();
    var kept = after.GetProperty("messages").EnumerateArray().Single(m => m.GetProperty("origin").GetString() == "cloud");
    kept.GetProperty("lookupId").ValueKind.Should().Be(JsonValueKind.Null);
    kept.GetProperty("sources").GetArrayLength().Should().Be(0);
    (await ada.DeleteAsync($"/api/assistant/lookups/{lookupId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task OtherMember_WithEveryGrant_ReachesNoneOfIt()
  {
    var (root, app, cloud) = Create();
    using var host = root;
    var (ada, bob) = await HouseholdAsync(app, shareEverything: true);
    (await ada.PutJsonAsync("/api/assistant/lookup", new { enabled = true, acknowledged = true })).EnsureSuccessStatusCode();
    var (_, question, _) = await AskAsync(ada, "Does zinc shorten colds?");
    var events = await EventsAsync(await ada.PostJsonAsync("/api/assistant/lookups",
      new { messageId = question, mode = "anonymized", text = "Does zinc shorten colds?", acknowledged = true }));
    var lookupId = events[0].Data.GetProperty("lookupId").GetGuid();

    // Ada's switch is not Bob's; with his own on, her message and her lookup are still not found.
    (await (await bob.GetAsync("/api/assistant/lookup")).ReadJsonAsync()).GetProperty("enabled").GetBoolean().Should().BeFalse();
    (await bob.PostJsonAsync("/api/assistant/lookups/draft", new { messageId = question, mode = "anonymized" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await bob.PutJsonAsync("/api/assistant/lookup", new { enabled = true, acknowledged = true })).EnsureSuccessStatusCode();
    (await bob.PostJsonAsync("/api/assistant/lookups/draft", new { messageId = question, mode = "anonymized" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.PostJsonAsync("/api/assistant/lookups", new { messageId = question, mode = "anonymized", text = "Does zinc shorten colds?", acknowledged = true }))
      .StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await (await bob.GetAsync("/api/assistant/lookups")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
    (await bob.DeleteAsync($"/api/assistant/lookups/{lookupId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await (await ada.GetAsync("/api/assistant/lookups")).ReadJsonAsync()).GetArrayLength().Should().Be(1);
    cloud.Asked.Should().ContainSingle();
  }

  [Fact]
  public async Task Send_RedFlagMessage_IsUnprocessable()
  {
    var (root, app, cloud) = Create();
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);
    (await ada.PutJsonAsync("/api/assistant/lookup", new { enabled = true, acknowledged = true })).EnsureSuccessStatusCode();
    var (_, question, done) = await AskAsync(ada, "I have crushing chest pain, what should I take?");

    done.GetProperty("lookup").ValueKind.Should().Be(JsonValueKind.Null, "a red flag is never offered a lookup");
    (await ada.PostJsonAsync("/api/assistant/lookups/draft", new { messageId = question, mode = "anonymized" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    (await ada.PostJsonAsync("/api/assistant/lookups", new { messageId = question, mode = "anonymized", text = "What helps with chest discomfort?", acknowledged = true }))
      .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    cloud.Asked.Should().BeEmpty();
  }
}
