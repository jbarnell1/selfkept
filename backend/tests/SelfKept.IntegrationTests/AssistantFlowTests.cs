using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Assistant;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// The in-app assistant over HTTP with a fake local model: capture and Undo, the streamed reply
/// and its guard, the model-off template, red flags, the evening check-in, nudges, memory, the
/// brief and the playbook. Another household member reaches none of it.
/// </summary>
public class AssistantFlowTests
{
  private sealed class FakeModel : IChatModel
  {
    public bool Enabled { get; set; } = true;
    public string ModelName => "fake-local";
    public string[] Pieces { get; set; } = ["Thanks for telling me. ", "Rest is a fair call today."];
    public int Calls { get; private set; }

    public Task<ChatCompletion> CompleteAsync(string system, string user, ChatOptions options, CancellationToken ct = default)
    {
      Calls++;
      return Task.FromResult(new ChatCompletion(string.Concat(Pieces), 10, 10));
    }

    public Task<ChatCompletion> CompleteAsync(IReadOnlyList<ChatTurn> turns, ChatOptions options, CancellationToken ct = default) =>
      CompleteAsync(string.Empty, string.Empty, options, ct);

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> turns, ChatOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
      Calls++;
      foreach (var piece in Pieces)
      {
        await Task.Yield();
        yield return piece;
      }
    }

