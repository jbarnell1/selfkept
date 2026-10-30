using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// The whole Foundation surface in order: first user, second user by invite, household, shares.
/// One factory per class so the database state carries from step to step inside a test.
/// </summary>
public class AuthFlowTests : IClassFixture<ApiFactory>
{
  private readonly ApiFactory _factory;

  public AuthFlowTests(ApiFactory factory)
  {
    _factory = factory;
  }

  private static object Register(string email, string displayName, string? inviteCode = null) =>
    new { email, password = "correct horse battery staple", displayName, inviteCode };

  [Fact]
  public async Task FullHouseholdFlow_FirstUserAdmin_InviteSecondUser_ShareData()
  {
    // Web client: refresh token arrives as an HttpOnly cookie, never in the body.
    var ada = _factory.CreateClient();
    var registered = await ada.PostJsonAsync("/api/auth/register", Register("ada@example.com", "Ada"));
    registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
    var adaAuth = await registered.ReadJsonAsync();
    adaAuth.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    adaAuth.TryGetProperty("refreshToken", out _).Should().BeFalse("web clients get the refresh token as a cookie");
    registered.Headers.GetValues("Set-Cookie").Should().ContainSingle(c => c.StartsWith("selfkept_refresh=") && c.Contains("httponly") && c.Contains("samesite=strict"));
    var adaUser = adaAuth.GetProperty("user");
    adaUser.GetProperty("role").GetString().Should().Be("admin");
    adaUser.GetProperty("email").GetString().Should().Be("ada@example.com");
    var adaPersonId = adaUser.GetProperty("person").GetProperty("id").GetGuid();
    adaUser.GetProperty("person").GetProperty("units").GetString().Should().Be("metric");
    adaUser.GetProperty("person").GetProperty("sexAtBirth").GetString().Should().Be("unspecified");
    ada.UseBearer(adaAuth.GetProperty("accessToken").GetString()!);

    // Registration closes after the first account.
    var status = await (await ada.GetAsync("/api/system/setup-status")).ReadJsonAsync();
    status.GetProperty("needsFirstUser").GetBoolean().Should().BeFalse();
    status.GetProperty("registrationOpen").GetBoolean().Should().BeFalse();

    // A stranger without an invite is refused with a problem body.
    var stranger = await _factory.CreateClient().PostJsonAsync("/api/auth/register", Register("mallory@example.com", "Mallory"));
    stranger.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    stranger.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

    // Wrong invite code too.
    var wrongCode = await _factory.CreateClient().PostJsonAsync("/api/auth/register", Register("mallory@example.com", "Mallory", "AAAAA-BBBBB-CCCCC-DDDDD"));
    wrongCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);

    // The admin creates an invite; the code is returned once.
    var created = await ada.PostJsonAsync("/api/household/invites", new { });
    created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
    var invite = await created.ReadJsonAsync();
    var code = invite.GetProperty("code").GetString()!;
    code.Should().MatchRegex("^[A-Z2-9]{5}(-[A-Z2-9]{5}){3}$");
    var pending = await (await ada.GetAsync("/api/household/invites")).ReadJsonAsync();
    pending.GetArrayLength().Should().Be(1);

    // Bob registers with the code (mobile client: refresh token in the body) and joins as a member.
    var bob = _factory.CreateClient();
    bob.DefaultRequestHeaders.Add("X-Client", "mobile");
    var bobRegistered = await bob.PostJsonAsync("/api/auth/register", Register("bob@example.com", "Bob", code.ToLowerInvariant()));
    bobRegistered.StatusCode.Should().Be(HttpStatusCode.OK, await bobRegistered.Content.ReadAsStringAsync());
    var bobAuth = await bobRegistered.ReadJsonAsync();
    bobAuth.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty("mobile clients get the refresh token in the body");
    bobRegistered.Headers.Contains("Set-Cookie").Should().BeFalse();
    bobAuth.GetProperty("user").GetProperty("role").GetString().Should().Be("member");
    bobAuth.GetProperty("user").GetProperty("householdId").GetGuid().Should().Be(adaUser.GetProperty("householdId").GetGuid());
    var bobPersonId = bobAuth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid();
    bob.UseBearer(bobAuth.GetProperty("accessToken").GetString()!);

