using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Package 2 end to end over HTTP: sources, a batch posted twice, daily and sample reads,
/// check-ins, the overview, and the share-grant rule on every read. Runs on InMemory or, with
/// SELFKEPT_TEST_CONNECTION set, on a scratch Postgres.
/// </summary>
public class CollectFlowTests
{
  /// <summary>
  /// A fresh server per test (the first account on a server becomes its admin and closes open
  /// registration) with two household members: Ada (admin) and Bob (member by invite).
  /// </summary>
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

  private static object Sample(string uid, string metric, string start, double value, string unit, string? end = null) =>
    new { sourceUid = uid, metric, start, end, value, unit };

  [Fact]
  public async Task Endpoints_WithoutABearer_Are401()
  {
    await using var factory = new ApiFactory();
    var anon = factory.CreateClient();

    (await anon.GetAsync("/api/sources")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.PostJsonAsync("/api/sources", new { kind = "manual", deviceName = "x", platform = "web" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.PostJsonAsync("/api/ingest/batch", new { sourceId = Guid.NewGuid(), samples = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.PostJsonAsync("/api/checkins", new { mood = 3 })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/checkins")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/data/daily?metric=steps")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/data/samples?metric=steps")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/data/overview")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/catalogue/metrics")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Catalogue_ListsEveryMetricWithItsShareCategory()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);

    var response = await ada.GetAsync("/api/catalogue/metrics");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var metrics = await response.ReadJsonAsync();
    metrics.GetArrayLength().Should().BeGreaterThan(50);
    var steps = metrics.EnumerateArray().Single(m => m.GetProperty("key").GetString() == "steps");
    steps.GetProperty("category").GetString().Should().Be("activity");
    steps.GetProperty("unit").GetString().Should().Be("count");
    steps.GetProperty("goodDirection").GetString().Should().Be("up");
    steps.GetProperty("displayName").GetString().Should().NotBeNullOrEmpty();
    var sleep = metrics.EnumerateArray().Single(m => m.GetProperty("key").GetString() == "sleep_stage");
    sleep.GetProperty("valueLabels").GetProperty("3").GetString().Should().Be("deep");
    metrics.EnumerateArray().Single(m => m.GetProperty("key").GetString() == "lab.apob").GetProperty("category").GetString().Should().Be("labs");
  }

  [Fact]
  public async Task Sources_UpsertIsIdempotentPerDevice_PatchAndSoftDeleteWork()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);
    var body = new { kind = "healthkit", deviceName = "Ada's iPhone", platform = "ios", appVersion = "0.1.0" };

    var first = await ada.PostJsonAsync("/api/sources", body);
    first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
    var source = await first.ReadJsonAsync();
    var id = source.GetProperty("id").GetGuid();
    source.GetProperty("kind").GetString().Should().Be("healthkit");
    source.GetProperty("enabled").GetBoolean().Should().BeTrue();
    source.GetProperty("sampleCount").GetInt64().Should().Be(0);

    var again = await (await ada.PostJsonAsync("/api/sources", body with { appVersion = "0.1.1" })).ReadJsonAsync();
    again.GetProperty("id").GetGuid().Should().Be(id, "the same device install gets its existing row back");
    (await (await ada.GetAsync("/api/sources")).ReadJsonAsync()).GetArrayLength().Should().Be(1);

    (await ada.PostJsonAsync("/api/sources", new { kind = "fitbit", deviceName = "x", platform = "web" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var patch = await ada.PatchAsync($"/api/sources/{id}", JsonContent.Create(new { deviceName = "Ada's new iPhone" }, options: ApiFactory.Json));
    patch.StatusCode.Should().Be(HttpStatusCode.OK, await patch.Content.ReadAsStringAsync());
    (await patch.ReadJsonAsync()).GetProperty("deviceName").GetString().Should().Be("Ada's new iPhone");
    (await ada.PatchAsync($"/api/sources/{Guid.NewGuid()}", JsonContent.Create(new { enabled = false }))).StatusCode.Should().Be(HttpStatusCode.NotFound);

    (await ada.DeleteAsync($"/api/sources/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await (await ada.GetAsync("/api/sources")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
    (await ada.DeleteAsync($"/api/sources/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ada.PostJsonAsync("/api/ingest/batch", new { sourceId = id, samples = new[] { Sample("a", "heart_rate_bpm", "2026-10-29T06:00:00Z", 58, "bpm") } }))
      .StatusCode.Should().Be(HttpStatusCode.NotFound, "a deleted source takes no more data");

    // Registering again after a delete creates a fresh row.
    (await (await ada.PostJsonAsync("/api/sources", body)).ReadJsonAsync()).GetProperty("id").GetGuid().Should().NotBe(id);
  }

  [Fact]
  public async Task Batch_PostedTwice_SecondTimeIsAllDuplicates_AndReadsReflectIt()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, bobPerson) = await HouseholdAsync(factory);
    var source = await (await ada.PostJsonAsync("/api/sources", new { kind = "healthkit", deviceName = "iPhone", platform = "ios" })).ReadJsonAsync();
    var sourceId = source.GetProperty("id").GetGuid();

    var batch = new
    {
      sourceId,
      cursor = "anchor-1",
      samples = new[]
      {
        Sample("hk-steps-1", "steps", "2026-10-29T07:00:00Z", 1200, "count", "2026-10-29T07:10:00Z"),
        Sample("hk-steps-2", "steps", "2026-10-29T12:00:00Z", 800, "count", "2026-10-29T12:10:00Z"),
        Sample("hk-hr-1", "heart_rate_bpm", "2026-10-29T06:00:00Z", 58, "count/min"),
        Sample("hk-hr-2", "heart_rate_bpm", "2026-10-29T06:05:00Z", 61, "bpm"),
        Sample("hk-weight", "weight_kg", "2026-10-29T06:30:00Z", 154.3, "lb"),
        Sample("hk-bad", "not_a_metric", "2026-10-29T06:30:00Z", 1, "x"),
        Sample("hk-nounit", "weight_kg", "2026-10-29T06:30:00Z", 70, "stone"),
      },
      workouts = new[]
      {
        new { sourceUid = "hk-run-1", type = "running", start = "2026-10-29T08:00:00Z", end = "2026-10-29T08:50:00Z", distanceM = 10020, energyKcal = 612, avgHr = 148, maxHr = 171 },
      },
    };

    var first = await ada.PostJsonAsync("/api/ingest/batch", batch);
    first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
    var r1 = await first.ReadJsonAsync();
    r1.GetProperty("accepted").GetInt32().Should().Be(6, "5 samples and 1 workout");
    r1.GetProperty("duplicates").GetInt32().Should().Be(0);
    r1.GetProperty("cursor").GetString().Should().Be("anchor-1");
    var rejected = r1.GetProperty("rejected").EnumerateArray().ToDictionary(x => x.GetProperty("sourceUid").GetString()!, x => x.GetProperty("reason").GetString()!);
    rejected.Should().HaveCount(2);
    rejected["hk-bad"].Should().Contain("unknown metric");
    rejected["hk-nounit"].Should().Contain("expected 'kg'");

    var second = await (await ada.PostJsonAsync("/api/ingest/batch", batch)).ReadJsonAsync();
    second.GetProperty("accepted").GetInt32().Should().Be(0);
    second.GetProperty("duplicates").GetInt32().Should().Be(6, "resending changes nothing");
    second.GetProperty("rejected").GetArrayLength().Should().Be(2);

    var sources = await (await ada.GetAsync("/api/sources")).ReadJsonAsync();
    var mine = sources.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == sourceId);
    mine.GetProperty("sampleCount").GetInt64().Should().Be(5, "workouts are not samples");
    mine.GetProperty("cursor").GetString().Should().Be("anchor-1");
    mine.GetProperty("lastSyncAt").ValueKind.Should().Be(JsonValueKind.String);
    mine.GetProperty("lastSampleAt").GetDateTime().ToUniversalTime().Should().Be(new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc));

    // Daily: steps summed on the person's local day (UTC by default); weight converted from lb.
    var daily = await ada.GetAsync("/api/data/daily?metric=steps&from=2026-10-28&to=2026-10-30");
    daily.StatusCode.Should().Be(HttpStatusCode.OK, await daily.Content.ReadAsStringAsync());
    var series = await daily.ReadJsonAsync();
    series.GetProperty("metric").GetString().Should().Be("steps");
    series.GetProperty("unit").GetString().Should().Be("count");
    var day = series.GetProperty("days").EnumerateArray().Single();
    day.GetProperty("day").GetString().Should().Be("2026-10-29");
    day.GetProperty("value").GetDouble().Should().Be(2000);
    day.GetProperty("count").GetInt32().Should().Be(2);
    day.TryGetProperty("min", out _).Should().BeFalse("sums carry no min/max");

    var hr = (await (await ada.GetAsync("/api/data/daily?metric=heart_rate_bpm&from=2026-10-29&to=2026-10-29")).ReadJsonAsync()).GetProperty("days")[0];
    hr.GetProperty("value").GetDouble().Should().Be(59.5);
    hr.GetProperty("min").GetDouble().Should().Be(58);
    hr.GetProperty("max").GetDouble().Should().Be(61);

    var weight = (await (await ada.GetAsync("/api/data/daily?metric=weight_kg&from=2026-10-29&to=2026-10-29")).ReadJsonAsync()).GetProperty("days")[0];
    weight.GetProperty("value").GetDouble().Should().BeApproximately(69.99, 0.01);

    (await ada.GetAsync("/api/data/daily?metric=steps&from=2026-10-30&to=2026-10-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.GetAsync("/api/data/daily?metric=not_a_metric")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ada.GetAsync("/api/data/daily")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Samples: newest first, keyset paged.
    var page1 = await (await ada.GetAsync("/api/data/samples?metric=heart_rate_bpm&limit=1")).ReadJsonAsync();
    page1.GetProperty("items").GetArrayLength().Should().Be(1);
    page1.GetProperty("items")[0].GetProperty("value").GetDouble().Should().Be(61);
    page1.GetProperty("items")[0].GetProperty("unit").GetString().Should().Be("bpm");
    page1.GetProperty("items")[0].GetProperty("sourceId").GetGuid().Should().Be(sourceId);
    var cursor = page1.GetProperty("nextCursor").GetString();
    cursor.Should().NotBeNullOrEmpty();
    var page2 = await (await ada.GetAsync($"/api/data/samples?metric=heart_rate_bpm&limit=1&cursor={Uri.EscapeDataString(cursor!)}")).ReadJsonAsync();
    page2.GetProperty("items")[0].GetProperty("value").GetDouble().Should().Be(58);
    page2.TryGetProperty("nextCursor", out _).Should().BeFalse();
    (await ada.GetAsync("/api/data/samples?metric=heart_rate_bpm&cursor=garbage")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Overview lists each metric with counts and the latest value.
    var overview = await (await ada.GetAsync("/api/data/overview")).ReadJsonAsync();
    var byMetric = overview.GetProperty("metrics").EnumerateArray().ToDictionary(m => m.GetProperty("metric").GetString()!);
    byMetric.Keys.Should().BeEquivalentTo(new[] { "steps", "heart_rate_bpm", "weight_kg" });
    byMetric["steps"].GetProperty("count").GetInt64().Should().Be(2);
    byMetric["steps"].GetProperty("lastValue").GetDouble().Should().Be(800);
    byMetric["heart_rate_bpm"].GetProperty("firstAt").GetDateTime().ToUniversalTime().Should().Be(new DateTime(2026, 10, 29, 6, 0, 0, DateTimeKind.Utc));

    // Bob may not read Ada's data until she shares, and then only the shared category.
    (await bob.GetAsync($"/api/data/daily?metric=steps&personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/data/samples?metric=steps&personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/data/overview?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/data/overview?personId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "activity" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    var bobDaily = await bob.GetAsync($"/api/data/daily?metric=steps&personId={adaPerson}&from=2026-10-29&to=2026-10-29");
    bobDaily.StatusCode.Should().Be(HttpStatusCode.OK, await bobDaily.Content.ReadAsStringAsync());
    (await bobDaily.ReadJsonAsync()).GetProperty("days")[0].GetProperty("value").GetDouble().Should().Be(2000);
    (await bob.GetAsync($"/api/data/samples?metric=steps&personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.OK);
    (await bob.GetAsync($"/api/data/daily?metric=heart_rate_bpm&personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "vitals were not shared");
    (await bob.GetAsync($"/api/data/samples?metric=weight_kg&personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "body was not shared");
    var bobOverview = await (await bob.GetAsync($"/api/data/overview?personId={adaPerson}")).ReadJsonAsync();
    bobOverview.GetProperty("metrics").EnumerateArray().Select(m => m.GetProperty("metric").GetString()).Should().BeEquivalentTo(new[] { "steps" });

    // Bob cannot post into Ada's source.
    (await bob.PostJsonAsync("/api/ingest/batch", batch)).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // A disabled source refuses batches; the cap refuses oversized ones.
    (await ada.PatchAsync($"/api/sources/{sourceId}", JsonContent.Create(new { enabled = false }))).StatusCode.Should().Be(HttpStatusCode.OK);
    (await ada.PostJsonAsync("/api/ingest/batch", batch)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await ada.PatchAsync($"/api/sources/{sourceId}", JsonContent.Create(new { enabled = true }))).StatusCode.Should().Be(HttpStatusCode.OK);
    var huge = new { sourceId, samples = Enumerable.Range(0, 5001).Select(i => Sample($"big-{i}", "heart_rate_bpm", "2026-10-29T06:00:00Z", 60, "bpm")).ToArray() };
    var tooBig = await ada.PostJsonAsync("/api/ingest/batch", huge);
    tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    tooBig.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
  }

  [Fact]
  public async Task Checkins_CreateReadDelete_WriteTheMoodSampleAndFollowTheCheckinsGrant()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, bobPerson) = await HouseholdAsync(factory);

    var created = await ada.PostJsonAsync("/api/checkins", new { at = "2026-10-29T18:30:00Z", mood = 5, note = "Good day, long walk.", tags = new[] { "Walk", "walk", "sun" } });
    created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
    var checkin = await created.ReadJsonAsync();
    var checkinId = checkin.GetProperty("id").GetGuid();
    checkin.GetProperty("personId").GetGuid().Should().Be(adaPerson);
    checkin.GetProperty("mood").GetInt32().Should().Be(5);
    checkin.GetProperty("note").GetString().Should().Be("Good day, long walk.");
    checkin.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("walk", "sun");

    (await ada.PostJsonAsync("/api/checkins", new { at = "2026-10-29T08:00:00Z", mood = 3 })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await ada.PostJsonAsync("/api/checkins", new { mood = 7 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PostJsonAsync("/api/checkins", new { mood = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var list = await (await ada.GetAsync("/api/checkins?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z")).ReadJsonAsync();
    list.GetArrayLength().Should().Be(2);
    list[0].GetProperty("mood").GetInt32().Should().Be(5, "newest first");
    list[0].GetProperty("note").GetString().Should().Be("Good day, long walk.", "the note is decrypted on read");
    list[1].TryGetProperty("note", out var nullNote).Should().BeTrue();
    nullNote.ValueKind.Should().Be(JsonValueKind.Null);

    // The check-in also landed as a checkin_mood sample under the system source and in the daily summary.
    var sources = await (await ada.GetAsync("/api/sources")).ReadJsonAsync();
    var system = sources.EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "manual");
    system.GetProperty("deviceName").GetString().Should().Be("Check-ins");
    system.GetProperty("sampleCount").GetInt64().Should().Be(2);
    var moodDay = (await (await ada.GetAsync("/api/data/daily?metric=checkin_mood&from=2026-10-29&to=2026-10-29")).ReadJsonAsync()).GetProperty("days")[0];
    moodDay.GetProperty("value").GetDouble().Should().Be(4);
    moodDay.GetProperty("min").GetDouble().Should().Be(3);
    moodDay.GetProperty("max").GetDouble().Should().Be(5);
    moodDay.GetProperty("count").GetInt32().Should().Be(2);
    var overview = await (await ada.GetAsync("/api/data/overview")).ReadJsonAsync();
    overview.GetProperty("metrics").EnumerateArray().Single(m => m.GetProperty("metric").GetString() == "checkin_mood").GetProperty("count").GetInt64().Should().Be(2);

    // Bob: nothing until check-ins are shared; an activity grant is not enough.
    (await bob.GetAsync($"/api/checkins?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "activity" } });
    (await bob.GetAsync($"/api/checkins?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "checkins" } });
    var bobList = await bob.GetAsync($"/api/checkins?personId={adaPerson}&from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z");
    bobList.StatusCode.Should().Be(HttpStatusCode.OK, await bobList.Content.ReadAsStringAsync());
    (await bobList.ReadJsonAsync()).GetArrayLength().Should().Be(2);

    // Only the owner deletes; deleting removes the mood sample and recomputes the day.
    (await bob.DeleteAsync($"/api/checkins/{checkinId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ada.DeleteAsync($"/api/checkins/{checkinId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await ada.DeleteAsync($"/api/checkins/{checkinId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await (await ada.GetAsync("/api/checkins?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z")).ReadJsonAsync()).GetArrayLength().Should().Be(1);
    var afterDelete = (await (await ada.GetAsync("/api/data/daily?metric=checkin_mood&from=2026-10-29&to=2026-10-29")).ReadJsonAsync()).GetProperty("days")[0];
    afterDelete.GetProperty("value").GetDouble().Should().Be(3);
    afterDelete.GetProperty("count").GetInt32().Should().Be(1);
    sources = await (await ada.GetAsync("/api/sources")).ReadJsonAsync();
    sources.EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "manual").GetProperty("sampleCount").GetInt64().Should().Be(1);
  }

  [Fact]
  public async Task Daily_UsesThePersonsTimeZone_ForSleepAndSteps()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);
    // Ada lives at UTC+10 (no DST): 23:30 local on the 28th is 13:30Z, and a night ending 06:30 local is 20:30Z the day before.
    var profile = await ada.PutJsonAsync("/api/me/profile", new { displayName = "Ada", sexAtBirth = "female", units = "metric", timeZone = "Australia/Brisbane" });
    profile.StatusCode.Should().Be(HttpStatusCode.OK, await profile.Content.ReadAsStringAsync());
    var sourceId = (await (await ada.PostJsonAsync("/api/sources", new { kind = "health_connect", deviceName = "Pixel", platform = "android" })).ReadJsonAsync()).GetProperty("id").GetGuid();

    var batch = new
    {
      sourceId,
      samples = new[]
      {
        // 23:30 local on 28 Oct (13:30Z): counts for the 28th in Brisbane, would be the 28th in UTC too, so add one that differs:
        Sample("st-1", "steps", "2026-10-28T13:30:00Z", 300, "count", "2026-10-28T13:40:00Z"),
        // 08:00 local on 29 Oct is 22:00Z on the 28th: a UTC day would put it on the 28th, Brisbane on the 29th.
        Sample("st-2", "steps", "2026-10-28T22:00:00Z", 5000, "count", "2026-10-28T22:10:00Z"),
        // Night of 28/29 local: 23:00 to 06:30 local = 13:00Z to 20:30Z on the 28th; attributed to the 29th (the day it ended).
        Sample("sl-1", "sleep_stage", "2026-10-28T13:00:00Z", 2, "stage", "2026-10-28T15:00:00Z"),
        Sample("sl-2", "sleep_stage", "2026-10-28T15:00:00Z", 3, "stage", "2026-10-28T17:00:00Z"),
        Sample("sl-3", "sleep_stage", "2026-10-28T17:00:00Z", 1, "stage", "2026-10-28T17:15:00Z"),
        Sample("sl-4", "sleep_stage", "2026-10-28T17:15:00Z", 4, "stage", "2026-10-28T20:30:00Z"),
      },
    };
    var posted = await ada.PostJsonAsync("/api/ingest/batch", batch);
    posted.StatusCode.Should().Be(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
    (await posted.ReadJsonAsync()).GetProperty("accepted").GetInt32().Should().Be(6);

    var steps = (await (await ada.GetAsync("/api/data/daily?metric=steps&from=2026-10-27&to=2026-10-30")).ReadJsonAsync()).GetProperty("days").EnumerateArray()
      .ToDictionary(d => d.GetProperty("day").GetString()!, d => d.GetProperty("value").GetDouble());
    steps.Should().Equal(new Dictionary<string, double> { ["2026-10-28"] = 300, ["2026-10-29"] = 5000 });

    var sleep = (await (await ada.GetAsync("/api/data/daily?metric=sleep_stage&from=2026-10-27&to=2026-10-30")).ReadJsonAsync()).GetProperty("days").EnumerateArray().Single();
    sleep.GetProperty("day").GetString().Should().Be("2026-10-29", "a night belongs to the day it ended");
    sleep.GetProperty("value").GetDouble().Should().Be(435, "awake minutes are excluded");
  }
}
