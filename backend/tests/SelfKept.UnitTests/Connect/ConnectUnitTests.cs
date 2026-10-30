using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Mcp;
using SelfKept.Infrastructure.Security;
using SelfKept.Infrastructure.Services.Connect;
using Xunit;

namespace SelfKept.UnitTests.Connect;

public class ConnectUnitTests
{
  // --- SSRF classifier ------------------------------------------------------------------------------

  [Theory]
  [InlineData("8.8.8.8", true)]
  [InlineData("2606:4700:4700::1111", true)]
  [InlineData("127.0.0.1", false)]
  [InlineData("10.1.2.3", false)]
  [InlineData("172.20.0.1", false)]
  [InlineData("192.168.1.10", false)]
  [InlineData("169.254.169.254", false)]
  [InlineData("100.101.2.3", false)] // a tailnet address: never fetched for client metadata
  [InlineData("0.0.0.0", false)]
  [InlineData("224.0.0.1", false)]
  [InlineData("::1", false)]
  [InlineData("fd7a:115c:a1e0::1", false)]
  [InlineData("fe80::1", false)]
  [InlineData("::ffff:10.0.0.1", false)]
  public void IsPublic_ClassifiesByExclusion(string address, bool expected)
  {
    PublicAddresses.IsPublic(IPAddress.Parse(address)).Should().Be(expected);
  }

  // --- PKCE -------------------------------------------------------------------------------------------

  [Fact]
  public void VerifyPkce_S256_MatchesOnlyTheRightVerifier()
  {
    var verifier = TokenCrypto.Base64Url(RandomNumberGenerator.GetBytes(32));
    var challenge = TokenCrypto.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    TokenCrypto.VerifyPkce(verifier, challenge).Should().BeTrue();
    TokenCrypto.VerifyPkce(verifier + "x", challenge).Should().BeFalse();
    TokenCrypto.VerifyPkce(verifier, verifier).Should().BeFalse("plain is not accepted");
  }

  // --- settings --------------------------------------------------------------------------------------

  private static IConfiguration Config(params (string Key, string Value)[] values) =>
    new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

  [Fact]
  public void McpSettings_FallBackToAppBaseUrl_AndDeriveTheirOwnSigningKey()
  {
    var settings = McpSettings.From(Config(("App:BaseUrl", "https://box.tail1234.ts.net/"), ("Jwt:Key", "an-app-signing-key-of-plenty-of-length-0123456789")));

    settings.IsConfigured.Should().BeTrue();
    settings.Issuer.Should().Be("https://box.tail1234.ts.net");
    settings.ResourceUri.Should().Be("https://box.tail1234.ts.net/mcp");
    settings.ConsentUrl(Guid.Empty).Should().StartWith("https://box.tail1234.ts.net/connect?request=");
    settings.SigningKey.Should().HaveCount(32);
    settings.SigningKey.Should().NotEqual(Encoding.UTF8.GetBytes("an-app-signing-key-of-plenty-of-length-0123456789").Take(32),
      "MCP tokens must never validate with the app key");
  }

  [Fact]
  public void McpSettings_WithoutAnOrigin_StayOff()
  {
    McpSettings.From(Config(("Jwt:Key", "k"))).IsConfigured.Should().BeFalse();
    McpSettings.From(Config(("App:BaseUrl", "https://x.example"), ("Mcp:Enabled", "false"))).IsConfigured.Should().BeFalse();
  }

  // --- arguments --------------------------------------------------------------------------------------

