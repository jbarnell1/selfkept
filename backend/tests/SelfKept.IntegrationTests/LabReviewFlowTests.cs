using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// The lab review package over HTTP: two panels with draw conditions, the review, the timeline,
/// trials, the links and related readings, and the household overview, with the share rules on
/// every read (labs for panels, body for weight and trials). Runs on InMemory or, with
/// SELFKEPT_TEST_CONNECTION set, on a scratch Postgres.
/// </summary>
public class LabReviewFlowTests
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

  private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd");

  private static async Task ShareAsync(HttpClient owner, Guid viewer, params string[] categories)
  {
    var response = await owner.PutJsonAsync($"/api/household/shares/{viewer}", new { categories });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
  }

  private static async Task ExpectNotSharedAsync(HttpResponseMessage response)
  {
    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await response.ReadJsonAsync()).GetProperty("detail").GetString().Should().Be(NotShared);
  }

  [Fact]
  public async Task Endpoints_WithoutABearer_Are401()
  {
    await using var factory = new ApiFactory();
    var anon = factory.CreateClient();

    (await anon.GetAsync($"/api/labs/reports/{Guid.NewGuid()}/review")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.PutJsonAsync($"/api/labs/reports/{Guid.NewGuid()}/conditions", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/labs/timeline")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/labs/links")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/labs/markers/lab.ferritin/related")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/trials")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.PostJsonAsync("/api/trials", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/household/overview")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task TwoPanels_Review_Timeline_Trial_Related_Overview_AndTheShareRules()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, bobPerson) = await HouseholdAsync(factory);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var first = today.AddDays(-60);
    var second = today.AddDays(-3);

    // Creatine from before the first panel; a weigh-in near each draw.
    (await ada.PostJsonAsync("/api/medications", new { kind = "supplement", product = "Creatine monohydrate", startedOn = Iso(first.AddDays(-30)) }))
      .StatusCode.Should().Be(HttpStatusCode.OK);
    (await ada.PostJsonAsync("/api/body/measurements", new { metricKey = "weight_kg", value = 84.0, at = first.AddDays(-2).ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc) }))
      .StatusCode.Should().Be(HttpStatusCode.OK);
    (await ada.PostJsonAsync("/api/body/measurements", new { metricKey = "weight_kg", value = 80.5, at = second.AddDays(1).ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc) }))
      .StatusCode.Should().Be(HttpStatusCode.OK);

    // First panel: Rythm, no conditions sent, so pre-filled as a morning fast.
    var firstPanel = await ada.PostJsonAsync("/api/labs/reports", new
    {
      provider = "rythm", collectedOn = Iso(first),
      results = new object[]
      {
        new { metricKey = "lab.ggt", value = 40, unit = "U/L" },
        new { metricKey = "lab.tsh", value = 2.0, unit = "mIU/L" },
        new { metricKey = "lab.ldl", value = 150, unit = "mg/dL" },
        new { metricKey = "lab.triglycerides", value = 120, unit = "mg/dL" },
      },
    });
    firstPanel.StatusCode.Should().Be(HttpStatusCode.OK, await firstPanel.Content.ReadAsStringAsync());
    var firstBody = await firstPanel.ReadJsonAsync();
    var conditions = firstBody.GetProperty("conditions");
    conditions.GetProperty("fasting").GetString().Should().Be("yes");
    conditions.GetProperty("fastingHours").GetInt32().Should().Be(8);
    conditions.GetProperty("drawPeriod").GetString().Should().Be("morning");
    conditions.GetProperty("defaulted").GetBoolean().Should().BeTrue();

    // A trial started after the first panel, read at the second.
    var trialResponse = await ada.PostJsonAsync("/api/trials", new
    {
      title = "Oats instead of a bagel", change = "breakfast swap", startedOn = Iso(first.AddDays(2)), readOn = Iso(second), metricKeys = new[] { "lab.ldl", "lab.ggt" },
    });
    trialResponse.StatusCode.Should().Be(HttpStatusCode.OK, await trialResponse.Content.ReadAsStringAsync());
    var trial = await trialResponse.ReadJsonAsync();
    var trialId = trial.GetProperty("id").GetGuid();
    trial.GetProperty("title").GetString().Should().Be("Oats instead of a bagel");
    trial.GetProperty("status").GetString().Should().Be("active");
    (await ada.PostJsonAsync("/api/trials", new { title = "Bad", startedOn = Iso(today), readOn = Iso(today.AddDays(-1)) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PostJsonAsync("/api/trials", new { title = "Bad", readOn = Iso(today), metricKeys = new[] { "lab.nope" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Second panel: manual, not fasting, afternoon.
    var secondPanel = await ada.PostJsonAsync("/api/labs/reports", new
    {
      provider = "manual", collectedOn = Iso(second),
      conditions = new { fasting = "no", drawTime = "15:40" },
      results = new object[]
      {
        new { metricKey = "lab.ggt", value = 28, unit = "U/L" },
        new { metricKey = "lab.tsh", value = 2.1, unit = "mIU/L" },
        new { metricKey = "lab.ldl", value = 120, unit = "mg/dL" },
        new { metricKey = "lab.triglycerides", value = 168, unit = "mg/dL" },
      },
    });
    secondPanel.StatusCode.Should().Be(HttpStatusCode.OK, await secondPanel.Content.ReadAsStringAsync());
    var secondBody = await secondPanel.ReadJsonAsync();
    var reportId = secondBody.GetProperty("id").GetGuid();
    secondBody.GetProperty("conditions").GetProperty("drawPeriod").GetString().Should().Be("afternoon");
    secondBody.GetProperty("conditions").GetProperty("defaulted").GetBoolean().Should().BeFalse();

    // The review: GGT working, LDL and triglycerides cannot tell, the fasting difference named.
    var review = await (await ada.GetAsync($"/api/labs/reports/{reportId}/review")).ReadJsonAsync();
    review.GetProperty("previousCollectedOn").GetString().Should().Be(Iso(first));
    review.GetProperty("conditionsDiffer").EnumerateArray().Select(d => d.GetProperty("condition").GetString()).Should().Contain(new[] { "fasting", "drawPeriod" });
    var markers = review.GetProperty("markers").EnumerateArray().ToDictionary(m => m.GetProperty("metricKey").GetString()!);
    markers["lab.ggt"].GetProperty("verdict").GetString().Should().Be("working");
    markers["lab.ggt"].GetProperty("rcvPercent").GetDouble().Should().Be(24.7);
    markers["lab.ggt"].GetProperty("variation").GetProperty("source").GetString().Should().Contain("Carobene 2017");
    markers["lab.ldl"].GetProperty("verdict").GetString().Should().Be("cannot_tell");
    markers["lab.triglycerides"].GetProperty("readWithCare").GetArrayLength().Should().BeGreaterThan(0);
    markers["lab.tsh"].GetProperty("verdict").GetString().Should().Be("no_clear_change");
    review.GetProperty("takingNow")[0].GetProperty("product").GetString().Should().Be("Creatine monohydrate");
    review.GetProperty("substancesNow").EnumerateArray().Select(s => s.GetString()).Should().Contain("creatine");
    review.GetProperty("weight").GetProperty("changeKg").GetDouble().Should().Be(-3.5);
    var reviewTrial = review.GetProperty("trials")[0];
    reviewTrial.GetProperty("trial").GetProperty("id").GetGuid().Should().Be(trialId);
    reviewTrial.GetProperty("markers").EnumerateArray().Select(m => m.GetProperty("verdict").GetString()).Should().Contain("working");
    review.GetProperty("verdictCounts").GetProperty("working").GetInt32().Should().Be(1);

    // The timeline: oldest first, weight at each draw, creatine on both dates.
    var timeline = await (await ada.GetAsync("/api/labs/timeline")).ReadJsonAsync();
    timeline.GetProperty("weightShared").GetBoolean().Should().BeTrue();
    var panels = timeline.GetProperty("panels").EnumerateArray().ToList();
    panels.Select(p => p.GetProperty("collectedOn").GetString()).Should().Equal(Iso(first), Iso(second));
    panels[0].GetProperty("weight").GetProperty("kg").GetDouble().Should().Be(84.0);
    panels[1].GetProperty("weight").GetProperty("kg").GetDouble().Should().Be(80.5);
    panels[1].GetProperty("substances").EnumerateArray().Select(s => s.GetString()).Should().Contain("creatine");

    // Editing the conditions replaces them.
    var edited = await ada.PutJsonAsync($"/api/labs/reports/{reportId}/conditions", new { fasting = "yes", fastingHours = 12, drawTime = "08:10" });
    edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
    (await edited.ReadJsonAsync()).GetProperty("conditions").GetProperty("drawPeriod").GetString().Should().Be("morning");
    (await ada.PutJsonAsync($"/api/labs/reports/{reportId}/conditions", new { fasting = "sometimes" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Links and related readings.
    var links = await (await ada.GetAsync("/api/labs/links")).ReadJsonAsync();
    links.EnumerateArray().Select(l => l.GetProperty("id").GetString()).Should().Contain("apob_ldl");
    var related = await (await ada.GetAsync("/api/labs/markers/ldl/related")).ReadJsonAsync();
    related.GetProperty("metricKey").GetString().Should().Be("lab.ldl");
    var byKey = related.GetProperty("related").EnumerateArray().ToDictionary(r => r.GetProperty("key").GetString()!);
    byKey["lab.triglycerides"].GetProperty("latestValue").GetDouble().Should().Be(168);
    byKey.Should().ContainKey("lab.apob");
    (await ada.GetAsync("/api/labs/markers/nope/related")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // Trials: patch to done with an outcome, list with includeEnded.
    var patched = await ada.PatchAsJsonAsync($"/api/trials/{trialId}", new { status = "done", outcome = "Kept it" }, ApiFactory.Json);
    patched.StatusCode.Should().Be(HttpStatusCode.OK, await patched.Content.ReadAsStringAsync());
    (await patched.ReadJsonAsync()).GetProperty("outcome").GetString().Should().Be("Kept it");
    (await (await ada.GetAsync("/api/trials")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
    (await (await ada.GetAsync("/api/trials?includeEnded=true")).ReadJsonAsync()).GetArrayLength().Should().Be(1);

    // Bob without any grant: every read is a 404 with the same words; his own writes on Ada's rows are 404 too.
    await ExpectNotSharedAsync(await bob.GetAsync($"/api/labs/reports/{reportId}/review"));
    await ExpectNotSharedAsync(await bob.GetAsync($"/api/labs/timeline?personId={adaPerson}"));
    await ExpectNotSharedAsync(await bob.GetAsync($"/api/trials?personId={adaPerson}"));
    await ExpectNotSharedAsync(await bob.GetAsync($"/api/labs/markers/ldl/related?personId={adaPerson}"));
    (await bob.PutJsonAsync($"/api/labs/reports/{reportId}/conditions", new { fasting = "no" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.PatchAsJsonAsync($"/api/trials/{trialId}", new { title = "mine now" }, ApiFactory.Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.DeleteAsync($"/api/trials/{trialId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    var overview = await (await bob.GetAsync("/api/household/overview")).ReadJsonAsync();
    var adaRow = overview.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("personId").GetGuid() == adaPerson);
    adaRow.GetProperty("isSelf").GetBoolean().Should().BeFalse();
    adaRow.TryGetProperty("labs", out _).Should().BeFalse("no labs grant: absent, not empty");
    adaRow.TryGetProperty("body", out _).Should().BeFalse();
    adaRow.GetProperty("sharedCategories").GetArrayLength().Should().Be(0);
    var bobRow = overview.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("personId").GetGuid() == bobPerson);
    bobRow.GetProperty("isSelf").GetBoolean().Should().BeTrue();
    bobRow.GetProperty("labs").GetProperty("panels").GetInt32().Should().Be(0, "his own labs are present, and empty");

    // Labs only: the review and the timeline open, without weight or trials.
    await ShareAsync(ada, bobPerson, "labs");
    var shared = await (await bob.GetAsync($"/api/labs/reports/{reportId}/review")).ReadJsonAsync();
    shared.GetProperty("markers").GetArrayLength().Should().Be(4);
    shared.TryGetProperty("weight", out _).Should().BeFalse("weight needs the body grant");
    shared.TryGetProperty("trials", out _).Should().BeFalse("trials need the body grant");
    var sharedTimeline = await (await bob.GetAsync($"/api/labs/timeline?personId={adaPerson}")).ReadJsonAsync();
    sharedTimeline.GetProperty("weightShared").GetBoolean().Should().BeFalse();
    sharedTimeline.GetProperty("panels")[0].GetProperty("weight").ValueKind.Should().Be(JsonValueKind.Null);
    await ExpectNotSharedAsync(await bob.GetAsync($"/api/trials?personId={adaPerson}"));
    var sharedRelated = await (await bob.GetAsync($"/api/labs/markers/ferritin/related?personId={adaPerson}")).ReadJsonAsync();
    var weightRow = sharedRelated.GetProperty("related").EnumerateArray().Single(r => r.GetProperty("key").GetString() == "weight_kg");
    weightRow.GetProperty("shared").GetBoolean().Should().BeFalse("weight follows the body grant");
    weightRow.TryGetProperty("latestValue", out _).Should().BeFalse();
    adaRow = (await (await bob.GetAsync("/api/household/overview")).ReadJsonAsync()).GetProperty("members").EnumerateArray().Single(m => m.GetProperty("personId").GetGuid() == adaPerson);
    adaRow.GetProperty("labs").GetProperty("panels").GetInt32().Should().Be(2);
    adaRow.TryGetProperty("body", out _).Should().BeFalse();

    // Labs and body: everything.
    await ShareAsync(ada, bobPerson, "labs", "body");
    var full = await (await bob.GetAsync($"/api/labs/reports/{reportId}/review")).ReadJsonAsync();
    full.GetProperty("weight").GetProperty("now").GetProperty("kg").GetDouble().Should().Be(80.5);
    full.GetProperty("trials").GetArrayLength().Should().Be(1);
    (await (await bob.GetAsync($"/api/trials?personId={adaPerson}&includeEnded=true")).ReadJsonAsync()).GetArrayLength().Should().Be(1);
    adaRow = (await (await bob.GetAsync("/api/household/overview")).ReadJsonAsync()).GetProperty("members").EnumerateArray().Single(m => m.GetProperty("personId").GetGuid() == adaPerson);
    adaRow.GetProperty("body").GetProperty("weightKg").GetDouble().Should().Be(80.5);

    // Ada deletes her trial.
    (await ada.DeleteAsync($"/api/trials/{trialId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await ada.DeleteAsync($"/api/trials/{trialId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }
}
