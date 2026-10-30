using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SelfKept.Infrastructure.Services.Sso;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Provider sign-in end to end with the provider replaced by a fake: the button's redirect, the
/// provider's return, linking from a signed-in account, the verified-email first sign-in, the
/// phone handoff, and the cases that must never reach an account.
/// </summary>
public class SsoFlowTests
{
  private const string Issuer = "https://id.example.test";

  /// <summary>Stands in for the provider: the authorize URL echoes what it was given, and each code redeems to a scripted identity.</summary>
  private sealed class FakeProvider : ISsoProvider
  {
    public Dictionary<string, SsoIdentity> Codes { get; } = new();
    public bool Down { get; set; }
    public string? LastNonce { get; private set; }

    public Task<string> AuthorizeUrlAsync(string state, string nonce, string codeChallenge, CancellationToken ct)
    {
      if (Down) throw new SsoProviderException("down");
      return Task.FromResult($"{Issuer}/authorize?state={state}&nonce={nonce}&code_challenge={codeChallenge}");
    }

    public Task<SsoIdentity> RedeemAsync(string code, string codeVerifier, string nonce, CancellationToken ct)
    {
      LastNonce = nonce;
      return Codes.TryGetValue(code, out var identity)
        ? Task.FromResult(identity)
        : throw new SsoProviderException("unknown code");
    }
  }

