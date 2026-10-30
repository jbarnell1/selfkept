using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SelfKept.Api.Diagnostics;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Bug visibility without telemetry: the apps report their errors to this server, the server keeps
/// recent warnings and errors in memory, and only the household admin can read them.
/// </summary>
public class DiagnosticsFlowTests
{
  private static async Task<HttpClient> RegisterAsync(ApiFactory factory, string email, string? inviteCode = null)
  {
    var client = factory.CreateClient();
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = "Ada", inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    client.UseBearer((await response.ReadJsonAsync()).GetProperty("accessToken").GetString()!);
    return client;
  }

  [Fact]
  public async Task AnAppError_IsLoggedAndShownToTheAdminOnly()
  {
    await using var factory = new ApiFactory();
    var admin = await RegisterAsync(factory, "diag-admin@example.com");
    var invite = await (await admin.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    var member = await RegisterAsync(factory, "diag-member@example.com", invite.GetProperty("code").GetString());

    var anonymous = factory.CreateClient();
    var sent = await anonymous.PostJsonAsync("/api/diagnostics/client-errors", new
    {
      kind = "flutter",
      message = "Null check operator used on a null value",
      stack = "#0 TodayPage.build (package:selfkept/pages/today_page.dart:120)",
      appVersion = "0.1.1",
      platform = "android",
      where = "/",
    });
    sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

    (await member.GetAsync("/api/diagnostics/problems")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    var problems = await (await admin.GetAsync("/api/diagnostics/problems")).ReadJsonAsync();
    problems.GetProperty("last24Hours").GetInt32().Should().BeGreaterThan(0);
    var entry = problems.GetProperty("items").EnumerateArray()
      .First(i => i.GetProperty("message").GetString()!.Contains("Null check operator"));
    entry.GetProperty("level").GetString().Should().Be("warning");
    entry.GetProperty("message").GetString().Should().Contain("android").And.Contain("0.1.1").And.Contain("signed out");
    entry.GetProperty("detail").GetString().Should().Contain("today_page.dart:120");
  }

  [Fact]
  public async Task AReportThatIsTooLongOrIncomplete_IsRefused()
  {
    await using var factory = new ApiFactory();
    var client = factory.CreateClient();
    (await client.PostJsonAsync("/api/diagnostics/client-errors", new { kind = "flutter", message = new string('x', 501) }))
      .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await client.PostJsonAsync("/api/diagnostics/client-errors", new { kind = "flutter" }))
      .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await factory.CreateClient().GetAsync("/api/diagnostics/problems")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public void RecentProblems_KeepsTheNewestWithinItsCapacity()
  {
    var problems = new RecentProblems();
    for (var i = 0; i < RecentProblems.Capacity + 25; i++)
      problems.Add(new ProblemEntry(DateTime.UtcNow, "warning", "Test", $"problem {i}", null));
    var snapshot = problems.Snapshot();
    snapshot.Should().HaveCount(RecentProblems.Capacity);
    snapshot[0].Message.Should().Be($"problem {RecentProblems.Capacity + 24}", "newest first");
    snapshot[^1].Message.Should().Be("problem 25");
  }
}
