using ModelContextProtocol.Protocol;
using SelfKept.Api.Authentication;
using SelfKept.Api.Extensions;
using SelfKept.Core.Configuration;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Mcp;
using SelfKept.Infrastructure.Services.Connect;

namespace SelfKept.Api.Endpoints;

/// <summary>
/// SDK wiring for /mcp and the OAuth discovery documents. Thin on purpose: what the tools are and
/// how a call is checked lives in <see cref="McpToolService"/>. Stateless Streamable HTTP, the one
/// transport Open WebUI speaks natively.
/// </summary>
public static class McpEndpoints
{

  /// <summary>Returned at initialize: the voice and the mental model, not tool docs (tools carry their own).</summary>
  public static string Instructions(BrandOptions brand) =>
    $"""
    You are connected to {brand.ProductName}, a self-hosted health companion for a household. It holds
    what the person's phone and watch recorded (sleep, heart rate, HRV, steps, workouts, temperature),
    their Oura ring data when connected, their monthly blood panel results, and short mood check-ins
    with notes. The data stays on the household's own server.

    How to read it:
    - Compare a person with their own usual range first (get_trend gives the 28-day band) and with
      population ranges second. A value inside the person's band is normal for them even when it
      looks low next to an average.
    - Insight cards were raised by fixed rules over the person's own data. Severity is notice, watch,
      or clinician; clinician means the pattern is worth raising with a clinician.
    - Lab ranges are approximate adult population ranges; the lab's own range on the report wins.
    - For "since my last blood test" questions, read the panel date from get_lab_markers, then use
      compare_periods.
    - Call recall_memory early: the person may have told an assistant about goals, conditions or
      medications. Use remember only for facts the person states, never for guesses.
    - Pass person to read a household member's data; it works only for what they chose to share.

    How to speak: calm, plain and specific, with the numbers exactly as the tools return them. Never
    diagnose. Say "this pattern often goes with" and suggest raising it with a clinician when a card
    is at clinician severity or something looks off. If data is missing, say so and use get_sources
    to explain the gap.
    """;

  public static IServiceCollection AddMcpServerEndpoints(this IServiceCollection services, BrandOptions brand)
  {
    services
      .AddMcpServer(options =>
      {
        options.ServerInfo = new Implementation { Name = brand.ProductName, Version = Extensions.AppVersion.Current };
        options.ServerInstructions = Instructions(brand);
      })
      .WithHttpTransport(options => options.Stateless = true)
      .WithListToolsHandler((context, _) =>
      {
        var service = context.Services!.GetRequiredService<McpToolService>();
        return ValueTask.FromResult(service.ListTools());
      })
      .WithCallToolHandler(async (context, ct) =>
      {
        var service = context.Services!.GetRequiredService<McpToolService>();
        var ip = context.Services!.GetService<IHttpContextAccessor>()?.HttpContext?.Connection.RemoteIpAddress?.ToString();
        return await service.CallToolAsync(context.User, context.Params, ip, ct);
      });
    return services;
  }

  public static void MapMcpEndpoints(this WebApplication app, McpSettings settings, BrandOptions brand)
  {
    app.MapMcp("/mcp")
      .RequireAuthorization(McpAuthentication.Policy)
      .RequireRateLimiting(RateLimitPolicies.Mcp);

    // RFC 9728 protected resource metadata. Served at the root and at the /mcp suffix because
    // clients differ in which they try.
    IResult ProtectedResource() => Results.Json(new Dictionary<string, object?>
    {
      ["resource"] = settings.ResourceUri,
      ["authorization_servers"] = new[] { settings.Issuer },
      ["scopes_supported"] = ConnectScopes.All,
      ["bearer_methods_supported"] = new[] { "header" },
      ["resource_name"] = brand.ProductName,
    });
    app.MapGet("/.well-known/oauth-protected-resource", ProtectedResource).AllowAnonymous();
    app.MapGet("/.well-known/oauth-protected-resource/mcp", ProtectedResource).AllowAnonymous();

    // RFC 8414 authorization server metadata, also under the OpenID path some clients probe.
    IResult AuthorizationServer() => Results.Json(new Dictionary<string, object?>
    {
      ["issuer"] = settings.Issuer,
      ["authorization_endpoint"] = $"{settings.Issuer}/api/oauth/authorize",
      ["token_endpoint"] = $"{settings.Issuer}/api/oauth/token",
      ["registration_endpoint"] = $"{settings.Issuer}/api/oauth/register",
      ["response_types_supported"] = new[] { "code" },
      ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
      ["code_challenge_methods_supported"] = new[] { "S256" },
      ["token_endpoint_auth_methods_supported"] = OAuthAuthMethods.All,
      ["scopes_supported"] = ConnectScopes.All,
      ["client_id_metadata_document_supported"] = true,
      ["service_documentation"] = brand.DocsUrl,
    });
    app.MapGet("/.well-known/oauth-authorization-server", AuthorizationServer).AllowAnonymous();
    app.MapGet("/.well-known/oauth-authorization-server/mcp", AuthorizationServer).AllowAnonymous();
    app.MapGet("/.well-known/openid-configuration", AuthorizationServer).AllowAnonymous();
  }
}
