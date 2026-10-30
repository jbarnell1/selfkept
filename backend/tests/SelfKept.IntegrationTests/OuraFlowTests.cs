using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Oura;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Oura end to end with Oura itself replaced by a fake: connect, the browser's return to the
/// callback, the first pull through the ingest path, a re-pull that changes nothing, a revoked
/// grant, and disconnect.
/// </summary>
public class OuraFlowTests
{
  private sealed class FakeOuraApi : IOuraApi
  {
    public ConcurrentQueue<string> Calls { get; } = new();
    public bool RevokeAccess { get; set; }
    public string? LastVerifier { get; private set; }
    public OuraApp? LastApp { get; private set; }
    public int Refreshes { get; private set; }

    public Task<OuraTokens> ExchangeCodeAsync(OuraApp app, string code, string codeVerifier, CancellationToken ct)
    {
      Calls.Enqueue($"exchange:{code}");
      LastApp = app;
      LastVerifier = codeVerifier;
      if (code != "good-code") throw new OuraUnauthorizedException("bad code");
      return Task.FromResult(new OuraTokens("access-1", "refresh-1", 86400, "daily heartrate spo2"));
    }

    public Task<OuraTokens> RefreshAsync(OuraApp app, string refreshToken, CancellationToken ct)
    {
      Refreshes++;
      return Task.FromResult(new OuraTokens($"access-r{Refreshes}", $"refresh-r{Refreshes}", 86400, null));
    }

    public Task RevokeAsync(string accessToken, CancellationToken ct)
    {
      Calls.Enqueue($"revoke:{accessToken}");
      return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JsonElement>> ListAsync(string accessToken, string collection, DateOnly start, DateOnly end, CancellationToken ct)
    {
      Calls.Enqueue($"list:{collection}:{start:yyyy-MM-dd}");
      if (RevokeAccess) throw new OuraUnauthorizedException("revoked");
      var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
      IReadOnlyList<JsonElement> docs = collection switch
      {
        "daily_readiness" => [Json($$"""{"id":"r-{{day}}","day":"{{day}}","score":81,"temperature_deviation":-0.12,"contributors":{},"timestamp":"x"}""")],
        "daily_sleep" => [Json($$"""{"id":"s-{{day}}","day":"{{day}}","score":77,"contributors":{},"timestamp":"x"}""")],
        "daily_resilience" => [Json($$$"""{"id":"z-{{{day}}}","day":"{{{day}}}","level":"solid","contributors":{"sleep_recovery":1,"daytime_recovery":1,"stress":1}}""")],
        _ => [],
      };
      return Task.FromResult(docs);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
  }

  /// <summary>
  /// The app with Oura faked. <paramref name="fromEnvironment"/> false leaves the environment
  /// empty, as on a fresh install, so the admin has to enter the application in the app.
  /// </summary>
  private static (WebApplicationFactory<Program> Factory, FakeOuraApi Api) Create(ApiFactory baseFactory, bool fromEnvironment = true)
  {
    var api = new FakeOuraApi();
    var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      if (fromEnvironment)
      {
        builder.UseSetting("Oura:ClientId", "client-123");
        builder.UseSetting("Oura:ClientSecret", "secret-456");
      }
      builder.ConfigureServices(services =>
      {
        services.RemoveAll<IOuraApi>();
        services.AddScoped<IOuraApi>(_ => api);
      });
    });
    return (factory, api);
  }

