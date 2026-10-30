using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SelfKept.Infrastructure.Services.Sso;
using Xunit;

namespace SelfKept.UnitTests.Services;

/// <summary>
/// The ID token checks that decide who signed in: a real RS256 token from a fake provider served
/// over HTTP, then one wrong thing at a time.
/// </summary>
public class SsoProviderTests
{
  private const string Authority = "https://id.example.test";
  private static readonly DateTimeOffset Now = new(2026, 10, 30, 18, 0, 0, TimeSpan.Zero);

  private sealed class FakeIdp : HttpMessageHandler
  {
    public RsaSecurityKey Key { get; set; } = NewKey("key-1");
    public string Issuer { get; set; } = Authority;
    public string IdToken { get; set; } = string.Empty;
    public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
    public List<string> Requests { get; } = [];
    public List<string> Hosts { get; } = [];
    public string? TokenRequestAuthorization { get; private set; }
    public string? TokenRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      var path = request.RequestUri!.AbsolutePath;
      Requests.Add(path);
      Hosts.Add(request.RequestUri.GetLeftPart(UriPartial.Authority));
      return path switch
      {
        "/.well-known/openid-configuration" => Json(new
        {
          issuer = Issuer,
          authorization_endpoint = $"{Authority}/authorize",
          token_endpoint = $"{Authority}/api/oidc/token",
          jwks_uri = $"{Authority}/.well-known/jwks.json",
        }),
        "/.well-known/jwks.json" => Json(new { keys = new[] { JsonWebKeyConverter.ConvertFromRSASecurityKey(Key) } }),
        "/api/oidc/token" => await TokenAsync(request, ct),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
      };
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
      TokenRequestAuthorization = request.Headers.Authorization?.ToString();
      TokenRequestBody = await request.Content!.ReadAsStringAsync(ct);
      return TokenStatus == HttpStatusCode.OK
        ? Json(new { access_token = "at", token_type = "Bearer", id_token = IdToken })
        : new HttpResponseMessage(TokenStatus);
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
      Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
  }

  private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
  {
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
  }

  private static RsaSecurityKey NewKey(string id) => new(RSA.Create(2048)) { KeyId = id };

