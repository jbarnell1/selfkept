using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// The body package over HTTP: height on the profile, a goal, a target, a day log entry, a
/// medicine with ingredients, a strength set and a typed waist reading, read back through the
/// tally and Progress. A household member without a grant gets 404 on every read, never 403.
/// Runs on InMemory or, with SELFKEPT_TEST_CONNECTION set, on a scratch Postgres.
/// </summary>
public class BodyFlowTests
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private static async Task<(HttpClient Ada, Guid AdaPerson, HttpClient Bob, Guid BobPerson)> HouseholdAsync(ApiFactory factory)
  {
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var ada = factory.CreateClient();
    var adaAuth = await RegisterAsync(ada, $"ada-{suffix}@example.com", "Ada");
    var invite = await (await ada.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    var bob = factory.CreateClient();
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

  private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> call)
  {
    var response = await call;
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return await response.ReadJsonAsync();
  }

  private static async Task ShouldBeHiddenAsync(HttpClient client, string url)
  {
    var response = await client.GetAsync(url);
    response.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
    response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    (await response.ReadJsonAsync()).GetProperty("detail").GetString().Should().Be(NotShared, url);
  }

  private static string[] BodyReads(Guid person) =>
  [
    $"/api/goals?personId={person}",
    $"/api/targets?personId={person}",
    $"/api/targets/tally?personId={person}",
    $"/api/progress?personId={person}",
  ];

  private static string[] OtherReads(Guid person) =>
  [
    $"/api/journal?personId={person}",
    $"/api/medications?personId={person}",
    $"/api/strength?personId={person}",
  ];

  [Fact]
  public async Task BodyPackage_EndToEnd_SavesReadsBackAndHidesItFromAnUngrantedMember()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, bobPerson) = await HouseholdAsync(factory);

    // Height lives on the profile.
    var profile = await OkAsync(ada.PutJsonAsync("/api/me/profile", new { displayName = "Ada", sexAtBirth = "female", units = "metric", timeZone = "UTC", heightCm = 170 }));
    profile.GetProperty("heightCm").GetDouble().Should().Be(170);
    (await ada.PutJsonAsync("/api/me/profile", new { displayName = "Ada", sexAtBirth = "female", units = "metric", timeZone = "UTC", heightCm = 300 }))
      .StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // A goal and a target pointing at it.
    var goal = await OkAsync(ada.PostJsonAsync("/api/goals", new { slot = "main", kind = "lose_fat", startValue = 80.0, targetValue = 72.0, pacePerWeek = -0.4 }));
    var goalId = goal.GetProperty("id").GetGuid();
    goal.GetProperty("title").GetString().Should().Be("Lose fat");
    goal.GetProperty("metricKey").ValueKind.Should().Be(JsonValueKind.Null);
    goal.GetProperty("status").GetString().Should().Be("active");

    var target = await OkAsync(ada.PostJsonAsync("/api/targets", new { goalId, label = "No alcohol", kind = "journal", tag = "alcohol", comparator = "at_most", value = 0.0, period = "day" }));
    target.GetProperty("unit").GetString().Should().Be("entries");
    var refused = await ada.PostJsonAsync("/api/targets", new { label = "Pizza", kind = "journal", tag = "pizza", comparator = "at_most", value = 0.0, period = "day" });
    refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await refused.ReadJsonAsync()).GetProperty("detail").GetString().Should().Be("Unknown tag 'pizza'.");

    // A day log entry: words encrypted, tags plain.
    var entry = await OkAsync(ada.PostJsonAsync("/api/journal", new { kind = "food", text = "Salmon and greens", tags = new[] { "high_protein", "Home Cooked" }, portion = "usual" }));
    entry.GetProperty("source").GetString().Should().Be("typed");
    entry.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("high_protein", "home_cooked");

    // A supplement with ingredients, tagged on the server.
    var item = await OkAsync(ada.PostJsonAsync("/api/medications", new
    {
      kind = "supplement",
      brand = "Acme",
      product = "Morning multi",
      dose = "1 tablet",
      ingredients = new object[] { new { name = "Vitamin D3", amount = 25.0, unit = "ug" }, new { name = "Biotin", amount = 30.0, unit = "ug" } },
    }));
    item.GetProperty("substances").EnumerateArray().Select(s => s.GetString()).Should().BeEquivalentTo(["vitamin_d", "biotin"]);
    item.GetProperty("active").GetBoolean().Should().BeTrue();
    item.GetProperty("schedule").GetString().Should().Be("daily");

    // A strength set; saving the same lift today again replaces it.
    var set = await OkAsync(ada.PostJsonAsync("/api/strength", new { lift = "Squat", weightKg = 100.0, reps = 5 }));
    var again = await OkAsync(ada.PostJsonAsync("/api/strength", new { lift = "squat", weightKg = 105.0, reps = 5 }));
    again.GetProperty("id").GetGuid().Should().Be(set.GetProperty("id").GetGuid());
    again.GetProperty("estimatedMaxKg").GetDouble().Should().BeApproximately(122.5, 1e-9);

    // A typed waist reading.
    var waist = await OkAsync(ada.PostJsonAsync("/api/body/measurements", new { metricKey = "waist_cm", value = 81.0 }));
    waist.GetProperty("unit").GetString().Should().Be("cm");
    var waists = await OkAsync(ada.GetAsync("/api/body/measurements?metricKey=waist_cm"));
    waists.EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).Should().Equal(waist.GetProperty("id").GetGuid());

    // The tally: a day with something logged and no alcohol counts, but today is still open.
    var tally = await OkAsync(ada.GetAsync("/api/targets/tally"));
    tally.GetProperty("goals").EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(goalId);
    var noAlcohol = tally.GetProperty("targets").EnumerateArray().Single();
    noAlcohol.GetProperty("current").GetDouble().Should().Be(0);
    noAlcohol.GetProperty("met").ValueKind.Should().Be(JsonValueKind.Null, "a drink can still be logged today");
    noAlcohol.GetProperty("week").GetArrayLength().Should().Be(7);
    noAlcohol.GetProperty("knownDays").GetInt32().Should().Be(0);

    // Progress puts it together.
    var progress = await OkAsync(ada.GetAsync("/api/progress"));
    var body = progress.GetProperty("body");
    body.GetProperty("heightCm").GetDouble().Should().Be(170);
    body.GetProperty("waistCm").GetDouble().Should().Be(81);
    body.GetProperty("waistToHeight").GetDouble().Should().BeApproximately(81.0 / 170, 1e-6);
    progress.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("key").GetString()).Should().Contain("waist_cm");
    var goalProgress = progress.GetProperty("goals").EnumerateArray().Single();
    goalProgress.GetProperty("paceStatus").GetString().Should().Be("unknown", "there is no weigh-in yet");
    goalProgress.GetProperty("summary").GetString().Should().Contain("No readings yet");
    progress.GetProperty("targets").GetArrayLength().Should().Be(1);
    var squat = progress.GetProperty("strength").EnumerateArray().Single();
    squat.GetProperty("liftName").GetString().Should().Be("Squat");
    squat.GetProperty("sets").GetArrayLength().Should().Be(1);
    progress.GetProperty("suggestions").GetArrayLength().Should().Be(5);

    // Bob, in the same household with nothing shared, sees none of it.
    foreach (var url in BodyReads(adaPerson).Concat(OtherReads(adaPerson))) await ShouldBeHiddenAsync(bob, url);
    (await OkAsync(bob.GetAsync("/api/body/measurements?metricKey=waist_cm"))).GetArrayLength().Should().Be(0, "typed readings are the caller's own");

    // With body shared he reads the body pages, but not the day log, the medicines or the strength log.
    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "body" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    foreach (var url in BodyReads(adaPerson)) (await bob.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, url);
    foreach (var url in OtherReads(adaPerson)) await ShouldBeHiddenAsync(bob, url);
    var shared = await OkAsync(bob.GetAsync($"/api/progress?personId={adaPerson}"));
    shared.GetProperty("suggestions").GetArrayLength().Should().Be(0);
    shared.GetProperty("strength").GetArrayLength().Should().Be(0, "activity is not shared");
    var sharedTally = await OkAsync(bob.GetAsync($"/api/targets/tally?personId={adaPerson}"));
    var unknown = sharedTally.GetProperty("targets").EnumerateArray().Single();
    unknown.GetProperty("current").ValueKind.Should().Be(JsonValueKind.Null, "check-ins are not shared");
    unknown.GetProperty("week").EnumerateArray().Should().OnlyContain(d => d.ValueKind == JsonValueKind.Null);

    // Writes are always the caller's own.
    (await bob.DeleteAsync($"/api/targets/{target.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.DeleteAsync($"/api/journal/{entry.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.DeleteAsync($"/api/medications/{item.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.DeleteAsync($"/api/strength/{set.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.DeleteAsync($"/api/body/measurements/waist_cm/{waist.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.PostJsonAsync($"/api/goals/{goalId}/end", new { status = "done" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task BodyReads_WithoutABearer_Are401()
  {
    await using var factory = new ApiFactory();
    var anon = factory.CreateClient();
    var person = Guid.NewGuid();

    foreach (var url in BodyReads(person).Concat(OtherReads(person)).Append("/api/body/measurements?metricKey=weight_kg"))
    {
      (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
    }
  }
}