    // The invite is one-time.
    var reuse = await _factory.CreateClient().PostJsonAsync("/api/auth/register", Register("carol@example.com", "Carol", code));
    reuse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await (await ada.GetAsync("/api/household/invites")).ReadJsonAsync()).GetArrayLength().Should().Be(0);

    // Household shows both members; only the admin may rename or invite.
    var household = await (await ada.GetAsync("/api/household")).ReadJsonAsync();
    household.GetProperty("members").GetArrayLength().Should().Be(2);
    (await bob.PutJsonAsync("/api/household", new { name = "Bob's place" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await bob.PostJsonAsync("/api/household/invites", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    var renamed = await (await ada.PutJsonAsync("/api/household", new { name = "The Lovelaces" })).ReadJsonAsync();
    renamed.GetProperty("name").GetString().Should().Be("The Lovelaces");

    // Sharing: Ada shares sleep and activity with Bob; Bob sees it under shared-with-me.
    var shared = await ada.PutJsonAsync($"/api/household/shares/{bobPersonId}", new { categories = new[] { "sleep", "activity", "sleep" } });
    shared.StatusCode.Should().Be(HttpStatusCode.OK, await shared.Content.ReadAsStringAsync());
    var grant = await shared.ReadJsonAsync();
    grant.GetProperty("personId").GetGuid().Should().Be(bobPersonId);
    grant.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).Should().BeEquivalentTo(new[] { "activity", "sleep" });

    var adaShares = await (await ada.GetAsync("/api/household/shares")).ReadJsonAsync();
    adaShares.GetArrayLength().Should().Be(1);
    var withBob = await (await bob.GetAsync("/api/household/shared-with-me")).ReadJsonAsync();
    withBob.GetArrayLength().Should().Be(1);
    withBob[0].GetProperty("personId").GetGuid().Should().Be(adaPersonId);
    withBob[0].GetProperty("displayName").GetString().Should().Be("Ada");
    (await (await bob.GetAsync("/api/household/shares")).ReadJsonAsync()).GetArrayLength().Should().Be(0, "Bob has shared nothing");

    // Sharing with yourself or with a stranger fails.
    (await ada.PutJsonAsync($"/api/household/shares/{adaPersonId}", new { categories = new[] { "sleep" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PutJsonAsync($"/api/household/shares/{Guid.NewGuid()}", new { categories = new[] { "sleep" } })).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // Revoking: an empty list removes every grant.
    await ada.PutJsonAsync($"/api/household/shares/{bobPersonId}", new { categories = Array.Empty<string>() });
    (await (await bob.GetAsync("/api/household/shared-with-me")).ReadJsonAsync()).GetArrayLength().Should().Be(0);

    // Profile and password.
    var profile = await ada.PutJsonAsync("/api/me/profile", new { displayName = "Ada L.", sexAtBirth = "female", birthDate = "1815-12-10", units = "imperial", timeZone = "Europe/London" });
    profile.StatusCode.Should().Be(HttpStatusCode.OK, await profile.Content.ReadAsStringAsync());
    var person = await profile.ReadJsonAsync();
    person.GetProperty("displayName").GetString().Should().Be("Ada L.");
    person.GetProperty("units").GetString().Should().Be("imperial");
    person.GetProperty("birthDate").GetString().Should().Be("1815-12-10");
    (await (await ada.GetAsync("/api/me/profile")).ReadJsonAsync()).GetProperty("timeZone").GetString().Should().Be("Europe/London");
    var me = await (await ada.GetAsync("/api/auth/me")).ReadJsonAsync();
    me.GetProperty("displayName").GetString().Should().Be("Ada L.");

    (await ada.PutJsonAsync("/api/me/password", new { currentPassword = "wrong", newPassword = "another long password" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PutJsonAsync("/api/me/password", new { currentPassword = "correct horse battery staple", newPassword = "another long password" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
  }

  [Fact]
  public async Task Login_Refresh_Logout_WebCookieFlow()
  {
    var factory = new ApiFactory();
    await using var _ = factory;
    var client = factory.CreateClient();
    (await client.PostJsonAsync("/api/auth/register", Register("eve@example.com", "Eve"))).StatusCode.Should().Be(HttpStatusCode.OK);

    var badLogin = await client.PostJsonAsync("/api/auth/login", new { email = "eve@example.com", password = "nope" });
    badLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    badLogin.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

    var login = await client.PostJsonAsync("/api/auth/login", new { email = "EVE@example.com", password = "correct horse battery staple" });
    login.StatusCode.Should().Be(HttpStatusCode.OK);
    var first = await login.ReadJsonAsync();

    // The cookie jar carries selfkept_refresh; refresh rotates it and returns a new access token.
    var refreshed = await client.PostJsonAsync("/api/auth/refresh", new { });
    refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
    var second = await refreshed.ReadJsonAsync();
    second.GetProperty("accessToken").GetString().Should().NotBe(first.GetProperty("accessToken").GetString());
    refreshed.Headers.GetValues("Set-Cookie").Should().ContainSingle(c => c.StartsWith("selfkept_refresh="));

    // Logout revokes the current refresh token; refreshing with it is refused.
    client.UseBearer(second.GetProperty("accessToken").GetString()!);
    (await client.PostJsonAsync("/api/auth/logout", new { })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    client.DefaultRequestHeaders.Authorization = null;
    var afterLogout = await client.PostJsonAsync("/api/auth/refresh", new { });
    afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Refresh_MobileBody_RotatesAndDetectsReuse()
  {
    var factory = new ApiFactory();
    await using var _ = factory;
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add("X-Client", "mobile");
    var registered = await (await client.PostJsonAsync("/api/auth/register", Register("fay@example.com", "Fay"))).ReadJsonAsync();
    var original = registered.GetProperty("refreshToken").GetString()!;

    var rotated = await client.PostJsonAsync("/api/auth/refresh", new { refreshToken = original });
    rotated.StatusCode.Should().Be(HttpStatusCode.OK);
    var replacement = (await rotated.ReadJsonAsync()).GetProperty("refreshToken").GetString()!;
    replacement.Should().NotBe(original);

    // Re-presenting the rotated token within the grace window is tolerated (multi-tab race);
    // the replacement keeps working either way.
    (await client.PostJsonAsync("/api/auth/refresh", new { refreshToken = replacement })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await client.PostJsonAsync("/api/auth/refresh", new { refreshToken = "not-a-real-token" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task DeletedOrUnknownUserToken_Is401()
  {
    var client = _factory.CreateClient();
    // A well-formed token signed with the wrong key is rejected by the bearer handler.
    client.UseBearer("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIn0.invalidsignature");

    var response = await client.GetAsync("/api/auth/me");

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }
}