  private static string Token(
    RsaSecurityKey key, string issuer = Authority, string audience = "selfkept", string nonce = "nonce-1",
    DateTimeOffset? expires = null, Dictionary<string, object>? extra = null)
  {
    var claims = new Dictionary<string, object>
    {
      ["sub"] = "sub-ada",
      ["nonce"] = nonce,
      ["email"] = "ada@example.com",
      ["email_verified"] = true,
      ["name"] = "Ada",
    };
    foreach (var (k, v) in extra ?? []) claims[k] = v;
    return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
    {
      Issuer = issuer,
      Audience = audience,
      IssuedAt = Now.UtcDateTime,
      NotBefore = Now.UtcDateTime.AddSeconds(-5),
      Expires = (expires ?? Now.AddMinutes(5)).UtcDateTime,
      Claims = claims,
      SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
    });
  }

  private static (SsoProvider Provider, FakeIdp Idp) Create(string? internalUrl = null)
  {
    var idp = new FakeIdp();
    var settings = new SsoSettings
    {
      Authority = Authority,
      InternalUrl = internalUrl,
      ClientId = "selfkept",
      ClientSecret = "s3cret:with/odd chars",
      RedirectUri = "https://box.example.test/api/auth/sso/callback",
      AppBaseUrl = "https://box.example.test",
    };
    return (new SsoProvider(new Clients(idp), settings, new TestClock(Now)), idp);
  }

  [Fact]
  public async Task AuthorizeUrl_CarriesStateNonceAndPkce()
  {
    var (provider, _) = Create();

    var url = await provider.AuthorizeUrlAsync("st", "nn", "challenge", default);

    url.Should().StartWith($"{Authority}/authorize?response_type=code&client_id=selfkept");
    url.Should().Contain("redirect_uri=https%3A%2F%2Fbox.example.test%2Fapi%2Fauth%2Fsso%2Fcallback")
      .And.Contain("scope=openid%20email%20profile")
      .And.Contain("state=st").And.Contain("nonce=nn")
      .And.Contain("code_challenge=challenge&code_challenge_method=S256");
  }

  [Fact]
  public async Task Redeem_AValidToken_ReturnsWhoSignedIn_AndSendsBasicAuthAndTheVerifier()
  {
    var (provider, idp) = Create();
    idp.IdToken = Token(idp.Key);

    var identity = await provider.RedeemAsync("code-1", "verifier-1", "nonce-1", default);

    identity.Should().Be(new SsoIdentity(Authority, "sub-ada", "ada@example.com", EmailVerified: true, "Ada"));
    var basic = Encoding.UTF8.GetString(Convert.FromBase64String(idp.TokenRequestAuthorization!["Basic ".Length..]));
    basic.Should().Be("selfkept:s3cret%3Awith%2Fodd%20chars", "client_secret_basic form-encodes both halves");
    idp.TokenRequestBody.Should().Contain("grant_type=authorization_code").And.Contain("code=code-1").And.Contain("code_verifier=verifier-1");
  }

  [Fact]
  public async Task Redeem_EmailVerifiedFalseOrMissing_IsNotTrusted()
  {
    var (provider, idp) = Create();
    idp.IdToken = Token(idp.Key, extra: new() { ["email_verified"] = false });

    (await provider.RedeemAsync("c", "v", "nonce-1", default)).EmailVerified.Should().BeFalse();
  }

  [Theory]
  [InlineData("nonce")]
  [InlineData("audience")]
  [InlineData("issuer")]
  [InlineData("expired")]
  [InlineData("signature")]
  public async Task Redeem_RefusesATokenWithOneThingWrong(string wrong)
  {
    var (provider, idp) = Create();
    idp.IdToken = wrong switch
    {
      "nonce" => Token(idp.Key, nonce: "someone-elses-nonce"),
      "audience" => Token(idp.Key, audience: "another-app"),
      "issuer" => Token(idp.Key, issuer: "https://evil.example.test"),
      "expired" => Token(idp.Key, expires: Now.AddMinutes(-10)),
      _ => Token(NewKey("key-1")), // same key id, different key
    };

    var act = () => provider.RedeemAsync("c", "v", "nonce-1", default);

    await act.Should().ThrowAsync<SsoProviderException>();
  }

  [Fact]
  public async Task Redeem_ARotatedKey_IsFetchedOnce()
  {
    var (provider, idp) = Create();
    idp.IdToken = Token(idp.Key);
    await provider.RedeemAsync("c", "v", "nonce-1", default);

    idp.Key = NewKey("key-2");
    idp.IdToken = Token(idp.Key);
    var identity = await provider.RedeemAsync("c", "v", "nonce-1", default);

    identity.Subject.Should().Be("sub-ada");
    idp.Requests.Count(r => r == "/.well-known/jwks.json").Should().Be(2);
  }

  [Fact]
  public async Task InternalUrl_CarriesTheServersOwnCalls_WhileTheBrowserKeepsThePublicAddress()
  {
    var (provider, idp) = Create(internalUrl: "http://pocket-id:1411");
    idp.IdToken = Token(idp.Key);

    var authorize = await provider.AuthorizeUrlAsync("s", "nonce-1", "c", default);
    var identity = await provider.RedeemAsync("c", "v", "nonce-1", default);

    authorize.Should().StartWith($"{Authority}/authorize?");
    identity.Issuer.Should().Be(Authority, "the issuer is still the public address");
    idp.Hosts.Should().OnlyContain(h => h == "http://pocket-id:1411");
    idp.Requests.Should().Contain(["/.well-known/openid-configuration", "/.well-known/jwks.json", "/api/oidc/token"]);
  }

  [Fact]
  public async Task Discovery_ThatNamesAnotherIssuer_IsRefused()
  {
    var (provider, idp) = Create();
    idp.Issuer = "https://somewhere-else.example.test";

    var act = () => provider.AuthorizeUrlAsync("s", "n", "c", default);

    await act.Should().ThrowAsync<SsoProviderException>().WithMessage("*calls itself*");
  }

  [Fact]
  public async Task Redeem_ARefusedCode_Throws()
  {
    var (provider, idp) = Create();
    idp.TokenStatus = HttpStatusCode.BadRequest;

    var act = () => provider.RedeemAsync("c", "v", "nonce-1", default);

    await act.Should().ThrowAsync<SsoProviderException>().WithMessage("*refused*");
  }
}
