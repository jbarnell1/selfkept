using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SelfKept.Api.Authentication;
using SelfKept.Api.Endpoints;
using SelfKept.Api.Extensions;
using SelfKept.Api.Middleware;
using SelfKept.Core.Auth;
using SelfKept.Core.Configuration;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Configuration;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Services.Connect;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure;
using Serilog.Events;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configuration order (later wins): appsettings.json, appsettings.Brand.json (generated from
// brand/brand.yaml), appsettings.{Environment}.json, user secrets, SelfKept:SecretsDirectory
// files, environment variables (Section__Key), command line.
InsertBrandSettings(builder.Configuration);
var secretsDirectory = SecretsDirectory.Apply(builder.Configuration, builder.Configuration);

var brand = builder.Configuration.GetSection(BrandOptions.SectionName).Get<BrandOptions>() ?? new BrandOptions();
builder.Services.AddSingleton(brand);
builder.Services.Configure<BrandOptions>(builder.Configuration.GetSection(BrandOptions.SectionName));

// Console-only logging: a self-hosted container's logs belong to `docker logs`.
Log.Logger = new LoggerConfiguration()
  .ReadFrom.Configuration(builder.Configuration)
  .MinimumLevel.Information()
  .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
  .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
  .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
  .MinimumLevel.Override("System", LogEventLevel.Warning)
  // Data protection keys sit in the data volume beside the database; the "no XML encryptor"
  // warning on every start says nothing the volume's own protection does not already cover.
  .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Error)
  .Enrich.FromLogContext()
  .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
  .CreateLogger();
builder.Host.UseSerilog();

Log.Information("{ProductName} API {Version} starting ({Environment})", brand.ProductName, AppVersion.Current, builder.Environment.EnvironmentName);
Log.Information("{Tagline}", brand.Tagline);
if (secretsDirectory != null) Log.Information("Secrets directory: {Directory}", secretsDirectory);
if (builder.Configuration["App:BaseUrl"] is { Length: > 0 } baseUrl) Log.Information("Public origin (App:BaseUrl): {BaseUrl}", baseUrl);

// Refuse to start with a missing or placeholder Jwt:Key / Encryption:Key. The dev-only keys in
// appsettings.Development.json are accepted in Development and nowhere else.
StartupSecretsValidator.ValidateOrThrow(builder.Configuration, builder.Environment.EnvironmentName);

var inMemoryDatabase = DependencyInjection.UsesInMemoryDatabase(builder.Configuration);
if (inMemoryDatabase) Log.Warning("Database:Provider is inmemory: no migrations, no job lock. Tests only.");

builder.Services.AddHttpContextAccessor();

// Keep ASP.NET's data protection keys across container restarts: in the data volume when there is
// one (the image's read-only file system has nowhere else to keep them).
if (builder.Configuration["SelfKept:DataDirectory"] is { Length: > 0 } keysRoot)
{
  builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(keysRoot, "data-protection-keys")))
    .SetApplicationName(brand.Codename);
}
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
  .AddJsonOptions(options =>
  {
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    // Enums travel as lowercase words: "admin", "activity", "metric". Clients may send any casing.
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
  });

// RFC 7807 everywhere: model validation (automatic 400s), status-code pages for empty 401/403/404
// bodies, and the error middleware for exceptions. traceId lets a log line be matched to a reply.
builder.Services.AddProblemDetails(options =>
  options.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);

// Rate limits per policy (Extensions/RateLimiting.cs).
builder.Services.AddAppRateLimiting();

// First-party JWT bearer. Claims keep their compact names (sub, role, person_id, household_id).
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
var mcpSettings = McpSettings.From(builder.Configuration);
builder.Services.AddAuthentication(options =>
  {
    // Route by path: /mcp takes only MCP tokens (OAuth or personal), everything else only the
    // app's own JWT. Neither kind is ever valid on the other surface.
    options.DefaultScheme = McpAuthentication.RoutingScheme;
    options.DefaultChallengeScheme = McpAuthentication.RoutingScheme;
  })
  .AddPolicyScheme(McpAuthentication.RoutingScheme, McpAuthentication.RoutingScheme, options =>
  {
    options.ForwardDefaultSelector = context =>
      McpAuthentication.IsMcpPath(context) ? McpAuthentication.McpScheme : JwtBearerDefaults.AuthenticationScheme;
  })
  .AddMcpSchemes(mcpSettings)
  .AddJwtBearer(options =>
  {
    options.RequireHttpsMetadata = false; // TLS is the reverse proxy's job on a home server
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
      ValidateIssuerSigningKey = true,
      IssuerSigningKey = signingKey,
      ValidateIssuer = true,
      ValidIssuer = jwtSection["Issuer"] ?? "api",
      ValidateAudience = true,
      ValidAudience = jwtSection["Audience"] ?? "clients",
      ValidateLifetime = true,
      ClockSkew = TimeSpan.Zero,
      NameClaimType = TokenClaims.Subject,
      RoleClaimType = TokenClaims.Role,
    };
    options.Events = new JwtBearerEvents
    {
      // A signature alone is not enough: a token for a deleted account must 401 so the client
      // signs out, instead of every controller answering with empty data.
      OnTokenValidated = async context =>
      {
        var sub = context.Principal?.FindFirst(TokenClaims.Subject)?.Value;
        if (!Guid.TryParse(sub, out var userId))
        {
          context.Fail("Token has no usable subject.");
          return;
        }
        var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        if (await users.GetByIdAsync(userId, context.HttpContext.RequestAborted) == null)
        {
          context.Fail("The account no longer exists.");
        }
      },
    };
  });
builder.Services.AddAuthorization(options =>
  options.AddPolicy(McpAuthentication.Policy, policy =>
  {
    policy.AuthenticationSchemes = [McpAuthentication.McpScheme];
    policy.RequireAuthenticatedUser();
  }));

