using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Data;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Package 3 over HTTP: seeded daily_summary rows, a recompute, then /today, /insights, reactions,
/// dismissals, /trends and the insights share rule on every read. Runs on InMemory or, with
/// SELFKEPT_TEST_CONNECTION set, on a scratch Postgres.
/// </summary>
public class InsightsFlowTests
{
  private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

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

  /// <summary>
  /// 28 days of HRV, resting HR, sleep and steps ending yesterday, shaped so hrv_low_run and
  /// sleep_debt fire for yesterday, plus last night's rows for today. Written straight to
  /// daily_summary: the Collect package already proves ingest produces these rows.
  /// </summary>
  private static async Task SeedSummariesAsync(ApiFactory factory, Guid personId, int days = 28)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
    var yesterday = Today.AddDays(-1);
    var now = DateTime.UtcNow;
    for (var i = 0; i < days; i++)
    {
      var day = yesterday.AddDays(-(days - 1 - i));
      var lastThree = i >= days - 3;
      context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "hrv_sdnn_ms", Day = day, Value = lastThree ? 28 : 50 + (i % 5) * 2, Count = 1, ComputedAt = now });
      context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "resting_hr_bpm", Day = day, Value = 55 + (i % 3), Min = 50, Max = 60, Count = 1, ComputedAt = now });
      context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "sleep_stage", Day = day, Value = lastThree ? 380 : 470 + (i % 4) * 5, Count = 4, ComputedAt = now });
      context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "steps", Day = day, Value = 7000 + (i % 7) * 100, Count = 10, ComputedAt = now });
    }
    context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "sleep_stage", Day = Today, Value = 435, Count = 4, ComputedAt = now });
    context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "hrv_sdnn_ms", Day = Today, Value = 30, Count = 1, ComputedAt = now });
    context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "resting_hr_bpm", Day = Today, Value = 56, Count = 1, ComputedAt = now });
    await context.SaveChangesAsync();
  }

  [Fact]
  public async Task Endpoints_WithoutABearer_Are401()
  {
    await using var factory = new ApiFactory();
    var anon = factory.CreateClient();

    (await anon.GetAsync("/api/today")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/insights")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.PostJsonAsync($"/api/insights/{Guid.NewGuid()}/reaction", new { reaction = "helpful" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.PostJsonAsync($"/api/insights/{Guid.NewGuid()}/dismiss", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.PostJsonAsync("/api/insights/recompute", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/trends")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/trends/steps")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Version_ListsTheRulesFeature_AndNotTheModelWhenDisabled()
  {
    await using var factory = new ApiFactory();
    var features = (await (await factory.CreateClient().GetAsync("/api/system/version")).ReadJsonAsync()).GetProperty("features").EnumerateArray().Select(f => f.GetString()).ToList();

    features.Should().Contain("insights.rules");
    features.Should().NotContain("insights.model");
  }

  [Fact]
  public async Task Today_WithNoData_IsLearning_AndRecomputeStoresTheLearningCard()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, _, _) = await HouseholdAsync(factory);

    var today = await ada.GetAsync("/api/today");
    today.StatusCode.Should().Be(HttpStatusCode.OK, await today.Content.ReadAsStringAsync());
    var body = await today.ReadJsonAsync();
    body.GetProperty("learning").GetBoolean().Should().BeTrue();
    body.GetProperty("sentence").GetString().Should().Contain("Still learning");
    body.GetProperty("generated").GetBoolean().Should().BeFalse();
    body.GetProperty("cards").GetArrayLength().Should().Be(0);
    body.GetProperty("readiness").GetProperty("hrv").ValueKind.Should().Be(JsonValueKind.Null);

    var recompute = await ada.PostJsonAsync("/api/insights/recompute", new { });
    recompute.StatusCode.Should().Be(HttpStatusCode.OK, await recompute.Content.ReadAsStringAsync());
    var result = await recompute.ReadJsonAsync();
    result.GetProperty("personId").GetGuid().Should().Be(adaPerson);
    result.GetProperty("baselines").GetInt32().Should().Be(0);
    result.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()).Should().Equal("learning");

    var archive = await (await ada.GetAsync("/api/insights")).ReadJsonAsync();
    archive.GetArrayLength().Should().Be(1);
    archive[0].GetProperty("kind").GetString().Should().Be("learning");
    archive[0].GetProperty("severity").GetString().Should().Be("notice");

    // The learning card never leads Today.
    (await (await ada.GetAsync("/api/today")).ReadJsonAsync()).GetProperty("cards").GetArrayLength().Should().Be(0);
    (await (await ada.GetAsync("/api/trends")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task Recompute_ThenToday_Insights_Reaction_Dismiss_AndTrends_EndToEnd()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, bobPerson) = await HouseholdAsync(factory);
    await SeedSummariesAsync(factory, adaPerson);

    var recompute = await ada.PostJsonAsync("/api/insights/recompute", new { });
    recompute.StatusCode.Should().Be(HttpStatusCode.OK, await recompute.Content.ReadAsStringAsync());
    var result = await recompute.ReadJsonAsync();
    result.GetProperty("day").GetString().Should().Be(Today.AddDays(-1).ToString("yyyy-MM-dd"));
    result.GetProperty("baselines").GetInt32().Should().Be(4);
    var kinds = result.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()).ToList();
    kinds.Should().Contain("hrv_low_run").And.Contain("sleep_debt").And.NotContain("learning");

    // Running the same day again re-ranks instead of duplicating.
    (await (await ada.PostJsonAsync("/api/insights/recompute", new { day = Today.AddDays(-1) })).ReadJsonAsync()).GetProperty("insights").GetInt32().Should().Be(kinds.Count);
    (await (await ada.GetAsync("/api/insights")).ReadJsonAsync()).GetArrayLength().Should().Be(kinds.Count);
    (await ada.PostJsonAsync("/api/insights/recompute", new { day = Today.AddDays(1) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Today: sleep and HRV from last night, two cards, the sentence from the templates.
    var today = await ada.GetAsync("/api/today");
    today.StatusCode.Should().Be(HttpStatusCode.OK, await today.Content.ReadAsStringAsync());
    var body = await today.ReadJsonAsync();
    body.GetProperty("learning").GetBoolean().Should().BeFalse();
    body.GetProperty("sleepMinutes").GetDouble().Should().Be(435);
    body.GetProperty("readiness").GetProperty("hrv").GetString().Should().Be("below");
    body.GetProperty("readiness").GetProperty("restingHr").GetString().Should().Be("inside");
    body.GetProperty("readiness").GetProperty("sleep").GetString().Should().Be("below");
    body.GetProperty("sentence").GetString().Should().Contain("7 h 15 min").And.Contain("HRV is below your usual range");
    body.GetProperty("generated").GetBoolean().Should().BeFalse();
    var cards = body.GetProperty("cards").EnumerateArray().ToList();
    cards.Should().HaveCount(2);
    cards.Select(c => c.GetProperty("kind").GetString()).Should().BeEquivalentTo(new[] { "hrv_low_run", "sleep_debt" });
    cards.Should().OnlyContain(c => c.GetProperty("severity").GetString() == "watch");
    cards.Should().OnlyContain(c => c.GetProperty("shownAt").ValueKind == JsonValueKind.String);
    cards[0].GetProperty("text").GetString().Should().NotBeNullOrEmpty();
    cards[0].GetProperty("generated").GetBoolean().Should().BeFalse();
    cards[0].GetProperty("evidence").ValueKind.Should().Be(JsonValueKind.Object);
    body.GetProperty("lastCheckin").ValueKind.Should().Be(JsonValueKind.Null);

    // A check-in shows up on Today and in the sentence's facts.
    (await ada.PostJsonAsync("/api/checkins", new { mood = 5 })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await (await ada.GetAsync("/api/today")).ReadJsonAsync()).GetProperty("lastCheckin").GetProperty("mood").GetInt32().Should().Be(5);

    // Archive: newest first, filter and dismissal.
    var archive = await (await ada.GetAsync("/api/insights")).ReadJsonAsync();
    archive.GetArrayLength().Should().Be(kinds.Count);
    var hrvCard = archive.EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "hrv_low_run");
    var hrvId = hrvCard.GetProperty("id").GetGuid();
    hrvCard.GetProperty("metrics").EnumerateArray().Select(m => m.GetString()).Should().Equal("hrv_sdnn_ms");
    hrvCard.GetProperty("reaction").ValueKind.Should().Be(JsonValueKind.Null);
    (await (await ada.GetAsync($"/api/insights?from={Today.AddDays(-10):yyyy-MM-dd}&to={Today.AddDays(-5):yyyy-MM-dd}")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
    (await ada.GetAsync($"/api/insights?from={Today:yyyy-MM-dd}&to={Today.AddDays(-5):yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var reacted = await ada.PostJsonAsync($"/api/insights/{hrvId}/reaction", new { reaction = "helpful" });
    reacted.StatusCode.Should().Be(HttpStatusCode.OK, await reacted.Content.ReadAsStringAsync());
    (await reacted.ReadJsonAsync()).GetProperty("reaction").GetString().Should().Be("helpful");
    (await ada.PostJsonAsync($"/api/insights/{hrvId}/reaction", new { reaction = "meh" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PostJsonAsync($"/api/insights/{Guid.NewGuid()}/reaction", new { reaction = "helpful" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.PostJsonAsync($"/api/insights/{hrvId}/reaction", new { reaction = "wrong" })).StatusCode.Should().Be(HttpStatusCode.NotFound, "only the owner reacts");

    var dismissed = await ada.PostJsonAsync($"/api/insights/{hrvId}/dismiss", new { });
    dismissed.StatusCode.Should().Be(HttpStatusCode.OK, await dismissed.Content.ReadAsStringAsync());
    (await dismissed.ReadJsonAsync()).GetProperty("dismissedAt").ValueKind.Should().Be(JsonValueKind.String);
    (await bob.PostJsonAsync($"/api/insights/{hrvId}/dismiss", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await (await ada.GetAsync("/api/insights")).ReadJsonAsync()).GetArrayLength().Should().Be(kinds.Count - 1);
    (await (await ada.GetAsync("/api/insights?includeDismissed=true")).ReadJsonAsync()).GetArrayLength().Should().Be(kinds.Count);
    var afterDismiss = (await (await ada.GetAsync("/api/today")).ReadJsonAsync()).GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("kind").GetString()).ToList();
    afterDismiss.Should().NotContain("hrv_low_run");
    afterDismiss.Should().Contain("sleep_debt");

    // Trends: the metric series with the band, and the list of metrics with enough history.
    var trend = await ada.GetAsync("/api/trends/resting_hr_bpm");
    trend.StatusCode.Should().Be(HttpStatusCode.OK, await trend.Content.ReadAsStringAsync());
    var series = await trend.ReadJsonAsync();
    series.GetProperty("metric").GetString().Should().Be("resting_hr_bpm");
    series.GetProperty("unit").GetString().Should().Be("bpm");
    series.GetProperty("points").GetArrayLength().Should().Be(28);
    series.GetProperty("points")[0].GetProperty("min").GetDouble().Should().Be(50);
    series.GetProperty("baselineDays").GetInt32().Should().Be(28);
    var band = series.GetProperty("band");
    band.GetProperty("median28").GetDouble().Should().Be(56);
    band.GetProperty("p25").GetDouble().Should().BeLessOrEqualTo(band.GetProperty("p75").GetDouble());
    band.GetProperty("p10").GetDouble().Should().BeLessOrEqualTo(band.GetProperty("p90").GetDouble());
    (await (await ada.GetAsync("/api/trends/resting_hr_bpm?days=90")).ReadJsonAsync()).GetProperty("points").GetArrayLength().Should().Be(29, "28 seeded days plus last night");
    (await ada.GetAsync("/api/trends/resting_hr_bpm?days=45")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.GetAsync("/api/trends/not_a_metric")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    var sleepTrend = await (await ada.GetAsync("/api/trends/sleep_stage")).ReadJsonAsync();
    sleepTrend.GetProperty("unit").GetString().Should().Be("min");
    sleepTrend.GetProperty("points").GetArrayLength().Should().Be(28, "27 nights in the window plus last night");

    var list = await (await ada.GetAsync("/api/trends")).ReadJsonAsync();
    var byMetric = list.EnumerateArray().ToDictionary(m => m.GetProperty("metric").GetString()!);
    byMetric.Keys.Should().BeEquivalentTo(new[] { "hrv_sdnn_ms", "resting_hr_bpm", "sleep_stage", "steps" });
    byMetric["hrv_sdnn_ms"].GetProperty("band").GetString().Should().Be("below");
    byMetric["hrv_sdnn_ms"].GetProperty("latestValue").GetDouble().Should().Be(30);
    byMetric["steps"].GetProperty("baselineDays").GetInt32().Should().Be(28);
    byMetric["steps"].GetProperty("displayName").GetString().Should().NotBeNullOrEmpty();

    // Bob: nothing until shared; insights for Today and the archive, each metric's own category for trends.
    (await bob.GetAsync($"/api/today?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/insights?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/trends?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/trends/steps?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/today?personId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "activity" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await bob.GetAsync($"/api/today?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "activity is not insights");
    (await bob.GetAsync($"/api/trends/steps?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.OK);
    (await bob.GetAsync($"/api/trends/resting_hr_bpm?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "vitals were not shared");
    (await (await bob.GetAsync($"/api/trends?personId={adaPerson}")).ReadJsonAsync()).EnumerateArray().Select(m => m.GetProperty("metric").GetString()).Should().Equal("steps");

    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "insights" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    var bobToday = await bob.GetAsync($"/api/today?personId={adaPerson}");
    bobToday.StatusCode.Should().Be(HttpStatusCode.OK, await bobToday.Content.ReadAsStringAsync());
    var bobBody = await bobToday.ReadJsonAsync();
    bobBody.GetProperty("sentence").GetString().Should().NotBeNullOrEmpty();
    bobBody.GetProperty("lastCheckin").ValueKind.Should().Be(JsonValueKind.Null, "check-ins were not shared");
    (await (await bob.GetAsync($"/api/insights?personId={adaPerson}")).ReadJsonAsync()).GetArrayLength().Should().BeGreaterThan(0);
    (await bob.GetAsync($"/api/trends/steps?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "insights is not activity");

    // Recompute for someone else: only the admin, only inside the household.
    (await bob.PostJsonAsync("/api/insights/recompute", new { personId = adaPerson })).StatusCode.Should().Be(HttpStatusCode.NotFound, "Bob is a member, not the admin");
    (await ada.PostJsonAsync("/api/insights/recompute", new { personId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    var adminRun = await ada.PostJsonAsync("/api/insights/recompute", new { personId = bobPerson });
    adminRun.StatusCode.Should().Be(HttpStatusCode.OK, await adminRun.Content.ReadAsStringAsync());
    (await adminRun.ReadJsonAsync()).GetProperty("personId").GetGuid().Should().Be(bobPerson);
  }

  [Fact]
  public async Task Recompute_IsRateLimitedToFiveAMinute()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);

    var statuses = new List<HttpStatusCode>();
    for (var i = 0; i < 6; i++) statuses.Add((await ada.PostJsonAsync("/api/insights/recompute", new { })).StatusCode);

    statuses.Take(5).Should().OnlyContain(s => s == HttpStatusCode.OK);
    statuses[5].Should().Be(HttpStatusCode.TooManyRequests);
  }

  [Fact]
  public async Task Today_IsCached_AndABatchDropsTheCache()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, _, _) = await HouseholdAsync(factory);
    await SeedSummariesAsync(factory, adaPerson);
    await ada.PostJsonAsync("/api/insights/recompute", new { });
    (await ada.GetAsync("/api/today")).StatusCode.Should().Be(HttpStatusCode.OK);

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
      (await context.TodayCaches.AsNoTracking().SingleOrDefaultAsync(c => c.PersonId == adaPerson)).Should().NotBeNull();
    }

    var sourceId = (await (await ada.PostJsonAsync("/api/sources", new { kind = "healthkit", deviceName = "iPhone", platform = "ios" })).ReadJsonAsync()).GetProperty("id").GetGuid();
    var batch = new { sourceId, samples = new[] { new { sourceUid = "hr-1", metric = "resting_hr_bpm", start = DateTime.UtcNow.AddHours(-2).ToString("O"), value = 70, unit = "bpm" } } };
    (await ada.PostJsonAsync("/api/ingest/batch", batch)).StatusCode.Should().Be(HttpStatusCode.OK);

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
      (await context.TodayCaches.AsNoTracking().SingleOrDefaultAsync(c => c.PersonId == adaPerson)).Should().BeNull("new data drops the cached sentence");
    }
    (await ada.GetAsync("/api/today")).StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task TodaySentence_ForAViewerWithoutTheCheckinsGrant_NeverMentionsTheMood()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, bobPerson) = await HouseholdAsync(factory);
    await SeedSummariesAsync(factory, adaPerson);
    (await ada.PostJsonAsync("/api/insights/recompute", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
    // With no cards left, the sentence's second line is the last check-in.
    foreach (var card in (await (await ada.GetAsync("/api/insights")).ReadJsonAsync()).EnumerateArray())
      (await ada.PostJsonAsync($"/api/insights/{card.GetProperty("id").GetGuid()}/dismiss", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await ada.PostJsonAsync("/api/checkins", new { mood = 2 })).StatusCode.Should().Be(HttpStatusCode.OK);

    var own = await (await ada.GetAsync("/api/today")).ReadJsonAsync();
    own.GetProperty("sentence").GetString().Should().Contain("Your last check-in was low");

    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "insights" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    var shared = await (await bob.GetAsync($"/api/today?personId={adaPerson}")).ReadJsonAsync();
    shared.GetProperty("sentence").GetString().Should().NotContain("check-in", "Bob may see insights, not check-ins");
    shared.GetProperty("lastCheckin").ValueKind.Should().Be(JsonValueKind.Null);
    (await (await ada.GetAsync("/api/today")).ReadJsonAsync()).GetProperty("sentence").GetString()
      .Should().Contain("Your last check-in was low", "Bob's view does not replace Ada's");

    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "insights", "checkins" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await (await bob.GetAsync($"/api/today?personId={adaPerson}")).ReadJsonAsync()).GetProperty("sentence").GetString()
      .Should().Contain("last check-in was low");
  }
}
