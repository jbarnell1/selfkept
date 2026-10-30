using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SelfKept.Core.Auth;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Mcp;
using SelfKept.Infrastructure.Services.Connect;

namespace SelfKept.Api.Authentication;

/// <summary>
/// Authentication for /mcp (docs/connect-design.md). Two token kinds reach it: an OAuth access
/// token (a JWT signed with the MCP key, iss = Mcp:Issuer, aud = the resource) and a personal
/// access token (opaque, "pat_" prefix). Neither is accepted on /api, and an app token is never
/// accepted on /mcp: a path-routing default scheme sends /mcp requests to these schemes and
/// everything else to the app's own JWT bearer.
/// </summary>
public static class McpAuthentication
{
  /// <summary>The default scheme: routes by path.</summary>
  public const string RoutingScheme = "Routing";

  /// <summary>Routes a /mcp request to the right MCP scheme by the token's shape.</summary>
  public const string McpScheme = "Mcp";

  public const string McpBearerScheme = "McpBearer";
  public const string PersonalTokenScheme = "McpPersonalToken";

  public const string Policy = "Mcp";

  public static bool IsMcpPath(HttpContext context) => context.Request.Path.StartsWithSegments("/mcp");

  public static AuthenticationBuilder AddMcpSchemes(this AuthenticationBuilder builder, McpSettings settings)
  {
    builder.AddPolicyScheme(McpScheme, McpScheme, options =>
    {
      options.ForwardDefaultSelector = context =>
        PersonalTokenService.LooksLikePersonalToken(BearerToken(context)) ? PersonalTokenScheme : McpBearerScheme;
    });

    builder.AddJwtBearer(McpBearerScheme, options =>
    {
      options.RequireHttpsMetadata = false; // TLS is the proxy's job on a home server
      options.MapInboundClaims = false;
      options.TokenValidationParameters = new TokenValidationParameters
      {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(settings.SigningKey.Length > 0 ? settings.SigningKey : new byte[32]),
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer ?? "mcp-not-configured",
        ValidateAudience = true,
        ValidAudience = settings.ResourceUri ?? "mcp-not-configured",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = TokenClaims.Subject,
      };
      options.Events = new JwtBearerEvents
      {
        OnTokenValidated = async context =>
        {
          var sub = context.Principal?.FindFirst(TokenClaims.Subject)?.Value;
          var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
          if (!Guid.TryParse(sub, out var userId) || await users.GetByIdAsync(userId, context.HttpContext.RequestAborted) == null)
            context.Fail("The account no longer exists.");
        },
        // RFC 9728: a 401 from /mcp points the client at the protected resource metadata, which is
        // how Open WebUI and Claude find the authorization server.
        OnChallenge = async context =>
        {
          context.HandleResponse();
          await WriteChallengeAsync(context.HttpContext, settings, context.AuthenticateFailure != null || context.Request.Headers.Authorization.Count > 0);
        },
      };
    });

    builder.AddScheme<AuthenticationSchemeOptions, PersonalTokenHandler>(PersonalTokenScheme, _ => { });
    return builder;
  }

  public static async Task WriteChallengeAsync(HttpContext http, McpSettings settings, bool invalidToken)
  {
    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
    var header = new StringBuilder("Bearer");
    header.Append($" resource_metadata=\"{settings.ResourceMetadataUrl}\"");
    if (invalidToken) header.Append(", error=\"invalid_token\"");
    http.Response.Headers.WWWAuthenticate = header.ToString();
    http.Response.ContentType = "application/json";
    await http.Response.WriteAsync("{\"error\":\"invalid_token\",\"error_description\":\"Connect this assistant first.\"}");
  }

  public static string? BearerToken(HttpContext context)
  {
    var header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
  }
}

/// <summary>Validates a personal access token and issues the same claims as an MCP access token.</summary>
public sealed class PersonalTokenHandler(
  IOptionsMonitor<AuthenticationSchemeOptions> options,
  ILoggerFactory logger,
  UrlEncoder encoder,
  McpSettings settings) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
  protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    var token = McpAuthentication.BearerToken(Context);
    if (!PersonalTokenService.LooksLikePersonalToken(token))
      return AuthenticateResult.NoResult();

    var tokens = Context.RequestServices.GetRequiredService<IPersonalTokenService>();
    var identity = await tokens.ValidateAsync(token!, Context.RequestAborted);
    if (identity == null)
      return AuthenticateResult.Fail("Unknown or revoked token.");

    var claims = new List<Claim>
    {
      new(TokenClaims.Subject, identity.UserId.ToString()),
      new(TokenClaims.Person, identity.PersonId.ToString()),
      new(TokenClaims.Scope, identity.Scope),
      new(TokenClaims.ClientId, $"pat:{identity.TokenId}"),
      new(TokenClaims.ClientName, identity.Name),
    };
    if (identity.HouseholdId is { } household)
      claims.Add(new Claim(TokenClaims.Household, household.ToString()));
    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, TokenClaims.Subject, null));
    return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
  }

  protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
    McpAuthentication.WriteChallengeAsync(Context, settings, invalidToken: true);
}