// Assistant connections over MCP (docs/connect-design.md). They need a known public origin for the
// OAuth issuer; without App:BaseUrl (or Mcp:Issuer) the feature stays off and says so.
if (mcpSettings.IsConfigured)
{
  builder.Services.AddMcpServerEndpoints(brand);
  Log.Information("Assistant connections: MCP at {Resource}, OAuth issuer {Issuer}", mcpSettings.ResourceUri, mcpSettings.Issuer);
}
else
{
  Log.Warning("Assistant connections are off: set App:BaseUrl (or Mcp:Issuer) to the address people use to reach this server.");
}

// CORS is only needed when the Flutter web dev server runs on its own port. In production the API
// serves the web build itself from wwwroot, same origin, so no CORS policy exists at all.
const string DevCorsPolicy = "DevWebClient";
if (builder.Environment.IsDevelopment())
{
  builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
    .WithOrigins("http://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));
}

var openApiEnabled = builder.Configuration.GetValue<bool?>("OpenApi:Enabled") ?? builder.Environment.IsDevelopment();
if (openApiEnabled)
{
  builder.Services.AddEndpointsApiExplorer();
  builder.Services.AddSwaggerGen(c =>
  {
    c.SwaggerDoc("v1", new OpenApiInfo
    {
      Title = $"{brand.ProductName} API",
      Version = AppVersion.Current,
      Description = brand.Tagline,
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
      Description = "JWT bearer token. Example: \"Authorization: Bearer {token}\"",
      Name = "Authorization",
      In = ParameterLocation.Header,
      Type = SecuritySchemeType.Http,
      Scheme = "bearer",
      BearerFormat = "JWT",
    });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
      [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
  });
}

var app = builder.Build();

// First middleware: behind a TLS-terminating proxy, honor X-Forwarded-For / X-Forwarded-Proto,
// but only from the proxies named in ForwardedHeaders:TrustedProxies / TrustedNetworks. With
// neither set, no sender is trusted and the headers are ignored.
{
  var options = new ForwardedHeadersOptions
  {
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
  };
  var forwardLimit = app.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit");
  if (forwardLimit.HasValue) options.ForwardLimit = forwardLimit.Value;
  options.KnownIPNetworks.Clear();
  options.KnownProxies.Clear();
  var proxies = app.Configuration.GetSection("ForwardedHeaders:TrustedProxies").Get<string[]>() ?? [];
  var networks = app.Configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>() ?? [];
  foreach (var proxy in proxies.Where(p => !string.IsNullOrWhiteSpace(p)))
  {
    options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy.Trim()));
  }
  foreach (var network in networks.Where(n => !string.IsNullOrWhiteSpace(n)))
  {
    var parts = network.Trim().Split('/');
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse(parts[0]), int.Parse(parts[1])));
  }
  Log.Information("ForwardedHeaders: trusting {Proxies} proxies and {Networks} networks", options.KnownProxies.Count, options.KnownIPNetworks.Count);
  app.UseForwardedHeaders(options);
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseStatusCodePages(); // problem+json bodies for empty 401/403/404 replies

// Apply pending SQL migrations before taking traffic. A failed migration stops the process in
// every environment: serving requests on a half-migrated schema is never useful.
if (!inMemoryDatabase)
{
  var connectionString = app.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName)!;
  var migrationLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SelfKept.Database.Migrations");
  var migrationSettings = app.Configuration.GetSection("Migrations").Get<MigrationSettings>() ?? new MigrationSettings();
  // Anything written to disk goes under SelfKept:DataDirectory (the container mounts /data there).
  if (string.IsNullOrWhiteSpace(app.Configuration["Migrations:BackupDirectory"])
      && app.Configuration["SelfKept:DataDirectory"] is { Length: > 0 } dataDirectory)
  {
    migrationSettings.BackupDirectory = Path.Combine(dataDirectory, "migration-backups");
  }
  SqlMigrationRunner.Run(connectionString, migrationLogger, migrationSettings);
}

if (openApiEnabled)
{
  app.UseSwagger();
  app.UseSwaggerUI(c =>
  {
    c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{brand.ProductName} API {AppVersion.Current}");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = $"{brand.ProductName} API";
  });
}

// The Flutter web build lives in wwwroot and is served from the same origin as the API.
app.UseWebClient();

if (app.Environment.IsDevelopment())
{
  app.UseCors(DevCorsPolicy);
}

app.UseAuthentication();
app.UseAuthorization();
// After authentication so the "api" policy can partition per user instead of per proxy IP.
app.UseRateLimiter();

app.MapControllers();
if (mcpSettings.IsConfigured)
{
  app.MapMcpEndpoints(mcpSettings, brand);
  if (!inMemoryDatabase)
  {
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IOAuthService>().UpsertConfiguredClientsAsync();
  }
}
app.MapWebClientFallback();

app.Run();

// --- helpers -------------------------------------------------------------------------------

// appsettings.Brand.json goes right after appsettings.json so the environment file, secrets
// directory and environment variables can still override a brand value.
static void InsertBrandSettings(ConfigurationManager configuration)
{
  var sources = configuration.Sources;
  var index = -1;
  for (var i = 0; i < sources.Count; i++)
  {
    if (sources[i] is JsonConfigurationSource { Path: "appsettings.json" })
    {
      index = i;
      break;
    }
  }
  var brandSource = new JsonConfigurationSource { Path = "appsettings.Brand.json", Optional = false, ReloadOnChange = false };
  if (index >= 0) sources.Insert(index + 1, brandSource);
  else sources.Add(brandSource);
}

// Make Program visible to WebApplicationFactory in the integration tests.
public partial class Program { }
