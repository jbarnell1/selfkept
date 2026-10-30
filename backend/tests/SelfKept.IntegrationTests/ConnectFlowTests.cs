using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Package 5 (Connect) over HTTP: discovery documents, dynamic registration, the OAuth 2.1 code
/// flow with PKCE and consent, refresh rotation and reuse detection, confidential clients,
/// personal tokens, MCP tool calls with the share grant enforced, memory, and revocation. Each
/// token kind is refused on the other surface.
/// </summary>
public class ConnectFlowTests
{
  private const string Origin = "http://localhost";
  private const string RedirectUri = "https://assistant.example/oauth/callback";

  // --- helpers ---------------------------------------------------------------------------------

  private static async Task<(HttpClient Ada, Guid AdaPerson, HttpClient Bob, Guid BobPerson)> HouseholdAsync(ApiFactory factory)
  {
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var ada = factory.CreateClient();
    var adaAuth = await RegisterAsync(ada, $"ada-{suffix}@example.com", "Ada Lovelace");
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

  private static HttpClient Anonymous(ApiFactory factory) =>
    factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

  private static HttpClient WithBearer(ApiFactory factory, string token)
  {
    var client = Anonymous(factory);
    client.UseBearer(token);
    return client;
  }

  private static (string Verifier, string Challenge) Pkce()
  {
    var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
    var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    return (verifier, challenge);
  }

  private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

  private static async Task<JsonElement> RegisterClientAsync(ApiFactory factory, string method = "none")
  {
    var response = await Anonymous(factory).PostAsJsonAsync("/api/oauth/register", new Dictionary<string, object>
    {
      ["client_name"] = "Open WebUI",
      ["redirect_uris"] = new[] { RedirectUri },
      ["token_endpoint_auth_method"] = method,
      ["grant_types"] = new[] { "authorization_code", "refresh_token" },
      ["response_types"] = new[] { "code" },
    });
    response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    return await response.ReadJsonAsync();
  }

  /// <summary>/authorize then consent as the signed-in person; returns the redirect the browser would follow.</summary>
  private static async Task<Uri> AuthorizeAsync(ApiFactory factory, HttpClient person, string clientId, string challenge, bool approve = true, string state = "xyz")
  {
    var query = $"response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                $"&code_challenge={challenge}&code_challenge_method=S256&state={state}&scope={Uri.EscapeDataString("health:read memory:write")}" +
                $"&resource={Uri.EscapeDataString(Origin + "/mcp")}";
    var authorize = await Anonymous(factory).GetAsync($"/api/oauth/authorize?{query}");
    authorize.StatusCode.Should().Be(HttpStatusCode.Redirect, await authorize.Content.ReadAsStringAsync());
    var consentUrl = authorize.Headers.Location!;
    consentUrl.AbsolutePath.Should().Be("/connect");
    var requestId = consentUrl.Query.Split('=')[1];

    var consent = await (await person.GetAsync($"/api/oauth/request/{requestId}")).ReadJsonAsync();
    consent.GetProperty("clientName").GetString().Should().Be("Open WebUI");
    consent.GetProperty("redirectHost").GetString().Should().Be("assistant.example");

    var decision = await person.PostJsonAsync($"/api/oauth/request/{requestId}/decision", new { approve });
    decision.StatusCode.Should().Be(HttpStatusCode.OK, await decision.Content.ReadAsStringAsync());
    return new Uri((await decision.ReadJsonAsync()).GetProperty("redirectUrl").GetString()!);
  }

  private static string QueryValue(Uri uri, string name) =>
    uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).First(p => p[0] == name)[1] is var v ? Uri.UnescapeDataString(v) : "";

  private static Task<HttpResponseMessage> TokenAsync(ApiFactory factory, Dictionary<string, string> form, AuthenticationHeaderValue? basic = null)
  {
    var client = Anonymous(factory);
    if (basic != null) client.DefaultRequestHeaders.Authorization = basic;
    return client.PostAsync("/api/oauth/token", new FormUrlEncodedContent(form));
  }

