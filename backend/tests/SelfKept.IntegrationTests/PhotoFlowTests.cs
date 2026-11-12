using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SelfKept.Core.Interfaces;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Photos read by the home model over HTTP (docs/photo-design.md), with a fake vision model: the
/// status flag, the multipart route and its errors, a chat message sent with a photo's
/// description, and day log numbers with their samples, hidden from a member without the grant.
/// </summary>
public class PhotoFlowTests
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46];

  private sealed class FakeVisionModel : IChatModel
  {
    public bool Enabled { get; set; } = true;
    public bool VisionEnabled { get; set; } = true;
    public string ModelName => "fake-vision";
    public Queue<string> Replies { get; } = new();
    public bool Down { get; set; }
    public List<IReadOnlyList<ChatTurn>> Sent { get; } = [];

    public Task<ChatCompletion> CompleteAsync(string system, string user, ChatOptions options, CancellationToken ct = default) =>
      CompleteAsync([new ChatTurn(ChatTurn.System, system), new ChatTurn(ChatTurn.User, user)], options, ct);

    public Task<ChatCompletion> CompleteAsync(IReadOnlyList<ChatTurn> turns, ChatOptions options, CancellationToken ct = default)
    {
      Sent.Add(turns);
      if (Down) throw new HttpRequestException("connection refused");
      return Task.FromResult(new ChatCompletion(Replies.Count > 0 ? Replies.Dequeue() : string.Empty, 10, 10));
    }

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> turns, ChatOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
      Sent.Add(turns);
      await Task.Yield();
      if (Down) throw new HttpRequestException("connection refused");
      yield return StreamReply;
    }

    public string StreamReply { get; set; } = "That looks like a plate of food.";

    public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(Enabled);
  }

  private static (ApiFactory Root, WebApplicationFactory<Program> App, FakeVisionModel Model) Create()
  {
    var model = new FakeVisionModel();
    var root = new ApiFactory();
    var app = root.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
      s.RemoveAll<IChatModel>();
      s.AddSingleton<IChatModel>(model);
    }));
    return (root, app, model);
  }

  private static async Task<(HttpClient Ada, Guid AdaPerson, HttpClient Bob, Guid BobPerson)> HouseholdAsync(WebApplicationFactory<Program> app)
  {
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var ada = app.CreateClient();
    var adaAuth = await RegisterAsync(ada, $"ada-{suffix}@example.com", "Ada");
    var invite = await (await ada.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    var bob = app.CreateClient();
    var bobAuth = await RegisterAsync(bob, $"bob-{suffix}@example.com", "Bob", invite.GetProperty("code").GetString());
    return (ada, PersonId(adaAuth), bob, PersonId(bobAuth));
  }

  private static async Task<JsonElement> RegisterAsync(HttpClient client, string email, string name, string? inviteCode = null)
  {
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = name, inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var auth = await response.ReadJsonAsync();
    client.UseBearer(auth.GetProperty("accessToken").GetString()!);
    return auth;
  }

  private static Guid PersonId(JsonElement auth) => auth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid();

  private static Task<HttpResponseMessage> PostPhotoAsync(HttpClient client, byte[]? image, string? kind = null, string? note = null)
  {
    var form = new MultipartFormDataContent();
    if (image != null)
    {
      var file = new ByteArrayContent(image);
      file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
      form.Add(file, "image", "photo.jpg");
    }
    if (kind != null) form.Add(new StringContent(kind), "kind");
    if (note != null) form.Add(new StringContent(note), "note");
    return client.PostAsync("/api/assistant/photos", form);
  }

  private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string detail)
  {
    response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
    response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    (await response.ReadJsonAsync()).GetProperty("detail").GetString().Should().Be(detail);
  }

  [Fact]
  public async Task Status_VisionOnOrOff_SaysWhetherPhotosAreOffered()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);

    (await (await ada.GetAsync("/api/assistant/status")).ReadJsonAsync()).GetProperty("photos").GetBoolean().Should().BeTrue();
    model.VisionEnabled = false;
    (await (await ada.GetAsync("/api/assistant/status")).ReadJsonAsync()).GetProperty("photos").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task Photo_Label_IsAProposalWithOnlyItsOwnPart()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    model.Replies.Enqueue("""{"kind": "nutrition_label", "description": "A nutrition label."}""");
    model.Replies.Enqueue("""{"product": "Granola", "servings_per_container": 8, "energy_kcal": 230, "protein_g": 3, "carbs_g": 37, "fat_g": 8}""");

    var response = await PostPhotoAsync(ada, Jpeg, note: "breakfast");

    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var body = await response.ReadJsonAsync();
    body.GetProperty("kind").GetString().Should().Be("nutrition_label");
    body.GetProperty("label").GetProperty("energyKcal").GetDouble().Should().Be(230);
    body.GetProperty("label").GetProperty("servingsPerContainer").GetDouble().Should().Be(8);
    body.TryGetProperty("meal", out _).Should().BeFalse();
    body.TryGetProperty("readings", out _).Should().BeFalse();
    model.Sent.Last().Single(t => t.Role == ChatTurn.User).Images.Should().ContainSingle();
  }

  [Fact]
  public async Task Photo_NoImage_Is400()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);

    await ShouldBeProblemAsync(await PostPhotoAsync(ada, null, kind: "meal"), HttpStatusCode.BadRequest, "Attach a photo.");
  }

  [Fact]
  public async Task Photo_NotAnImage_Is400()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);

    await ShouldBeProblemAsync(await PostPhotoAsync(ada, "%PDF-1.7 not a photo"u8.ToArray()), HttpStatusCode.BadRequest, "That file is not a JPEG, PNG or WebP photo.");
    model.Sent.Should().BeEmpty();
  }

  [Fact]
  public async Task Photo_LargerThanSixMegabytes_Is400()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    var big = new byte[6 * 1024 * 1024 + 10];
    Jpeg.CopyTo(big, 0);

    await ShouldBeProblemAsync(await PostPhotoAsync(ada, big), HttpStatusCode.BadRequest, "The photo is larger than 6 MB.");
  }

  [Fact]
  public async Task Photo_UnknownKind_Is400()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);

    await ShouldBeProblemAsync(await PostPhotoAsync(ada, Jpeg, kind: "selfie"), HttpStatusCode.BadRequest,
      "kind must be one of: meal, nutrition_label, medicine_label, device_display, lab_report, other.");
  }

  [Fact]
  public async Task Photo_PhotosOff_Is409()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    model.VisionEnabled = false;

    await ShouldBeProblemAsync(await PostPhotoAsync(ada, Jpeg), HttpStatusCode.Conflict, "Photos are not turned on for this server.");
  }

  [Fact]
  public async Task Photo_ModelAnswersRubbishTwice_Is422()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    model.Replies.Enqueue("a nice photo");
    model.Replies.Enqueue("still a nice photo");

    await ShouldBeProblemAsync(await PostPhotoAsync(ada, Jpeg, kind: "meal"), HttpStatusCode.UnprocessableEntity,
      "The photo could not be read. Try a closer, sharper picture.");
  }

  [Fact]
  public async Task Photo_ModelDown_Is503()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    model.Down = true;

    await ShouldBeProblemAsync(await PostPhotoAsync(ada, Jpeg), HttpStatusCode.ServiceUnavailable,
      "The home model is not answering, so the photo could not be read. Nothing was saved.");
  }

  [Fact]
  public async Task Photo_NotSignedIn_Is401()
  {
    var (root, app, _) = Create();
    using var host = root;

    (await PostPhotoAsync(app.CreateClient(), Jpeg)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Message_WithAPhoto_StoresTheDescriptionAndTellsTheModelItCannotSeeIt()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    var conversation = (await (await ada.PostJsonAsync("/api/assistant/conversations", new { })).ReadJsonAsync()).GetProperty("id").GetGuid();

    var response = await ada.PostJsonAsync($"/api/assistant/conversations/{conversation}/messages",
      new { text = "", photo = new { kind = "other", description = "A bowl of pasta with 3 meatballs." } });

    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var stream = await response.Content.ReadAsStringAsync();
    stream.Should().Contain("\"entries\":[]");
    var system = model.Sent.Last().First(t => t.Role == ChatTurn.System).Content;
    system.Should().Contain("You cannot see the photo");
    model.Sent.Last().Last().Content.Should().Be("What is in this photo?\nPhoto: A bowl of pasta with 3 meatballs.");

    var thread = await (await ada.GetAsync($"/api/assistant/conversations/{conversation}")).ReadJsonAsync();
    var user = thread.GetProperty("messages").EnumerateArray().First(m => m.GetProperty("role").GetString() == "user");
    user.GetProperty("content").GetString().Should().Be("What is in this photo?\nPhoto: A bowl of pasta with 3 meatballs.");
  }

  [Fact]
  public async Task Message_WithAPhotoAndARefusedReply_FallsBackToWhatThePhotoShows()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    var conversation = (await (await ada.PostJsonAsync("/api/assistant/conversations", new { })).ReadJsonAsync()).GetProperty("id").GetGuid();
    // A converted number is not in the photo line, so the guard refuses the reply.
    model.StreamReply = "That is about 83 kg.";

    var response = await ada.PostJsonAsync($"/api/assistant/conversations/{conversation}/messages",
      new { text = "What does this say?", photo = new { kind = "other", description = "A display showing a weight of 183.4 lb." } });

    var stream = await response.Content.ReadAsStringAsync();
    stream.Should().Contain("\"origin\":\"guard\"");
    stream.Should().Contain("Here is what the photo shows. A display showing a weight of 183.4 lb.");
    stream.Should().NotContain("Here is what your record says");
  }

  [Fact]
  public async Task Message_WithAPhotoAndTheModelDown_FallsBackToWhatThePhotoShows()
  {
    var (root, app, model) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    var conversation = (await (await ada.PostJsonAsync("/api/assistant/conversations", new { })).ReadJsonAsync()).GetProperty("id").GetGuid();
    model.Down = true;

    var response = await ada.PostJsonAsync($"/api/assistant/conversations/{conversation}/messages",
      new { text = "What does this say?", photo = new { kind = "other", description = "A display showing a weight of 183.4 lb." } });

    var stream = await response.Content.ReadAsStringAsync();
    stream.Should().Contain("\"origin\":\"template\"");
    stream.Should().Contain("Here is what it shows. A display showing a weight of 183.4 lb.");
  }

  [Fact]
  public async Task Message_PhotoDescriptionThatSoundsLikeAMeal_IsNotCaptured()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    var conversation = (await (await ada.PostJsonAsync("/api/assistant/conversations", new { })).ReadJsonAsync()).GetProperty("id").GetGuid();

    var response = await ada.PostJsonAsync($"/api/assistant/conversations/{conversation}/messages",
      new { text = "Is this a good lunch?", photo = new { kind = "meal", description = "I had a burger and fries with a beer." } });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await response.Content.ReadAsStringAsync()).Should().Contain("\"entries\":[]");
    (await (await ada.GetAsync("/api/journal")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task Message_PhotoWithUnknownKind_Is400()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);
    var conversation = (await (await ada.PostJsonAsync("/api/assistant/conversations", new { })).ReadJsonAsync()).GetProperty("id").GetGuid();

    var response = await ada.PostJsonAsync($"/api/assistant/conversations/{conversation}/messages",
      new { text = "Look", photo = new { kind = "selfie", description = "A face." } });

    await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, "kind must be one of: meal, nutrition_label, medicine_label, device_display, lab_report, other.");
  }

  [Fact]
  public async Task Journal_WithNumbers_CountsOnTheDayAndIsHiddenFromAMemberWithoutTheGrant()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, adaPerson, bob, bobPerson) = await HouseholdAsync(app);

    var created = await ada.PostJsonAsync("/api/journal", new
    {
      kind = "food",
      text = "Granola",
      nutrition = new { basis = "label", energyKcal = 460, proteinG = 6, carbsG = 74, fatG = 16 },
    });
    created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
    var entry = await created.ReadJsonAsync();
    entry.GetProperty("source").GetString().Should().Be("photo");
    entry.GetProperty("nutrition").GetProperty("basis").GetString().Should().Be("label");

    var protein = await (await ada.GetAsync("/api/data/daily?metric=protein_g")).ReadJsonAsync();
    protein.GetProperty("days").EnumerateArray().Single().GetProperty("value").GetDouble().Should().Be(6);

    var hidden = await bob.GetAsync($"/api/journal?personId={adaPerson}");
    await ShouldBeProblemAsync(hidden, HttpStatusCode.NotFound, NotShared);

    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "checkins" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    var shared = await (await bob.GetAsync($"/api/journal?personId={adaPerson}")).ReadJsonAsync();
    shared.EnumerateArray().Single().GetProperty("nutrition").GetProperty("energyKcal").GetDouble().Should().Be(460);

    (await ada.DeleteAsync($"/api/journal/{entry.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    var after = await (await ada.GetAsync("/api/data/daily?metric=protein_g")).ReadJsonAsync();
    after.GetProperty("days").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task Journal_NumbersOnASymptom_Is400()
  {
    var (root, app, _) = Create();
    using var host = root;
    var (ada, _, _, _) = await HouseholdAsync(app);

    var response = await ada.PostJsonAsync("/api/journal", new { kind = "symptom", text = "Headache", nutrition = new { basis = "estimate", energyKcal = 100 } });

    await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, "Only food and drink have numbers.");
  }
}
