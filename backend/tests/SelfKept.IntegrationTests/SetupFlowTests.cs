using System.Net;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// First-run setup (docs/setup-design.md): a new account starts unfinished with no answers, saves
/// its answers, finishes, and a later save never reopens it. The Postgres test checks the real
/// column types and that the stored Oura application is encrypted.
/// </summary>
public class SetupFlowTests
{
  private static async Task<(HttpClient Client, JsonElement User)> RegisterAsync(ApiFactory factory, string email)
  {
    var client = factory.CreateClient();
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = "Ada" });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var auth = await response.ReadJsonAsync();
    client.UseBearer(auth.GetProperty("accessToken").GetString()!);
    return (client, auth.GetProperty("user"));
  }

  [Fact]
  public async Task ANewAccount_SavesItsAnswers_Finishes_AndStaysFinished()
  {
    await using var factory = new ApiFactory();
    var (client, user) = await RegisterAsync(factory, "setup@example.com");
    user.GetProperty("setupCompletedAt").ValueKind.Should().Be(JsonValueKind.Null);
    user.GetProperty("setupPlan").GetArrayLength().Should().Be(0);

    var answers = await client.PutJsonAsync("/api/me/setup", new { plan = new[] { "healthConnect", "oura", "oura" }, completed = false });
    answers.StatusCode.Should().Be(HttpStatusCode.OK, await answers.Content.ReadAsStringAsync());
    var saved = await answers.ReadJsonAsync();
    saved.GetProperty("setupPlan").EnumerateArray().Select(e => e.GetString()).Should().Equal("healthConnect", "oura");
    saved.GetProperty("setupCompletedAt").ValueKind.Should().Be(JsonValueKind.Null);

    var done = await (await client.PutJsonAsync("/api/me/setup", new { plan = new[] { "healthConnect", "oura", "labs" }, completed = true })).ReadJsonAsync();
    done.GetProperty("setupCompletedAt").ValueKind.Should().Be(JsonValueKind.String);
    var finishedAt = done.GetProperty("setupCompletedAt").GetDateTime();

    var again = await (await client.PutJsonAsync("/api/me/setup", new { plan = new[] { "labs" }, completed = false })).ReadJsonAsync();
    again.GetProperty("setupCompletedAt").GetDateTime().Should().BeCloseTo(finishedAt, TimeSpan.FromMilliseconds(1), "saving answers later never reopens setup");

    // A fresh sign-in carries the same state.
    var login = await factory.CreateClient().PostJsonAsync("/api/auth/login", new { email = "setup@example.com", password = "correct horse battery staple" });
    var fresh = (await login.ReadJsonAsync()).GetProperty("user");
    fresh.GetProperty("setupPlan").EnumerateArray().Select(e => e.GetString()).Should().Equal("labs");
    fresh.GetProperty("setupCompletedAt").GetDateTime().Should().BeCloseTo(finishedAt, TimeSpan.FromMilliseconds(1));
  }

  [Fact]
  public async Task AnUnknownAnswer_IsRefused()
  {
    await using var factory = new ApiFactory();
    var (client, _) = await RegisterAsync(factory, "setup-bad@example.com");
    (await client.PutJsonAsync("/api/me/setup", new { plan = new[] { "fitbitCloud" }, completed = false })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await factory.CreateClient().PutJsonAsync("/api/me/setup", new { plan = Array.Empty<string>(), completed = true })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [PostgresFact]
  public async Task OnPostgres_TheAnswersRoundTrip_AndTheOuraApplicationIsStoredEncrypted()
  {
    await using var factory = new ApiFactory();
    factory.UsesPostgres.Should().BeTrue();
    var (client, _) = await RegisterAsync(factory, "pg-setup@example.com");

    var done = await (await client.PutJsonAsync("/api/me/setup", new { plan = new[] { "appleHealth", "assistant" }, completed = true })).ReadJsonAsync();
    done.GetProperty("setupPlan").EnumerateArray().Select(e => e.GetString()).Should().Equal("appleHealth", "assistant");

    (await client.PutJsonAsync("/api/integrations/oura", new { clientId = "pg-app-1", clientSecret = "pg-secret-2" })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await (await client.GetAsync("/api/integrations/oura")).ReadJsonAsync()).GetProperty("clientId").GetString().Should().Be("pg-app-1");

    await using var connection = new NpgsqlConnection(factory.ScratchConnection);
    await connection.OpenAsync();
    await using (var plan = new NpgsqlCommand("SELECT setup_plan, setup_completed_at IS NOT NULL FROM user_account WHERE email = 'pg-setup@example.com'", connection))
    {
      await using var reader = await plan.ExecuteReaderAsync();
      (await reader.ReadAsync()).Should().BeTrue();
      reader.GetFieldValue<string[]>(0).Should().Equal("AppleHealth", "Assistant");
      reader.GetBoolean(1).Should().BeTrue();
    }
    await using (var settings = new NpgsqlCommand("SELECT key, value FROM server_setting ORDER BY key", connection))
    {
      var rows = new List<(string Key, string Value)>();
      await using var reader = await settings.ExecuteReaderAsync();
      while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1)));
      rows.Select(r => r.Key).Should().Equal("oura.client_id", "oura.client_secret");
      rows.Should().OnlyContain(r => r.Value.StartsWith("v2:"), "every stored value is encrypted");
      rows.Should().NotContain(r => r.Value.Contains("pg-secret-2") || r.Value.Contains("pg-app-1"));
    }
  }
}