  private static (WebApplicationFactory<Program> Factory, FakeProvider Provider) Create(ApiFactory baseFactory, bool enabled = true)
  {
    var provider = new FakeProvider();
    var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      if (enabled)
      {
        builder.UseSetting("Sso:Authority", Issuer);
        builder.UseSetting("Sso:ClientId", "selfkept");
        builder.UseSetting("Sso:ClientSecret", "provider-secret");
        builder.UseSetting("Sso:Name", "Pocket ID");
      }
      builder.ConfigureServices(services =>
      {
        services.RemoveAll<ISsoProvider>();
        services.AddSingleton<ISsoProvider>(provider);
      });
    });
    return (factory, provider);
  }

  private static HttpClient Browser(WebApplicationFactory<Program> factory) =>
    factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

  private static async Task<(HttpClient Client, string Email)> RegisterAsync(WebApplicationFactory<Program> factory, string? inviteCode = null)
  {
    var email = $"ada-{Guid.NewGuid():N}@example.com";
    var client = Browser(factory);
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = "Ada", inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    client.UseBearer((await response.ReadJsonAsync()).GetProperty("accessToken").GetString()!);
    return (client, email);
  }

  private static string QueryValue(string url, string name) =>
    Uri.UnescapeDataString(new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).First(p => p[0] == name)[1]);

  /// <summary>Presses the sign-in button and returns the state the provider would echo back.</summary>
  private static async Task<string> StartSignInAsync(HttpClient browser, string client = "web")
  {
    var start = await browser.GetAsync($"/api/auth/sso/start?client={client}");
    start.StatusCode.Should().Be(HttpStatusCode.Redirect);
    var url = start.Headers.Location!.ToString();
    url.Should().StartWith($"{Issuer}/authorize?");
    return QueryValue(url, "state");
  }

  private static async Task<string> StartLinkAsync(HttpClient signedIn, string client = "web")
  {
    var start = await signedIn.PostAsync($"/api/auth/sso/link?client={client}", null);
    start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
    return QueryValue((await start.ReadJsonAsync()).GetProperty("url").GetString()!, "state");
  }

  private static Task<HttpResponseMessage> CallbackAsync(HttpClient browser, string code, string state) =>
    browser.GetAsync($"/api/auth/sso/callback?code={code}&state={state}");

  [Fact]
  public async Task Info_IsOffUntilConfigured_AndTheButtonSaysWhy()
  {
    await using var baseFactory = new ApiFactory();
    var (off, _) = Create(baseFactory, enabled: false);

    var info = await (await Browser(off).GetAsync("/api/auth/sso")).ReadJsonAsync();
    info.GetProperty("enabled").GetBoolean().Should().BeFalse();
    var start = await Browser(off).GetAsync("/api/auth/sso/start");
    start.Headers.Location!.ToString().Should().Be("http://localhost/login?sso=disabled");

    var (on, _) = Create(baseFactory);
    var enabled = await (await Browser(on).GetAsync("/api/auth/sso")).ReadJsonAsync();
    enabled.GetProperty("enabled").GetBoolean().Should().BeTrue();
    enabled.GetProperty("name").GetString().Should().Be("Pocket ID");
  }

  [Fact]
  public async Task Start_AsksForPkceAndANonce_AndAnUnreachableProviderLandsOnLogin()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, provider) = Create(baseFactory);

    var start = await Browser(factory).GetAsync("/api/auth/sso/start");
    var url = start.Headers.Location!.ToString();
    QueryValue(url, "nonce").Should().NotBeNullOrEmpty();
    QueryValue(url, "code_challenge").Should().HaveLength(43, "an S256 challenge");

    provider.Down = true;
    var down = await Browser(factory).GetAsync("/api/auth/sso/start");
    down.Headers.Location!.ToString().Should().Be("http://localhost/login?sso=provider");
  }

  [Fact]
  public async Task Link_ThenSignIn_SetsTheRefreshCookieAndTheSessionWorks()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, provider) = Create(baseFactory);
    var (ada, _) = await RegisterAsync(factory);
    provider.Codes["link-code"] = new SsoIdentity(Issuer, "sub-ada", "ada@id.example.test", EmailVerified: false, "Ada");
    provider.Codes["signin-code"] = provider.Codes["link-code"];

    var linkState = await StartLinkAsync(ada);
    var linked = await CallbackAsync(Browser(factory), "link-code", linkState);
    linked.Headers.Location!.ToString().Should().Be("http://localhost/you?sso=linked");
    var status = await (await ada.GetAsync("/api/auth/sso/link")).ReadJsonAsync();
    status.GetProperty("linked").GetBoolean().Should().BeTrue();
    status.GetProperty("email").GetString().Should().Be("ada@id.example.test");

    var browser = Browser(factory);
    var state = await StartSignInAsync(browser);
    var back = await CallbackAsync(browser, "signin-code", state);
    back.StatusCode.Should().Be(HttpStatusCode.Redirect);
    back.Headers.Location!.ToString().Should().Be("http://localhost/");
    var cookie = back.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("selfkept_refresh="));
    cookie.Should().Contain("httponly").And.Contain("path=/api/auth");

    // The web app's first move after landing: refresh with the cookie.
    var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
    refresh.Headers.Add("Cookie", cookie.Split(';')[0]);
    var refreshed = await browser.SendAsync(refresh);
    refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
    (await refreshed.ReadJsonAsync()).GetProperty("user").GetProperty("displayName").GetString().Should().Be("Ada");

    var replay = await CallbackAsync(browser, "signin-code", state);
    replay.Headers.Location!.ToString().Should().Be("http://localhost/login?sso=expired", "a state is single use");
  }

  [Fact]
  public async Task FirstSignIn_WithAVerifiedEmail_FindsTheAccount_ButAnUnverifiedOneNever()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, provider) = Create(baseFactory);
    var (_, email) = await RegisterAsync(factory);
    provider.Codes["unverified"] = new SsoIdentity(Issuer, "sub-x", email.ToUpperInvariant(), EmailVerified: false, null);
    provider.Codes["verified"] = new SsoIdentity(Issuer, "sub-ada", email.ToUpperInvariant(), EmailVerified: true, null);
    provider.Codes["other-login-same-email"] = new SsoIdentity(Issuer, "sub-mallory", email, EmailVerified: true, null);

    var browser = Browser(factory);
    var refused = await CallbackAsync(browser, "unverified", await StartSignInAsync(browser));
    refused.Headers.Location!.ToString().Should().Be("http://localhost/login?sso=not_linked");
    refused.Headers.Contains("Set-Cookie").Should().BeFalse();

    var found = await CallbackAsync(browser, "verified", await StartSignInAsync(browser));
    found.Headers.Location!.ToString().Should().Be("http://localhost/");

    // The account is now tied to sub-ada; a second provider login claiming the same email is refused.
    var second = await CallbackAsync(browser, "other-login-same-email", await StartSignInAsync(browser));
    second.Headers.Location!.ToString().Should().Be("http://localhost/login?sso=not_linked");
  }

  [Fact]
  public async Task NoAccountIsEverCreated_ForAnUnknownLogin()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, provider) = Create(baseFactory);
    await RegisterAsync(factory);
    provider.Codes["stranger"] = new SsoIdentity(Issuer, "sub-stranger", "stranger@example.com", EmailVerified: true, "Stranger");

    var browser = Browser(factory);
    var back = await CallbackAsync(browser, "stranger", await StartSignInAsync(browser));
    back.Headers.Location!.ToString().Should().Be("http://localhost/login?sso=not_linked");
    var login = await browser.PostJsonAsync("/api/auth/login", new { email = "stranger@example.com", password = "anything at all" });
    login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task ALoginTiedToOneAccount_CannotBeLinkedToAnother()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, provider) = Create(baseFactory);
    var (ada, _) = await RegisterAsync(factory);
    var invite = await (await ada.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    var (bob, _) = await RegisterAsync(factory, invite.GetProperty("code").GetString());
    provider.Codes["ada"] = new SsoIdentity(Issuer, "sub-ada", null, false, null);

    (await CallbackAsync(Browser(factory), "ada", await StartLinkAsync(ada))).Headers.Location!.ToString()
      .Should().Be("http://localhost/you?sso=linked");
    (await CallbackAsync(Browser(factory), "ada", await StartLinkAsync(bob))).Headers.Location!.ToString()
      .Should().Be("http://localhost/you?sso=linked_elsewhere");
    (await (await bob.GetAsync("/api/auth/sso/link")).ReadJsonAsync()).GetProperty("linked").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task Phone_GetsAHandoffCode_ThatTradesForTokensOnce()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, provider) = Create(baseFactory);
    var (ada, _) = await RegisterAsync(factory);
    provider.Codes["ada"] = new SsoIdentity(Issuer, "sub-ada", null, false, null);
    await CallbackAsync(Browser(factory), "ada", await StartLinkAsync(ada));

    var browser = Browser(factory);
    var back = await CallbackAsync(browser, "ada", await StartSignInAsync(browser, client: "mobile"));
    var location = back.Headers.Location!.OriginalString;
    location.Should().StartWith("selfkept://sso?code=");
    back.Headers.Contains("Set-Cookie").Should().BeFalse("a phone keeps its refresh token, not a cookie");
    var code = Uri.UnescapeDataString(location["selfkept://sso?code=".Length..]);

    var phone = Browser(factory);
    phone.DefaultRequestHeaders.Add("X-Client", "mobile");
    var exchanged = await phone.PostJsonAsync("/api/auth/sso/exchange", new { code });
    exchanged.StatusCode.Should().Be(HttpStatusCode.OK, await exchanged.Content.ReadAsStringAsync());
    var body = await exchanged.ReadJsonAsync();
    body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    body.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();

    (await phone.PostJsonAsync("/api/auth/sso/exchange", new { code })).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a handoff is single use");
  }

  [Fact]
  public async Task Cancelled_AtTheProvider_GoesBackWithTheReason()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, _) = Create(baseFactory);
    var (ada, _) = await RegisterAsync(factory);

    var browser = Browser(factory);
    var signIn = await browser.GetAsync($"/api/auth/sso/callback?error=access_denied&state={await StartSignInAsync(browser, "mobile")}");
    signIn.Headers.Location!.OriginalString.Should().Be("selfkept://sso?error=cancelled");

    var link = await browser.GetAsync($"/api/auth/sso/callback?error=access_denied&state={await StartLinkAsync(ada)}");
    link.Headers.Location!.ToString().Should().Be("http://localhost/you?sso=cancelled");
  }

  [Fact]
  public async Task Unlink_RemovesTheLogin_AndLinkEndpointsNeedASession()
  {
    await using var baseFactory = new ApiFactory();
    var (factory, provider) = Create(baseFactory);
    var (ada, _) = await RegisterAsync(factory);
    provider.Codes["ada"] = new SsoIdentity(Issuer, "sub-ada", null, false, null);
    await CallbackAsync(Browser(factory), "ada", await StartLinkAsync(ada));

    (await ada.DeleteAsync("/api/auth/sso/link")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await ada.DeleteAsync("/api/auth/sso/link")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    var browser = Browser(factory);
    var back = await CallbackAsync(browser, "ada", await StartSignInAsync(browser));
    back.Headers.Location!.ToString().Should().Be("http://localhost/login?sso=not_linked");

    var anonymous = Browser(factory);
    (await anonymous.GetAsync("/api/auth/sso/link")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anonymous.PostAsync("/api/auth/sso/link", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }
}