  private static async Task<(string Access, string Refresh)> FullFlowAsync(ApiFactory factory, HttpClient person)
  {
    var registered = await RegisterClientAsync(factory);
    var clientId = registered.GetProperty("client_id").GetString()!;
    var (verifier, challenge) = Pkce();
    var redirect = await AuthorizeAsync(factory, person, clientId, challenge);
    var token = await TokenAsync(factory, new()
    {
      ["grant_type"] = "authorization_code",
      ["code"] = QueryValue(redirect, "code"),
      ["redirect_uri"] = RedirectUri,
      ["client_id"] = clientId,
      ["code_verifier"] = verifier,
    });
    token.StatusCode.Should().Be(HttpStatusCode.OK, await token.Content.ReadAsStringAsync());
    var body = await token.ReadJsonAsync();
    return (body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!);
  }

  /// <summary>One JSON-RPC call to /mcp. The SDK may answer as JSON or as one server-sent event.</summary>
  private static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> RpcAsync(HttpClient client, string method, object? parameters = null)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
    {
      Content = JsonContent.Create(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters ?? new { } }),
    };
    request.Headers.Accept.ParseAdd("application/json");
    request.Headers.Accept.ParseAdd("text/event-stream");
    request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
    var response = await client.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
      text = string.Concat(text.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim()));
    var body = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    return (response.StatusCode, body, response);
  }

  private static async Task<(bool IsError, string Text)> CallToolAsync(HttpClient client, string tool, object? arguments = null)
  {
    var (status, body, _) = await RpcAsync(client, "tools/call", new { name = tool, arguments = arguments ?? new { } });
    status.Should().Be(HttpStatusCode.OK, body.ToString());
    var result = body.GetProperty("result");
    var isError = result.TryGetProperty("isError", out var e) && e.GetBoolean();
    var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
    return (isError, text);
  }

  // --- discovery ---------------------------------------------------------------------------------

  [Fact]
  public async Task Discovery_ServesProtectedResourceAndAuthorizationServerMetadata()
  {
    await using var factory = new ApiFactory();
    var client = Anonymous(factory);

    var resource = await (await client.GetAsync("/.well-known/oauth-protected-resource/mcp")).ReadJsonAsync();
    resource.GetProperty("resource").GetString().Should().Be(Origin + "/mcp");
    resource.GetProperty("authorization_servers")[0].GetString().Should().Be(Origin);

    var server = await (await client.GetAsync("/.well-known/oauth-authorization-server")).ReadJsonAsync();
    server.GetProperty("issuer").GetString().Should().Be(Origin);
    server.GetProperty("authorization_endpoint").GetString().Should().Be(Origin + "/api/oauth/authorize");
    server.GetProperty("token_endpoint").GetString().Should().Be(Origin + "/api/oauth/token");
    server.GetProperty("registration_endpoint").GetString().Should().Be(Origin + "/api/oauth/register");
    server.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()).Should().Equal("S256");
  }

  [Fact]
  public async Task Mcp_WithoutAToken_Is401_AndPointsAtTheResourceMetadata()
  {
    await using var factory = new ApiFactory();
    var (status, _, response) = await RpcAsync(Anonymous(factory), "tools/list");

    status.Should().Be(HttpStatusCode.Unauthorized);
    response.Headers.WwwAuthenticate.ToString().Should().Contain($"resource_metadata=\"{Origin}/.well-known/oauth-protected-resource/mcp\"");
  }

  [Fact]
  public async Task Mcp_WithAnAppToken_Is401()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);

    var (status, _, _) = await RpcAsync(ada, "tools/list");

    status.Should().Be(HttpStatusCode.Unauthorized, "an app token is never valid on /mcp");
  }

  // --- OAuth code flow ------------------------------------------------------------------------------

  [Fact]
  public async Task OAuthFlow_RegisterAuthorizeConsentToken_ThenToolsWork_AndTheTokenIsRefusedOnApi()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);

    var (access, _) = await FullFlowAsync(factory, ada);
    var assistant = WithBearer(factory, access);

    var (initStatus, init, _) = await RpcAsync(assistant, "initialize", new
    {
      protocolVersion = "2025-06-18",
      capabilities = new { },
      clientInfo = new { name = "test", version = "1" },
    });
    initStatus.Should().Be(HttpStatusCode.OK, init.ToString());
    init.GetProperty("result").GetProperty("instructions").GetString().Should().Contain("Never").And.Contain("diagnose").And.Contain("compare_periods");

    var (listStatus, list, _) = await RpcAsync(assistant, "tools/list");
    listStatus.Should().Be(HttpStatusCode.OK, list.ToString());
    var names = list.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
    names.Should().Contain(["get_today", "get_daily_summary", "get_trend", "compare_periods", "get_insights", "get_checkins",
      "get_lab_markers", "get_lab_history", "get_sources", "list_metrics", "recall_memory", "remember"]);

    var (isError, text) = await CallToolAsync(assistant, "get_today");
    isError.Should().BeFalse(text);
    JsonDocument.Parse(text).RootElement.GetProperty("person").GetString().Should().Be("Ada Lovelace");

    (await assistant.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "an MCP token is never valid on /api");

    var connections = await (await ada.GetAsync("/api/connect")).ReadJsonAsync();
    connections.GetProperty("mcpUrl").GetString().Should().Be(Origin + "/mcp");
    connections.GetProperty("assistants").EnumerateArray().Select(a => a.GetProperty("clientName").GetString()).Should().Equal("Open WebUI");
  }

  [Fact]
  public async Task Authorize_WithAnUnregisteredRedirectUri_DoesNotRedirect()
  {
    await using var factory = new ApiFactory();
    var clientId = (await RegisterClientAsync(factory)).GetProperty("client_id").GetString()!;
    var (_, challenge) = Pkce();

    var response = await Anonymous(factory).GetAsync(
      $"/api/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString("https://evil.example/cb")}&code_challenge={challenge}&code_challenge_method=S256");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Headers.Location.Should().BeNull();
  }

  [Fact]
  public async Task Authorize_WithoutPkce_BouncesInvalidRequestToTheClient()
  {
    await using var factory = new ApiFactory();
    var clientId = (await RegisterClientAsync(factory)).GetProperty("client_id").GetString()!;

    var response = await Anonymous(factory).GetAsync(
      $"/api/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(RedirectUri)}&state=s1");

    response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    response.Headers.Location!.ToString().Should().StartWith(RedirectUri).And.Contain("error=invalid_request").And.Contain("state=s1");
  }

  [Fact]
  public async Task Consent_Denied_RedirectsWithAccessDenied_AndTheRequestCannotBeReused()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);
    var clientId = (await RegisterClientAsync(factory)).GetProperty("client_id").GetString()!;
    var (_, challenge) = Pkce();

    var redirect = await AuthorizeAsync(factory, ada, clientId, challenge, approve: false);

    QueryValue(redirect, "error").Should().Be("access_denied");
    QueryValue(redirect, "state").Should().Be("xyz");
  }

  [Fact]
  public async Task Token_WithTheWrongVerifier_OrAReplayedCode_IsInvalidGrant()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);
    var clientId = (await RegisterClientAsync(factory)).GetProperty("client_id").GetString()!;
    var (verifier, challenge) = Pkce();
    var redirect = await AuthorizeAsync(factory, ada, clientId, challenge);
    var code = QueryValue(redirect, "code");
    Dictionary<string, string> Form(string v) => new()
    {
      ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = RedirectUri, ["client_id"] = clientId, ["code_verifier"] = v,
    };

    var wrong = await TokenAsync(factory, Form(Pkce().Verifier));
    wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await wrong.ReadJsonAsync()).GetProperty("error").GetString().Should().Be("invalid_grant");

    (await TokenAsync(factory, Form(verifier))).StatusCode.Should().Be(HttpStatusCode.OK);

    var replay = await TokenAsync(factory, Form(verifier));
    replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await replay.ReadJsonAsync()).GetProperty("error").GetString().Should().Be("invalid_grant");
  }

  [Fact]
  public async Task Refresh_Rotates_AndReusingAnOldTokenRevokesTheFamily()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);
    var (_, refresh1) = await FullFlowAsync(factory, ada);

    var first = await TokenAsync(factory, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh1 });
    first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
    var refresh2 = (await first.ReadJsonAsync()).GetProperty("refresh_token").GetString()!;
    refresh2.Should().NotBe(refresh1);

    var reuse = await TokenAsync(factory, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh1 });
    reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a rotated token was replayed");

    var after = await TokenAsync(factory, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh2 });
    after.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the replay revoked the whole family, including the newest token");
  }

  [Fact]
  public async Task ConfidentialClient_MustPresentItsSecret_ByPostOrBasic()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);
    var registered = await RegisterClientAsync(factory, "client_secret_basic");
    var clientId = registered.GetProperty("client_id").GetString()!;
    var secret = registered.GetProperty("client_secret").GetString()!;
    secret.Should().NotBeNullOrEmpty();

    var (verifier, challenge) = Pkce();
    var redirect = await AuthorizeAsync(factory, ada, clientId, challenge);
    var form = new Dictionary<string, string>
    {
      ["grant_type"] = "authorization_code", ["code"] = QueryValue(redirect, "code"), ["redirect_uri"] = RedirectUri, ["client_id"] = clientId, ["code_verifier"] = verifier,
    };

    var noSecret = await TokenAsync(factory, form);
    noSecret.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await noSecret.ReadJsonAsync()).GetProperty("error").GetString().Should().Be("invalid_client");

    var basic = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(secret)}")));
    var withSecret = await TokenAsync(factory, form, basic);
    withSecret.StatusCode.Should().Be(HttpStatusCode.OK, await withSecret.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Register_RejectsMissingOrUnsafeRedirectUris()
  {
    await using var factory = new ApiFactory();
    var client = Anonymous(factory);

    (await client.PostAsJsonAsync("/api/oauth/register", new Dictionary<string, object> { ["client_name"] = "x" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await client.PostAsJsonAsync("/api/oauth/register", new Dictionary<string, object> { ["redirect_uris"] = new[] { "javascript:alert(1)" } }))
      .StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task RevokingAnAssistant_StopsItsRefreshToken()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);
    var (_, refresh) = await FullFlowAsync(factory, ada);
    var clientId = (await (await ada.GetAsync("/api/connect")).ReadJsonAsync()).GetProperty("assistants")[0].GetProperty("clientId").GetString()!;

    (await ada.DeleteAsync($"/api/connect/assistants/{clientId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

    (await TokenAsync(factory, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await (await ada.GetAsync("/api/connect")).ReadJsonAsync()).GetProperty("assistants").GetArrayLength().Should().Be(0);
  }

  // --- personal tokens --------------------------------------------------------------------------

  [Fact]
  public async Task PersonalToken_WorksOnMcp_IsRefusedOnApi_AndStopsWhenRevoked()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);

    var created = await (await ada.PostJsonAsync("/api/connect/tokens", new { name = "Open WebUI" })).ReadJsonAsync();
    var secret = created.GetProperty("secret").GetString()!;
    secret.Should().StartWith("pat_");
    var tokenId = created.GetProperty("token").GetProperty("id").GetGuid();
    var assistant = WithBearer(factory, secret);

    var (isError, text) = await CallToolAsync(assistant, "get_sources");
    isError.Should().BeFalse(text);
    (await assistant.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a personal token is for /mcp only");

    var listed = await (await ada.GetAsync("/api/connect")).ReadJsonAsync();
    listed.GetProperty("tokens")[0].GetProperty("prefix").GetString().Should().Be(secret[..10]);
    listed.GetProperty("tokens")[0].GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.String);

    (await ada.DeleteAsync($"/api/connect/tokens/{tokenId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    var (status, _, _) = await RpcAsync(assistant, "tools/list");
    status.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Remember_NeedsTheMemoryScope_AndRecallReturnsWhatWasKept()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);
    var readOnly = (await (await ada.PostJsonAsync("/api/connect/tokens", new { name = "Reader", allowMemoryWrite = false })).ReadJsonAsync()).GetProperty("secret").GetString()!;
    var full = (await (await ada.PostJsonAsync("/api/connect/tokens", new { name = "Claude" })).ReadJsonAsync()).GetProperty("secret").GetString()!;

    var refused = await CallToolAsync(WithBearer(factory, readOnly), "remember", new { type = "goal", key = "sleep_goal", value = "Eight hours on weeknights" });
    refused.IsError.Should().BeTrue();
    refused.Text.Should().Contain("memory:write");

    var kept = await CallToolAsync(WithBearer(factory, full), "remember", new { type = "goal", key = "sleep_goal", value = "Eight hours on weeknights", display_text = "Sleep 8 h" });
    kept.IsError.Should().BeFalse(kept.Text);

    var bad = await CallToolAsync(WithBearer(factory, full), "remember", new { type = "secret", key = "k", value = "v" });
    bad.IsError.Should().BeTrue();

    var recalled = await CallToolAsync(WithBearer(factory, readOnly), "recall_memory", new { topic = "sleep" });
    recalled.IsError.Should().BeFalse();
    var memories = JsonDocument.Parse(recalled.Text).RootElement.GetProperty("memories");
    memories.GetArrayLength().Should().Be(1);
    memories[0].GetProperty("value").GetString().Should().Be("Eight hours on weeknights");
    memories[0].GetProperty("rememberedBy").GetString().Should().Be("Claude");

    var inApp = await (await ada.GetAsync("/api/connect/memory")).ReadJsonAsync();
    var id = inApp[0].GetProperty("id").GetGuid();
    (await ada.DeleteAsync($"/api/connect/memory/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await (await ada.GetAsync("/api/connect/memory")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
  }

  // --- the share grant reaches through MCP ---------------------------------------------------------

  [Fact]
  public async Task PersonArgument_ReadsAMembersData_OnlyWhenTheyShared()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, _) = await HouseholdAsync(factory);
    var bobToken = (await (await bob.PostJsonAsync("/api/connect/tokens", new { name = "Bob's assistant" })).ReadJsonAsync()).GetProperty("secret").GetString()!;
    var assistant = WithBearer(factory, bobToken);

    var before = await CallToolAsync(assistant, "get_daily_summary", new { metric = "steps", person = "Ada" });
    before.IsError.Should().BeTrue();
    before.Text.Should().Contain("Ada Lovelace has not shared activity data with you");

    var unknown = await CallToolAsync(assistant, "get_today", new { person = "Mallory" });
    unknown.IsError.Should().BeTrue();
    unknown.Text.Should().Contain("No household member is called Mallory");

    (await ada.PutJsonAsync($"/api/household/shares/{(await (await bob.GetAsync("/api/auth/me")).ReadJsonAsync()).GetProperty("person").GetProperty("id").GetGuid()}",
      new { categories = new[] { "activity" } })).StatusCode.Should().Be(HttpStatusCode.OK);

    var after = await CallToolAsync(assistant, "get_daily_summary", new { metric = "steps", person = "ada lovelace" });
    after.IsError.Should().BeFalse(after.Text);
    JsonDocument.Parse(after.Text).RootElement.GetProperty("person").GetString().Should().Be("Ada Lovelace");

    var stillPrivate = await CallToolAsync(assistant, "get_checkins", new { person = "Ada" });
    stillPrivate.IsError.Should().BeTrue("activity was shared, check-ins were not");

    var ownOnly = await CallToolAsync(assistant, "get_sources", new { person = "Ada" });
    ownOnly.IsError.Should().BeTrue("sources are never shared");
    adaPerson.Should().NotBeEmpty();
  }

  [Fact]
  public async Task ComparePeriods_AndLabs_AnswerTheSinceMyLastBloodTestQuestion()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);
    var token = (await (await ada.PostJsonAsync("/api/connect/tokens", new { name = "Open WebUI" })).ReadJsonAsync()).GetProperty("secret").GetString()!;
    var assistant = WithBearer(factory, token);

    var drawn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
    var report = await ada.PostJsonAsync("/api/labs/reports", new
    {
      provider = "rythm",
      collectedOn = drawn,
      results = new[] { new { metricKey = "lab.apob", value = 92.0, unit = "mg/dL" } },
    });
    report.StatusCode.Should().Be(HttpStatusCode.OK, await report.Content.ReadAsStringAsync());

    var markers = await CallToolAsync(assistant, "get_lab_markers");
    markers.IsError.Should().BeFalse(markers.Text);
    var root = JsonDocument.Parse(markers.Text).RootElement;
    root.GetProperty("lastPanelDate").GetString().Should().Be(drawn.ToString("yyyy-MM-dd"));
    root.GetProperty("markers")[0].GetProperty("marker").GetString().Should().Be("lab.apob");

    var history = await CallToolAsync(assistant, "get_lab_history", new { marker = "apob" });
    history.IsError.Should().BeFalse(history.Text);
    JsonDocument.Parse(history.Text).RootElement.GetProperty("history").GetArrayLength().Should().Be(1);

    var compare = await CallToolAsync(assistant, "compare_periods", new
    {
      metric = "steps",
      from_a = drawn.AddDays(-30).ToString("yyyy-MM-dd"),
      to_a = drawn.AddDays(-1).ToString("yyyy-MM-dd"),
      from_b = drawn.ToString("yyyy-MM-dd"),
      to_b = "today",
    });
    compare.IsError.Should().BeFalse(compare.Text);
    JsonDocument.Parse(compare.Text).RootElement.GetProperty("b").GetProperty("daysWithData").GetInt32().Should().Be(0);

    var tooLong = await CallToolAsync(assistant, "get_daily_summary", new { metric = "steps", from = "2025-01-01", to = "2026-01-01" });
    tooLong.IsError.Should().BeTrue();
    tooLong.Text.Should().Contain("90 days");
  }
}
