using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>The privacy bundle redacts by default and carries only the caller's own data.</summary>
public class ExportFlowTests
{
  private static async Task<(HttpClient Ada, HttpClient Bob, Guid BobPerson)> HouseholdAsync(ApiFactory factory)
  {
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var ada = factory.CreateClient();
    await RegisterAsync(ada, $"ada-{suffix}@example.com", "Ada Lovelace");
    var invite = await (await ada.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    var bob = factory.CreateClient();
    var bobAuth = await RegisterAsync(bob, $"bob-{suffix}@example.com", "Bob Byron", invite.GetProperty("code").GetString());
    return (ada, bob, bobAuth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid());
  }

  private static async Task<JsonElement> RegisterAsync(HttpClient client, string email, string name, string? inviteCode = null)
  {
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = name, inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var auth = await response.ReadJsonAsync();
    client.UseBearer(auth.GetProperty("accessToken").GetString()!);
    return auth;
  }

  private static async Task SeedAsync(HttpClient client, string note)
  {
    (await client.PutJsonAsync("/api/me/profile", new { displayName = "Ada Lovelace", sexAtBirth = "female", birthDate = "1990-06-15", units = "metric", timeZone = "UTC" }))
      .StatusCode.Should().Be(HttpStatusCode.OK);
    var source = await (await client.PostJsonAsync("/api/sources", new { kind = "healthkit", deviceName = "Phone", platform = "ios", appVersion = "0.1.0" })).ReadJsonAsync();
    var day = DateTime.UtcNow.Date.AddDays(-3);
    var batch = await client.PostJsonAsync("/api/ingest/batch", new
    {
      sourceId = source.GetProperty("id").GetGuid(),
      samples = new object[]
      {
        new { sourceUid = "s1", metric = "steps", start = day.AddHours(9), end = day.AddHours(10), value = 6123, unit = "count" },
        new { sourceUid = "r1", metric = "resting_hr_bpm", start = day.AddHours(12), value = 54, unit = "bpm" },
      },
    });
    batch.StatusCode.Should().Be(HttpStatusCode.OK, await batch.Content.ReadAsStringAsync());
    (await client.PostJsonAsync("/api/checkins", new { mood = 2, note })).StatusCode.Should().Be(HttpStatusCode.OK);
  }

  private static async Task<JsonElement> BundleAsync(HttpClient client, object body)
  {
    var response = await client.PostJsonAsync("/api/export/bundle", body);
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return await response.ReadJsonAsync();
  }

  [Fact]
  public async Task Bundle_ByDefault_LeavesOutTheNameDatesAndNotes()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _) = await HouseholdAsync(factory);
    await SeedAsync(ada, "Headache after the Smith family dinner");

    var bundle = await BundleAsync(ada, new { days = 30, keepDates = false, includeNotes = false, includeName = false, export = false });
    var text = bundle.GetProperty("text").GetString()!;

    text.Should().NotContain("Ada").And.NotContain("Lovelace");
    text.Should().NotContain("1990").And.NotContain(DateTime.UtcNow.Year + "-", "dates are counted back from today");
    text.Should().Contain("day -3").And.Contain("6123").And.Contain("Age: 30 to 39").And.Contain("Sex at birth: female");
    text.Should().NotContain("Headache", "notes stay out unless the person turns them on");
    text.Should().Contain("| day 0 | 2 |", "the mood still goes in");
    bundle.GetProperty("included").EnumerateArray().Select(x => x.GetString()).Should().Contain(["activity", "vitals", "checkins"]);
    bundle.GetProperty("approximateTokens").GetInt32().Should().BeGreaterThan(0);
  }

  [Fact]
  public async Task Bundle_WithEverythingOn_HasTheNameRealDatesAndNotes()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _) = await HouseholdAsync(factory);
    await SeedAsync(ada, "Slept badly | felt off");

    var text = (await BundleAsync(ada, new { days = 30, keepDates = true, includeNotes = true, includeName = true, export = true })).GetProperty("text").GetString()!;

    text.Should().Contain("Name: Ada Lovelace");
    text.Should().Contain(DateTime.UtcNow.Date.AddDays(-3).ToString("yyyy-MM-dd"));
    text.Should().Contain("Slept badly / felt off", "a pipe in a note cannot break the table");
  }

  [Fact]
  public async Task Bundle_OnlyTheChosenCategories_AndLongWindowsGoWeekly()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _) = await HouseholdAsync(factory);
    await SeedAsync(ada, "x");

    var bundle = await BundleAsync(ada, new { categories = new[] { "activity" }, days = 90, keepDates = false, includeNotes = false, includeName = false, export = false });
    var text = bundle.GetProperty("text").GetString()!;

    bundle.GetProperty("included").EnumerateArray().Select(x => x.GetString()).Should().Equal("activity");
    text.Should().Contain("| Week starting | Median |").And.NotContain("Resting heart rate").And.NotContain("Mood check-ins");
  }

  [Fact]
  public async Task Bundle_NeverIncludesAMembersData_EvenWhenShared()
  {
    await using var factory = new ApiFactory();
    var (ada, bob, bobPerson) = await HouseholdAsync(factory);
    await SeedAsync(ada, "Ada's private note");
    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "activity", "checkins" } })).StatusCode.Should().Be(HttpStatusCode.OK);

    var bobs = (await BundleAsync(bob, new { days = 30, keepDates = false, includeNotes = true, includeName = false, export = false })).GetProperty("text").GetString()!;

    bobs.Should().NotContain("6123").And.NotContain("private note");
    bobs.Should().Contain("No data in these categories");
  }

  [Fact]
  public async Task FullExport_IsAJsonAttachmentOfTheCallersData()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _) = await HouseholdAsync(factory);
    await SeedAsync(ada, "a note");

    var response = await ada.GetAsync("/api/export/full");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    response.Content.Headers.ContentDisposition!.ToString().Should().Contain("attachment");
    var body = await response.ReadJsonAsync();
    body.GetProperty("profile").GetProperty("displayName").GetString().Should().Be("Ada Lovelace");
    body.GetProperty("daily").EnumerateArray().Should().Contain(d => d.GetProperty("metric").GetString() == "steps");
    body.GetProperty("checkins")[0].GetProperty("note").GetString().Should().Be("a note");
  }

  [Fact]
  public async Task Export_WithoutSignIn_Is401()
  {
    await using var factory = new ApiFactory();
    (await factory.CreateClient().GetAsync("/api/export/full")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }
}