  private static McpArguments Args(object values) =>
    new(JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()));

  [Fact]
  public void McpArguments_ParseDatesIntsAndWords()
  {
    var today = new DateOnly(2026, 10, 30);
    var args = Args(new { a = "2026-10-24", b = "today", c = "yesterday", d = 28, e = "90", f = "  " });

    args.Date("a", today).Should().Be(new DateOnly(2026, 10, 24));
    args.Date("b", today).Should().Be(today);
    args.Date("c", today).Should().Be(today.AddDays(-1));
    args.Int("d").Should().Be(28);
    args.Int("e").Should().Be(90);
    args.String("f").Should().BeNull();
    args.String("missing").Should().BeNull();
  }

  [Fact]
  public void McpArguments_BadValues_RaiseAPlainMessage()
  {
    var args = Args(new { day = "last tuesday", n = "many" });

    ((Action)(() => args.Date("day", new DateOnly(2026, 10, 30)))).Should().Throw<McpToolException>().WithMessage("*date like*");
    ((Action)(() => args.Int("n"))).Should().Throw<McpToolException>().WithMessage("*whole number*");
    ((Action)(() => args.RequiredString("absent"))).Should().Throw<McpToolException>().WithMessage("absent is required.");
  }

  // --- the policy layer ------------------------------------------------------------------------------

  private sealed class EchoTool(bool readOnly, bool acceptsPerson) : IMcpTool
  {
    public string Name => readOnly ? "echo" : "write";
    public string Description => "test";
    public bool ReadOnly => readOnly;
    public bool AcceptsPerson => acceptsPerson;
    public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>
    {
      ["word"] = new("string", "A word.", Required: true),
    };

    public McpToolContext? Seen { get; private set; }

    public Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
    {
      Seen = context;
      return Task.FromResult(new McpToolOutput(new { word = arguments.RequiredString("word"), whose = context.Target.DisplayName }, 1));
    }
  }

  private static readonly Guid UserId = Guid.NewGuid();
  private static readonly Guid PersonId = Guid.NewGuid();
  private static readonly Guid OtherPerson = Guid.NewGuid();

  private static ClaimsPrincipal Principal(string scope) => new(new ClaimsIdentity(
  [
    new Claim("sub", UserId.ToString()),
    new Claim("person_id", PersonId.ToString()),
    new Claim("scope", scope),
    new Claim("client_id", "dcr_test"),
    new Claim("client_name", "Open WebUI"),
  ], "test"));

  private static (McpToolService Service, Mock<IAuditService> Audit) Service(params IMcpTool[] tools)
  {
    var households = new Mock<IHouseholdService>();
    households.Setup(h => h.GetForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new HouseholdDto(Guid.NewGuid(), "Home",
    [
      new MemberDto(PersonId, UserId, "Jonathan Barnell", HouseholdRole.Admin, DateTime.UtcNow),
      new MemberDto(OtherPerson, Guid.NewGuid(), "Sam Barnell", HouseholdRole.Member, DateTime.UtcNow),
    ]));
    var audit = new Mock<IAuditService>();
    return (new McpToolService(tools, households.Object, audit.Object, NullLogger<McpToolService>.Instance), audit);
  }

  private static ModelContextProtocol.Protocol.CallToolRequestParams Call(string name, object args) => new()
  {
    Name = name,
    Arguments = JsonSerializer.SerializeToElement(args).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
  };

  private static string Text(ModelContextProtocol.Protocol.CallToolResult result) =>
    ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text;

  [Fact]
  public void Schema_AddsPersonOnlyForToolsThatAcceptIt()
  {
    var withPerson = McpToolService.Schema(new EchoTool(true, true));
    var without = McpToolService.Schema(new EchoTool(true, false));

    withPerson.GetProperty("properties").TryGetProperty("person", out _).Should().BeTrue();
    without.GetProperty("properties").TryGetProperty("person", out _).Should().BeFalse();
    withPerson.GetProperty("required")[0].GetString().Should().Be("word");
  }

  [Fact]
  public async Task Call_WithoutTheScope_IsRefused_AndAudited()
  {
    var (service, audit) = Service(new EchoTool(readOnly: false, acceptsPerson: false));

    var result = await service.CallToolAsync(Principal("health:read"), Call("write", new { word = "hi" }), "10.0.0.1");

    result.IsError.Should().BeTrue();
    Text(result).Should().Contain("memory:write");
    audit.Verify(a => a.RecordAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never, "a refused scope never reaches a tool");
  }

  [Fact]
  public async Task Call_ResolvesAMemberByFirstName_CaseInsensitively_AndAuditsWithoutValues()
  {
    var tool = new EchoTool(true, true);
    var (service, audit) = Service(tool);

    var result = await service.CallToolAsync(Principal("health:read"), Call("echo", new { word = "secret-value", person = "sam" }), "10.0.0.1");

    result.IsError.Should().BeFalse(Text(result));
    tool.Seen!.Target.PersonId.Should().Be(OtherPerson);
    tool.Seen.Target.IsSelf.Should().BeFalse();
    audit.Verify(a => a.RecordAsync(
      It.Is<AuditEvent>(e => e.Action == AuditActions.McpToolCall && e.TargetId == "echo" && !JsonSerializer.Serialize(e.Details, (JsonSerializerOptions?)null).Contains("secret-value")),
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task Call_NamingYourself_ReadsYourOwnData()
  {
    var tool = new EchoTool(true, true);
    var (service, _) = Service(tool);

    await service.CallToolAsync(Principal("health:read"), Call("echo", new { word = "x", person = "Jonathan" }), null);

    tool.Seen!.Target.IsSelf.Should().BeTrue();
    tool.Seen.Target.PersonId.Should().Be(PersonId);
  }

  [Fact]
  public async Task Call_UnknownPerson_OrPersonOnAnOwnOnlyTool_IsAPlainError()
  {
    var (service, _) = Service(new EchoTool(true, true));
    var unknown = await service.CallToolAsync(Principal("health:read"), Call("echo", new { word = "x", person = "Mallory" }), null);
    unknown.IsError.Should().BeTrue();
    Text(unknown).Should().Contain("Members: Jonathan Barnell, Sam Barnell");

    var (ownOnly, _) = Service(new EchoTool(true, false));
    var refused = await ownOnly.CallToolAsync(Principal("health:read"), Call("echo", new { word = "x", person = "Sam" }), null);
    refused.IsError.Should().BeTrue();
    Text(refused).Should().Contain("leave out person");
  }

  [Fact]
  public async Task Call_WithoutACaller_OrAnUnknownTool_IsAnError()
  {
    var (service, _) = Service(new EchoTool(true, true));

    (await service.CallToolAsync(new ClaimsPrincipal(new ClaimsIdentity()), Call("echo", new { word = "x" }), null)).IsError.Should().BeTrue();
    (await service.CallToolAsync(Principal("health:read"), Call("nope", new { }), null)).IsError.Should().BeTrue();
  }
}
