using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SelfKept.IntegrationTests;

public class SystemEndpointsTests : IClassFixture<ApiFactory>
{
  private readonly ApiFactory _factory;

  public SystemEndpointsTests(ApiFactory factory)
  {
    _factory = factory;
  }

  [Fact]
  public async Task Version_ReturnsVersionBrandAndFeatures()
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/api/system/version");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.ReadJsonAsync();
    body.GetProperty("version").GetString().Should().Be("0.1.0");
    body.GetProperty("minClientVersion").GetString().Should().NotBeNullOrEmpty();
    var brand = body.GetProperty("brand");
    brand.GetProperty("productName").GetString().Should().Be("SelfKept");
    brand.GetProperty("shortName").GetString().Should().NotBeNullOrEmpty();
    brand.GetProperty("tagline").GetString().Should().NotBeNullOrEmpty();
    brand.GetProperty("supportUrl").GetString().Should().StartWith("https://");
    brand.GetProperty("privacyUrl").GetString().Should().StartWith("https://");
    body.GetProperty("features").ValueKind.Should().Be(JsonValueKind.Array);
  }

  [Fact]
  public async Task SetupStatus_OnFreshServer_NeedsFirstUser()
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/api/system/setup-status");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.ReadJsonAsync();
    body.GetProperty("needsFirstUser").GetBoolean().Should().BeTrue();
    body.GetProperty("registrationOpen").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task Health_ReturnsOkWhenDatabaseAnswers()
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/api/system/health");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.ReadJsonAsync();
    body.GetProperty("status").GetString().Should().Be("ok");
    body.GetProperty("database").GetString().Should().Be("ok");
  }

  [Fact]
  public async Task UnknownApiPath_IsProblemJson404_NotTheAppShell()
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/api/does-not-exist");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
  }

  [Fact]
  public async Task NonApiPath_ServesTheWebShell()
  {
    var client = _factory.CreateClient();

    var root = await client.GetAsync("/");
    var deepLink = await client.GetAsync("/some/client/route");

    root.StatusCode.Should().Be(HttpStatusCode.OK);
    root.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    (await root.Content.ReadAsStringAsync()).Should().Contain("web client not built yet");
    deepLink.StatusCode.Should().Be(HttpStatusCode.OK);
    deepLink.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
  }

  [Fact]
  public async Task ProtectedEndpoint_WithoutToken_Is401ProblemJson()
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/api/me/profile");

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
  }

  [Fact]
  public async Task ValidationFailure_Is400ProblemJson()
  {
    var client = _factory.CreateClient();

    var response = await client.PostJsonAsync("/api/auth/register", new { email = "not-an-email", password = "short", displayName = "" });

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    var body = await response.ReadJsonAsync();
    body.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain(new[] { "email", "password", "displayName" });
  }
}