  private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> factory, string? inviteCode = null)
  {
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    var response = await client.PostJsonAsync("/api/auth/register", new { email = $"ada-{Guid.NewGuid():N}@example.com", password = "correct horse battery staple", displayName = "Ada", inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    client.UseBearer((await response.ReadJsonAsync()).GetProperty("accessToken").GetString()!);
    return client;
  }

  private static async Task<HttpClient> MemberAsync(WebApplicationFactory<Program> factory, HttpClient admin)
  {
    var invite = await admin.PostJsonAsync("/api/household/invites", new { });
    invite.StatusCode.Should().Be(HttpStatusCode.OK, await invite.Content.ReadAsStringAsync());
    return await SignedInAsync(factory, (await invite.ReadJsonAsync()).GetProperty("code").GetString());
  }

  private static string QueryValue(string url, string name) =>
    Uri.UnescapeDataString(new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).First(p => p[0] == name)[1]);

  private static async Task<string> ConnectAsync(HttpClient client, string clientId = "client-123", string? returnTo = null)
  {
    var start = await client.PostAsync(returnTo == null ? "/api/oura/connect" : $"/api/oura/connect?returnTo={Uri.EscapeDataString(returnTo)}", null);
    start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
    var url = (await start.ReadJsonAsync()).GetProperty("authorizeUrl").GetString()!;
    url.Should().StartWith("https://cloud.ouraring.com/oauth/authorize?");
    QueryValue(url, "redirect_uri").Should().Be("http://localhost/api/oura/callback");
    QueryValue(url, "code_challenge_method").Should().Be("S256");
    QueryValue(url, "client_id").Should().Be(clientId);
    return QueryValue(url, "state");
  }

  private static async Task PullDueAsync(WebApplicationFactory<Program> factory)
  {
    using var scope = factory.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IOuraService>().PullDueAsync();
  }

  [Fact]
  public async Task Status_WithoutCredentials_IsNotConfigured_AndConnectIs404()
  {
    await using var factory = new ApiFactory();
    var client = await SignedInAsync(factory);

    var status = await (await client.GetAsync("/api/oura")).ReadJsonAsync();
    status.GetProperty("configured").GetBoolean().Should().BeFalse();
    (await client.PostAsync("/api/oura/connect", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Connect_Callback_Pull_RePull_Disconnect()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, api) = Create(baseFactory);
    var client = await SignedInAsync(factory);
    var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    var state = await ConnectAsync(client);
    var back = await browser.GetAsync($"/api/oura/callback?code=good-code&state={state}");
    back.StatusCode.Should().Be(HttpStatusCode.Redirect);
    back.Headers.Location!.ToString().Should().Be("http://localhost/sources?oura=connected");
    api.LastVerifier.Should().NotBeNullOrEmpty();

    var replay = await browser.GetAsync($"/api/oura/callback?code=good-code&state={state}");
    replay.Headers.Location!.ToString().Should().EndWith("oura=error", "a state is single use");

    var connected = await (await client.GetAsync("/api/oura")).ReadJsonAsync();
    connected.GetProperty("connected").GetBoolean().Should().BeTrue();
    connected.GetProperty("lastPullAt").ValueKind.Should().Be(JsonValueKind.Null);

    await PullDueAsync(factory);
    api.Calls.Should().Contain(c => c.StartsWith("list:daily_readiness:") && c.EndsWith(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-90).ToString("yyyy-MM-dd")),
      "the first pull reaches back 90 days");
    var afterPull = await (await client.GetAsync("/api/oura")).ReadJsonAsync();
    afterPull.GetProperty("sampleCount").GetInt64().Should().Be(4, "readiness score, temperature, sleep score and resilience");
    afterPull.GetProperty("lastError").ValueKind.Should().Be(JsonValueKind.Null);

    var readiness = await (await client.GetAsync("/api/data/daily?metric=oura_readiness")).ReadJsonAsync();
    readiness.GetProperty("days")[0].GetProperty("value").GetDouble().Should().Be(81);

    (await client.PostAsync("/api/oura/sync", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
    await PullDueAsync(factory);
    (await (await client.GetAsync("/api/oura")).ReadJsonAsync()).GetProperty("sampleCount").GetInt64().Should().Be(4, "a re-pull of the same days changes nothing");

    (await client.DeleteAsync("/api/oura")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    api.Calls.Should().Contain("revoke:access-1");
    var gone = await (await client.GetAsync("/api/oura")).ReadJsonAsync();
    gone.GetProperty("connected").GetBoolean().Should().BeFalse();
    (await (await client.GetAsync("/api/data/daily?metric=oura_readiness")).ReadJsonAsync()).GetProperty("days").GetArrayLength().Should().Be(1, "readings already brought in stay");
  }

  [Fact]
  public async Task Callback_WithABadCode_OrAnUnknownState_GoesBackWithAnError()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, _) = Create(baseFactory);
    var client = await SignedInAsync(factory);
    var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    var state = await ConnectAsync(client);
    (await browser.GetAsync($"/api/oura/callback?code=bad-code&state={state}")).Headers.Location!.ToString().Should().EndWith("oura=error");
    (await browser.GetAsync("/api/oura/callback?code=good-code&state=made-up")).Headers.Location!.ToString().Should().EndWith("oura=error");
    (await browser.GetAsync($"/api/oura/callback?error=access_denied&state={await ConnectAsync(client)}")).Headers.Location!.ToString().Should().EndWith("oura=error");
    (await (await client.GetAsync("/api/oura")).ReadJsonAsync()).GetProperty("connected").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task RevokedGrant_StopsPolling_AndSaysSo()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, api) = Create(baseFactory);
    var client = await SignedInAsync(factory);
    var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    await browser.GetAsync($"/api/oura/callback?code=good-code&state={await ConnectAsync(client)}");

    api.RevokeAccess = true;
    await PullDueAsync(factory);

    var status = await (await client.GetAsync("/api/oura")).ReadJsonAsync();
    status.GetProperty("connected").GetBoolean().Should().BeTrue();
    status.GetProperty("lastError").GetString().Should().Contain("Connect Oura again");

    var calls = api.Calls.Count;
    await PullDueAsync(factory);
    api.Calls.Count.Should().Be(calls, "a revoked grant is not pulled again until the person reconnects");
  }

  [Fact]
  public async Task AdminEntersTheApplicationInTheApp_ThenConnectsAndComesBackToSetup()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, api) = Create(baseFactory, fromEnvironment: false);
    var admin = await SignedInAsync(factory);
    var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    var empty = await (await admin.GetAsync("/api/integrations/oura")).ReadJsonAsync();
    empty.GetProperty("configured").GetBoolean().Should().BeFalse();
    empty.GetProperty("source").GetString().Should().Be("none");
    empty.GetProperty("redirectUri").GetString().Should().Be("http://localhost/api/oura/callback", "the owner pastes this into Oura's form");
    empty.GetProperty("developerUrl").GetString().Should().StartWith("https://cloud.ouraring.com/");
    (await admin.PostAsync("/api/oura/connect", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

    var saved = await admin.PutJsonAsync("/api/integrations/oura", new { clientId = "  app-777 ", clientSecret = "shh-888" });
    saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    var body = await saved.Content.ReadAsStringAsync();
    body.Should().NotContain("shh-888", "the secret never comes back");
    var app = JsonDocument.Parse(body).RootElement;
    app.GetProperty("configured").GetBoolean().Should().BeTrue();
    app.GetProperty("source").GetString().Should().Be("app");
    app.GetProperty("clientId").GetString().Should().Be("app-777");
    (await (await admin.GetAsync("/api/oura")).ReadJsonAsync()).GetProperty("configured").GetBoolean().Should().BeTrue();

    var state = await ConnectAsync(admin, clientId: "app-777", returnTo: "/welcome/steps");
    var back = await browser.GetAsync($"/api/oura/callback?code=good-code&state={state}");
    back.Headers.Location!.ToString().Should().Be("http://localhost/welcome/steps?oura=connected");
    api.LastApp.Should().Be(new OuraApp("app-777", "shh-888"), "the code is exchanged with the saved application");

    var cleared = await (await admin.DeleteAsync("/api/integrations/oura")).ReadJsonAsync();
    cleared.GetProperty("configured").GetBoolean().Should().BeFalse();
    (await (await admin.GetAsync("/api/oura")).ReadJsonAsync()).GetProperty("configured").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task OnlyTheAdmin_SeesOrChangesTheApplication_AndAPasteWithSpacesIsRefused()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, _) = Create(baseFactory, fromEnvironment: false);
    var admin = await SignedInAsync(factory);
    var member = await MemberAsync(factory, admin);

    (await member.GetAsync("/api/integrations/oura")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await member.PutJsonAsync("/api/integrations/oura", new { clientId = "a", clientSecret = "b" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await member.DeleteAsync("/api/integrations/oura")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    (await admin.PutJsonAsync("/api/integrations/oura", new { clientId = "app 777", clientSecret = "b" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await admin.PutJsonAsync("/api/integrations/oura", new { clientId = "app", clientSecret = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task TheEnvironmentWins_AndCannotBeChangedFromTheApp()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, _) = Create(baseFactory);
    var admin = await SignedInAsync(factory);

    var app = await (await admin.GetAsync("/api/integrations/oura")).ReadJsonAsync();
    app.GetProperty("source").GetString().Should().Be("environment");
    app.GetProperty("clientId").GetString().Should().Be("client-123");
    (await admin.PutJsonAsync("/api/integrations/oura", new { clientId = "x", clientSecret = "y" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await admin.DeleteAsync("/api/integrations/oura")).StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  [Fact]
  public async Task AReturnPathOutsideTheList_FallsBackToSources()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, _) = Create(baseFactory);
    var client = await SignedInAsync(factory);
    var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    var state = await ConnectAsync(client, returnTo: "https://elsewhere.example/steal");
    (await browser.GetAsync($"/api/oura/callback?code=good-code&state={state}")).Headers.Location!.ToString()
      .Should().Be("http://localhost/sources?oura=connected");
  }
}