    public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(Enabled);
  }

  private sealed record SseEvent(string Name, JsonElement Data);

  private static (ApiFactory Root, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> App, FakeModel Model) Create()
  {
    var model = new FakeModel();
    var root = new ApiFactory();
    var app = root.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
      s.RemoveAll<IChatModel>();
      s.AddSingleton<IChatModel>(model);
    }));
    return (root, app, model);
  }

  private static async Task<(HttpClient Ada, HttpClient Bob)> HouseholdAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
  {
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var ada = app.CreateClient();
    await RegisterAsync(ada, $"ada-{suffix}@example.com", "Ada");
    var invite = await (await ada.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    var bob = app.CreateClient();
    await RegisterAsync(bob, $"bob-{suffix}@example.com", "Bob", invite.GetProperty("code").GetString());
    return (ada, bob);
  }

  private static async Task RegisterAsync(HttpClient client, string email, string name, string? inviteCode = null)
  {
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = name, inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    client.UseBearer((await response.ReadJsonAsync()).GetProperty("accessToken").GetString()!);
  }

  private static async Task<Guid> NewConversationAsync(HttpClient client)
  {
    var response = await client.PostJsonAsync("/api/assistant/conversations", new { });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
  }

  private static async Task<List<SseEvent>> SendAsync(HttpClient client, Guid conversationId, string text)
  {
    var response = await client.PostJsonAsync($"/api/assistant/conversations/{conversationId}/messages", new { text });
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

  [Fact]
  public async Task Message_CapturesStreamsAndUndo_RemovesTheCaptures()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);
    var conversation = await NewConversationAsync(ada);

    var events = await SendAsync(ada, conversation, "Had oats and berries for breakfast. Throat's a bit scratchy and I'm tired.");

    events.Select(e => e.Name).Should().StartWith("capture").And.EndWith("done");
    var capture = events[0].Data;
    capture.GetProperty("entries").GetArrayLength().Should().Be(2);
    var userMessageId = capture.GetProperty("messageId").GetGuid();
    string.Concat(events.Where(e => e.Name == "delta").Select(e => e.Data.GetProperty("text").GetString()))
      .Should().Be("Thanks for telling me. Rest is a fair call today.");
    var done = events[^1].Data;
    done.GetProperty("origin").GetString().Should().Be("model");
    done.GetProperty("userMessageId").GetGuid().Should().Be(userMessageId);
    model.Calls.Should().Be(1);

    var journal = await (await ada.GetAsync("/api/journal")).ReadJsonAsync();
    journal.EnumerateArray().Where(j => j.GetProperty("source").GetString() == "assistant").Should().HaveCount(2);

    var thread = await (await ada.GetAsync($"/api/assistant/conversations/{conversation}")).ReadJsonAsync();
    var messages = thread.GetProperty("messages");
    messages.GetArrayLength().Should().Be(2);
    messages[0].GetProperty("content").GetString().Should().StartWith("Had oats");
    messages[0].GetProperty("captures").GetArrayLength().Should().Be(2);
    thread.GetProperty("title").GetString().Should().StartWith("Had oats");

    var undo = await ada.DeleteAsync($"/api/assistant/messages/{userMessageId}/captures");
    undo.StatusCode.Should().Be(HttpStatusCode.OK);
    (await undo.ReadJsonAsync()).GetProperty("removedEntries").GetInt32().Should().Be(2);
    var after = await (await ada.GetAsync("/api/journal")).ReadJsonAsync();
    after.GetArrayLength().Should().Be(0);
    var undone = await (await ada.GetAsync($"/api/assistant/conversations/{conversation}")).ReadJsonAsync();
    undone.GetProperty("messages")[0].GetProperty("capturesUndone").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task OtherMember_CannotReachConversationsMessagesMemoryOrNudges()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, bob) = await HouseholdAsync(app);
    var conversation = await NewConversationAsync(ada);
    var events = await SendAsync(ada, conversation, "I'm vegetarian. Had a salad for lunch.");
    var messageId = events[0].Data.GetProperty("messageId").GetGuid();
    (await ada.PutJsonAsync("/api/assistant/nudges", new { enabled = true, ntfyUrl = "https://ntfy.home.example/ada-topic" })).EnsureSuccessStatusCode();

    (await bob.GetAsync($"/api/assistant/conversations/{conversation}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.DeleteAsync($"/api/assistant/conversations/{conversation}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.PostJsonAsync($"/api/assistant/conversations/{conversation}/messages", new { text = "hello" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.DeleteAsync($"/api/assistant/messages/{messageId}/captures")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await (await bob.GetAsync("/api/assistant/conversations")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
    (await (await bob.GetAsync("/api/assistant/memory")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
    var bobNudges = await (await bob.GetAsync("/api/assistant/nudges")).ReadJsonAsync();
    bobNudges.GetProperty("urlSet").GetBoolean().Should().BeFalse();

    var adaMemory = await (await ada.GetAsync("/api/assistant/memory")).ReadJsonAsync();
    adaMemory.EnumerateArray().Should().Contain(m => m.GetProperty("key").GetString() == "diet");
    (await (await ada.GetAsync($"/api/assistant/conversations/{conversation}")).ReadJsonAsync()).GetProperty("messages").GetArrayLength().Should().Be(2);
  }

  [Fact]
  public async Task Reply_WithAnInventedNumber_IsReplacedByTheGuard()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);
    var conversation = await NewConversationAsync(ada);
    model.Pieces = ["Your record looks steady. ", "Aim for 12000 steps tomorrow."];

    var events = await SendAsync(ada, conversation, "What should I do tomorrow?");

    events.Select(e => e.Name).Should().Equal("capture", "delta", "replace", "done");
    events[1].Data.GetProperty("text").GetString().Should().Be("Your record looks steady.");
    events[2].Data.GetProperty("reason").GetString().Should().Be("guard");
    var done = events[3].Data;
    done.GetProperty("origin").GetString().Should().Be("guard");
    done.GetProperty("text").GetString().Should().NotContain("12000");
  }

  [Fact]
  public async Task Reply_ModelOff_IsATemplateAndCapturesStillHappen()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);
    var conversation = await NewConversationAsync(ada);
    model.Enabled = false;

    var events = await SendAsync(ada, conversation, "Had two coffees this morning.");

    events[0].Data.GetProperty("entries").GetArrayLength().Should().Be(1);
    var done = events[^1].Data;
    done.GetProperty("origin").GetString().Should().Be("template");
    done.GetProperty("text").GetString().Should().StartWith("Saved to today's log.");
    model.Calls.Should().Be(0);
    var status = await (await ada.GetAsync("/api/assistant/status")).ReadJsonAsync();
    status.GetProperty("enabled").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task RedFlag_IsAnsweredByCode_WithoutTheModel()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);
    var conversation = await NewConversationAsync(ada);

    var events = await SendAsync(ada, conversation, "I have crushing chest pain right now");

    var done = events[^1].Data;
    done.GetProperty("origin").GetString().Should().Be("guard");
    done.GetProperty("text").GetString().Should().Be(ReplyGuard.EmergencyReply);
    model.Calls.Should().Be(0);
  }

  [Fact]
  public async Task Message_Empty_IsAProblem()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);
    var conversation = await NewConversationAsync(ada);

    var response = await ada.PostJsonAsync($"/api/assistant/conversations/{conversation}/messages", new { text = "   " });

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
  }

  [Fact]
  public async Task Checkin_AsksWhatIsMissing_HarvestsAndLeaving_KeepsIt()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);
    model.Enabled = false;
    // Food is already on today's log, so the check-in skips eating.
    (await ada.PostJsonAsync("/api/journal", new { kind = "food", text = "Porridge" })).EnsureSuccessStatusCode();

    var start = await (await ada.PostJsonAsync("/api/assistant/checkin", new { })).ReadJsonAsync();
    start.GetProperty("topic").GetString().Should().Be("training");
    start.GetProperty("status").GetString().Should().Be("partial");
    start.GetProperty("remaining").EnumerateArray().Select(t => t.GetString()).Should().Equal("training", "drinks", "symptoms", "mood");

    var training = await (await ada.PostJsonAsync("/api/assistant/checkin", new { answer = "Yes, a tough session, squats and deadlifts" })).ReadJsonAsync();
    training.GetProperty("encouragement").GetString().Should().Be(AssistantTemplates.EncouragementFor("training", CheckinTones.Hard));
    training.GetProperty("topic").GetString().Should().Be("drinks");
    training.GetProperty("captured").GetArrayLength().Should().Be(1);

    var drinks = await (await ada.PostJsonAsync("/api/assistant/checkin", new { answer = "None" })).ReadJsonAsync();
    drinks.GetProperty("captured")[0].GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Contain(["no_alcohol", "no_caffeine"]);

    var left = await (await ada.PostJsonAsync("/api/assistant/checkin", new { leave = true })).ReadJsonAsync();
    left.GetProperty("status").GetString().Should().Be("partial");
    left.GetProperty("reply").GetString().Should().Be(AssistantTemplates.Left);

    var today = await (await ada.GetAsync("/api/assistant/checkin/today")).ReadJsonAsync();
    today.GetProperty("status").GetString().Should().Be("partial");
    today.GetProperty("done").EnumerateArray().Select(t => t.GetString()).Should().Equal("training", "drinks");
    today.GetProperty("missing").EnumerateArray().Select(t => t.GetString()).Should().Equal("symptoms", "mood");

    var journal = await (await ada.GetAsync("/api/journal")).ReadJsonAsync();
    journal.EnumerateArray().Count(j => j.GetProperty("source").GetString() == "checkin").Should().Be(2);

    // Back again: it resumes with symptoms, then mood goes into the mood check-in.
    var resumed = await (await ada.PostJsonAsync("/api/assistant/checkin", new { })).ReadJsonAsync();
    resumed.GetProperty("topic").GetString().Should().Be("symptoms");
    await ada.PostJsonAsync("/api/assistant/checkin", new { answer = "All good" });
    var mood = await (await ada.PostJsonAsync("/api/assistant/checkin", new { answer = "pretty good" })).ReadJsonAsync();
    mood.GetProperty("mood").GetInt32().Should().Be(5);
    mood.GetProperty("status").GetString().Should().Be("done");
    var checkins = await (await ada.GetAsync("/api/checkins")).ReadJsonAsync();
    checkins.GetArrayLength().Should().Be(1);
  }

  [Fact]
  public async Task Nudges_SaveMasksTheAddress_AndRefusesABadOne()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, _) = await HouseholdAsync(app);

    var saved = await ada.PutJsonAsync("/api/assistant/nudges", new { enabled = true, ntfyUrl = "https://ntfy.home.example/ada-secret", ntfyToken = "tk_abc", eveningTime = "21:15" });
    saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    var body = await saved.Content.ReadAsStringAsync();
    body.Should().NotContain("ada-secret").And.NotContain("tk_abc");
    var dto = JsonDocument.Parse(body).RootElement;
    dto.GetProperty("ntfyUrlMasked").GetString().Should().Be("https://ntfy.home.example/***");
    dto.GetProperty("tokenSet").GetBoolean().Should().BeTrue();
    dto.GetProperty("eveningTime").GetString().Should().Be("21:15");

    (await ada.PutJsonAsync("/api/assistant/nudges", new { enabled = true, ntfyUrl = "ftp://nope/topic" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PutJsonAsync("/api/assistant/nudges", new { enabled = true, eveningTime = "9pm" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task BriefPlaybookAndModelCheck_Answer()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, bob) = await HouseholdAsync(app);
    model.Enabled = false;
    var conversation = await NewConversationAsync(ada);
    await SendAsync(ada, conversation, "Woke up with a sore throat.");

    var brief = await ada.GetAsync("/api/assistant/brief");
    brief.StatusCode.Should().Be(HttpStatusCode.OK);
    (await brief.ReadJsonAsync()).GetProperty("text").GetString().Should().StartWith("Good morning, Ada.");

    var playbook = await (await ada.GetAsync("/api/assistant/playbook")).ReadJsonAsync();
    playbook.EnumerateArray().Select(p => p.GetProperty("key").GetString()).Should().Contain(["early_cold_rest", "early_cold_zinc"]);

    model.Enabled = true;
    model.Pieces = ["Your resting heart rate was 62 bpm last night, above your usual range."];
    var check = await ada.PostAsync("/api/assistant/model-check", null);
    check.StatusCode.Should().Be(HttpStatusCode.OK, await check.Content.ReadAsStringAsync());
    var result = await check.ReadJsonAsync();
    result.GetProperty("total").GetInt32().Should().Be(ModelCheckService.Cases.Count + 1);
    result.GetProperty("cases")[0].GetProperty("passed").GetBoolean().Should().BeTrue();
    (await bob.PostAsync("/api/assistant/model-check", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }
}
