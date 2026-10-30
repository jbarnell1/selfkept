using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace SelfKept.Infrastructure.Services.Connect;

/// <summary>A client named in configuration (Mcp:Clients), written to oauth_client at startup.</summary>
public sealed class ConfiguredMcpClient
{
  public string ClientId { get; set; } = string.Empty;
  public string ClientName { get; set; } = string.Empty;
  public string? ClientUri { get; set; }
  public List<string> RedirectUris { get; set; } = [];
}

/// <summary>
/// The MCP and OAuth settings, resolved once from configuration (docs/connect-design.md).
///
/// <list type="bullet">
///   <item><c>Mcp:Enabled</c>: default true. False maps no /mcp route and no OAuth endpoints.</item>
///   <item><c>Mcp:Issuer</c>: the origin the API answers on, used as the OAuth issuer and for the
///   endpoint URLs in the metadata. Defaults to <c>App:BaseUrl</c> (on a self-hosted box the API and
///   the web app share one origin). Without either, MCP stays off and says so in the log.</item>
///   <item><c>Mcp:ResourceUri</c>: the protected resource; defaults to the issuer plus <c>/mcp</c>.</item>
///   <item><c>App:BaseUrl</c>: where the web app lives; the consent page is <c>{App:BaseUrl}/connect</c>.</item>
/// </list>
///
/// MCP access tokens are signed with a key derived from <c>Jwt:Key</c> by HKDF with its own label,
/// so an MCP token can never validate as an app token or the other way round, with no new secret.
/// </summary>
public sealed class McpSettings
{
  public bool Enabled { get; init; }
  public string? Issuer { get; init; }
  public string? ResourceUri { get; init; }
  public string? AppBaseUrl { get; init; }
  public IReadOnlyList<ConfiguredMcpClient> Clients { get; init; } = [];
  public byte[] SigningKey { get; init; } = [];

  public bool IsConfigured => Enabled && Issuer != null && ResourceUri != null;

  public string ResourceMetadataUrl => $"{Issuer}/.well-known/oauth-protected-resource/mcp";

  public string ConsentUrl(Guid requestId) => $"{(AppBaseUrl ?? Issuer)}/connect?request={requestId}";

  private static readonly byte[] SigningSalt = Encoding.UTF8.GetBytes("mcp-access-token");
  private static readonly byte[] SigningInfo = Encoding.UTF8.GetBytes("hs256 signing key v1");

  public static McpSettings From(IConfiguration configuration)
  {
    var enabled = configuration.GetValue<bool?>("Mcp:Enabled") ?? true;
    var issuer = Normalise(configuration["Mcp:Issuer"]) ?? Normalise(configuration["App:BaseUrl"]);
    var resource = Normalise(configuration["Mcp:ResourceUri"]) ?? (issuer == null ? null : $"{issuer}/mcp");
    var appBase = Normalise(configuration["App:BaseUrl"]) ?? issuer;
    var clients = configuration.GetSection("Mcp:Clients").Get<List<ConfiguredMcpClient>>() ?? [];

    var jwtKey = configuration["Jwt:Key"] ?? string.Empty;
    var signingKey = jwtKey.Length == 0
      ? []
      : HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwtKey), 32, SigningSalt, SigningInfo);

    return new McpSettings
    {
      Enabled = enabled,
      Issuer = issuer,
      ResourceUri = resource,
      AppBaseUrl = appBase,
      Clients = clients,
      SigningKey = signingKey,
    };
  }

  private static string? Normalise(string? url) =>
    string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
}
