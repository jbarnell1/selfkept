using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Data;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// The context package over HTTP: log a context event, interpret a seeded person, then read
/// /api/context, /api/patterns, /api/systems, /api/review/weekly and the read-backs as the person
/// and as a household member with no grant (404) and with only the insights grant (sections left out).
/// </summary>
public class ContextFlowTests
{
  private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
  private static readonly DateOnly Yesterday = Today.AddDays(-1);

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

  /// <summary>28 days of resting heart rate and sleep ending yesterday; the last two nights' heart rate well up.</summary>
  private static async Task SeedAsync(ApiFactory factory, Guid personId)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
    var now = DateTime.UtcNow;
    for (var i = 0; i < 28; i++)
    {
      var day = Yesterday.AddDays(-(27 - i));
      context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = MetricKeys.RestingHrBpm, Day = day, Value = i >= 26 ? 70 : 55 + i % 3, Count = 1, ComputedAt = now });
      context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = MetricKeys.SleepStage, Day = day, Value = 450 + i % 4 * 5, Count = 4, ComputedAt = now });
    }
    await context.SaveChangesAsync();
  }

  [Fact]
  public async Task Endpoints_WithoutABearer_Are401()
  {
    await using var factory = new ApiFactory();
    var anon = factory.CreateClient();

    foreach (var path in new[] { "/api/context", "/api/patterns", "/api/systems", "/api/systems/heart", "/api/review/weekly", "/api/sleep", "/api/workouts" })
    {
      (await anon.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
    }
    (await anon.PostJsonAsync("/api/context", new { kind = "illness" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Context_Interpret_Patterns_Systems_Review_EndToEnd_WithGrants()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, bobPerson) = await HouseholdAsync(factory);
    await SeedAsync(factory, adaPerson);

    // Log feeling unwell from the day before yesterday, with a note.
    var created = await ada.PostJsonAsync("/api/context", new { kind = "illness", startedOn = Yesterday.AddDays(-1), note = "Streaming cold" });
    created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
    var ev = await created.ReadJsonAsync();
    var eventId = ev.GetProperty("id").GetGuid();
    ev.GetProperty("source").GetString().Should().Be("user");
    ev.GetProperty("note").GetString().Should().Be("Streaming cold");
    ev.GetProperty("active").GetBoolean().Should().BeTrue();

    (await ada.PostJsonAsync("/api/context", new { kind = "flu" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Interpret yesterday: resting heart rate is up, and the logged illness explains it.
    var recompute = await ada.PostJsonAsync("/api/insights/recompute", new { });
    recompute.StatusCode.Should().Be(HttpStatusCode.OK, await recompute.Content.ReadAsStringAsync());
    var insights = await (await ada.GetAsync("/api/insights")).ReadJsonAsync();
    var rhr = insights.EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "rhr_elevated");
    rhr.GetProperty("severity").GetString().Should().Be("notice", "the logged illness steps it down from watch");
    rhr.GetProperty("text").GetString().Should().Contain("You logged feeling unwell on");
    rhr.GetProperty("evidence").GetProperty("context").GetProperty("kind").GetString().Should().Be("illness");

    // Ada reads everything.
    var list = await (await ada.GetAsync("/api/context")).ReadJsonAsync();
    list.GetProperty("events").GetArrayLength().Should().Be(1);
    list.GetProperty("active").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).Should().Contain("illness");
    var patterns = await ada.GetAsync("/api/patterns");
    patterns.StatusCode.Should().Be(HttpStatusCode.OK, await patterns.Content.ReadAsStringAsync());
    (await patterns.ReadJsonAsync()).GetProperty("note").GetString().Should().Be("A pattern in your own data, not proof.");
    var systems = await (await ada.GetAsync("/api/systems")).ReadJsonAsync();
    var heart = systems.GetProperty("systems").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "heart");
    heart.GetProperty("readings").EnumerateArray().Select(r => r.GetProperty("metric").GetString()).Should().Contain("resting_hr_bpm");
    (await ada.GetAsync("/api/systems/heart")).StatusCode.Should().Be(HttpStatusCode.OK);
    (await ada.GetAsync("/api/systems/kidneys")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    var review = await ada.GetAsync("/api/review/weekly");
    review.StatusCode.Should().Be(HttpStatusCode.OK, await review.Content.ReadAsStringAsync());
    (await review.ReadJsonAsync()).GetProperty("sleep").ValueKind.Should().Be(JsonValueKind.Object);
    (await ada.GetAsync("/api/sleep")).StatusCode.Should().Be(HttpStatusCode.OK);
    (await ada.GetAsync("/api/workouts")).StatusCode.Should().Be(HttpStatusCode.OK);

    // Bob has no grant: every read is a 404 that says nothing about why.
    foreach (var path in new[] { "/api/context", "/api/patterns", "/api/systems", "/api/systems/heart", "/api/review/weekly", "/api/sleep", "/api/workouts" })
    {
      var denied = await bob.GetAsync($"{path}?personId={adaPerson}");
      denied.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
    }
    (await bob.PatchAsync($"/api/context/{eventId}", JsonContent(new { endedOn = Yesterday }))).StatusCode.Should().Be(HttpStatusCode.NotFound, "writes are the caller's own");
    (await bob.DeleteAsync($"/api/context/{eventId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // With only the insights grant Bob sees context without the note, patterns, and the review's insight sections.
    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "insights" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    var shared = await (await bob.GetAsync($"/api/context?personId={adaPerson}")).ReadJsonAsync();
    shared.GetProperty("events")[0].GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null, "the note stays with its owner");
    (await bob.GetAsync($"/api/patterns?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.OK);
    var bobReview = await (await bob.GetAsync($"/api/review/weekly?personId={adaPerson}")).ReadJsonAsync();
    bobReview.GetProperty("sleep").ValueKind.Should().Be(JsonValueKind.Null, "sleep was not shared");
    bobReview.GetProperty("targets").ValueKind.Should().Be(JsonValueKind.Null, "body was not shared");
    bobReview.GetProperty("insights").ValueKind.Should().Be(JsonValueKind.Array);
    var bobSystems = await (await bob.GetAsync($"/api/systems?personId={adaPerson}")).ReadJsonAsync();
    bobSystems.GetProperty("labs").ValueKind.Should().Be(JsonValueKind.Null);
    foreach (var system in bobSystems.GetProperty("systems").EnumerateArray())
    {
      system.GetProperty("readings").GetArrayLength().Should().Be(0, "no reading category was shared");
    }
    (await bob.GetAsync($"/api/sleep?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // Ada ends the event, then removes it.
    var patched = await ada.PatchAsync($"/api/context/{eventId}", JsonContent(new { endedOn = Yesterday }));
    patched.StatusCode.Should().Be(HttpStatusCode.OK, await patched.Content.ReadAsStringAsync());
    (await patched.ReadJsonAsync()).GetProperty("endedOn").GetString().Should().Be(Yesterday.ToString("yyyy-MM-dd"));
    (await ada.DeleteAsync($"/api/context/{eventId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await (await ada.GetAsync("/api/context")).ReadJsonAsync()).GetProperty("events").GetArrayLength().Should().Be(0);
  }

  private static StringContent JsonContent<T>(T body) =>
    new(JsonSerializer.Serialize(body, ApiFactory.Json), System.Text.Encoding.UTF8, "application/json");
}
