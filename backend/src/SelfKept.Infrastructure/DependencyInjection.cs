using System.Net.Sockets;
using System.Net;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Mcp.Tools;
using SelfKept.Infrastructure.Mcp;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Security;
using SelfKept.Infrastructure.Services.Collect;
using SelfKept.Infrastructure.Services.Connect;
using SelfKept.Infrastructure.Services.Export;
using SelfKept.Infrastructure.Services.Sso;
using SelfKept.Infrastructure.Services.Interpret.Ai;
using SelfKept.Infrastructure.Services.Interpret;
using SelfKept.Infrastructure.Services.Labs;
using SelfKept.Infrastructure.Services.Oura;
using SelfKept.Infrastructure.Services;

namespace SelfKept.Infrastructure;

public static class DependencyInjection
{
  /// <summary>Name of the connection string: ConnectionStrings:SelfKept (env ConnectionStrings__SelfKept).</summary>
  public const string ConnectionStringName = "SelfKept";

  /// <summary>Database:Provider = postgres (default) or inmemory (tests only; no migrations, no job lock).</summary>
  public const string ProviderKey = "Database:Provider";

  public static bool UsesInMemoryDatabase(IConfiguration configuration) =>
    string.Equals(configuration[ProviderKey], "inmemory", StringComparison.OrdinalIgnoreCase);

  public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
  {
    services.TryAddTimeProvider();

    var inMemory = UsesInMemoryDatabase(configuration);
    var connectionString = configuration.GetConnectionString(ConnectionStringName);

    if (inMemory)
    {
      var databaseName = configuration["Database:InMemoryName"] ?? "selfkept-tests";
      services.AddDbContextFactory<SelfKeptDbContext>(options => options
        .UseInMemoryDatabase(databaseName)
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
    }
    else
    {
      if (string.IsNullOrWhiteSpace(connectionString))
      {
        throw new InvalidOperationException(
          $"ConnectionStrings:{ConnectionStringName} is not set (environment variable ConnectionStrings__{ConnectionStringName}).");
      }
      services.AddDbContextFactory<SelfKeptDbContext>(options => options.UseNpgsql(connectionString));
    }

    // AddDbContextFactory registers the factory as a singleton and the context itself as scoped,
    // so request code injects SelfKeptDbContext while token and audit writers take the factory.

    services.AddScoped<IUnitOfWork, EfUnitOfWork>();
    services.AddScoped<IUserRepository, UserRepository>();
    services.AddScoped<IPersonRepository, PersonRepository>();
    services.AddScoped<IHouseholdRepository, HouseholdRepository>();
    services.AddScoped<IInviteRepository, InviteRepository>();
    services.AddScoped<IShareGrantRepository, ShareGrantRepository>();

    services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
    services.AddSingleton<ITokenService, JwtTokenService>();
    services.AddSingleton<IEncryptionService, EncryptionService>();
    services.AddScoped<IRefreshTokenService, RefreshTokenService>();
    services.AddScoped<IAuditService, AuditService>();
    services.AddScoped<IAccessPolicy, ShareGrantAccessPolicy>();
    services.AddScoped<IAuthService, AuthService>();
    services.AddScoped<IInviteService, InviteService>();
    services.AddScoped<IHouseholdService, HouseholdService>();
    services.AddScoped<IProfileService, ProfileService>();
    services.AddScoped<ISetupService, SetupService>();
    services.AddScoped<IIntegrationService, IntegrationService>();
    services.AddScoped<ServerSettingStore>();

    // Package 2 (Collect). The catalogue is a singleton loaded once at startup (MetricCatalogueLoader).
    services.AddSingleton<IMetricCatalogue, MetricCatalogue>();
    services.AddHostedService<MetricCatalogueLoader>();
    services.AddScoped<ISourceRepository, SourceRepository>();
    services.AddScoped<ISampleRepository, SampleRepository>();
    services.AddScoped<IWorkoutRepository, WorkoutRepository>();
    services.AddScoped<ICheckinRepository, CheckinRepository>();
    services.AddScoped<IDailySummaryRepository, DailySummaryRepository>();
    services.AddScoped<IDailySummaryService, DailySummaryService>();
    services.AddScoped<ISourceService, SourceService>();
    services.AddScoped<IIngestService, IngestService>();
    services.AddScoped<ICheckinService, CheckinService>();
    services.AddScoped<IDataService, DataService>();

    // Package 3 (Interpret). The model adapter is the app's only outbound HTTP, to the host the
    // user configured under Ai:, and only when Ai:Enabled is true.
    services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
    services.AddHttpClient(OpenAiCompatibleChatModel.HttpClientName);
    services.AddSingleton<IChatModel, OpenAiCompatibleChatModel>();
    services.AddScoped<IBaselineRepository, BaselineRepository>();
    services.AddScoped<IInsightRepository, InsightRepository>();
    services.AddScoped<IAiCallRepository, AiCallRepository>();
    services.AddScoped<ITodayCacheRepository, TodayCacheRepository>();
    services.AddScoped<IInsightPhraser, InsightPhraser>();
    services.AddScoped<IBaselineService, BaselineService>();
    foreach (var rule in RuleEngine.DefaultRules()) services.AddSingleton(rule);
    services.AddSingleton<IRuleEngine, RuleEngine>();
    services.AddScoped<IInterpretRunner, InterpretRunner>();
    services.AddScoped<ITodayService, TodayService>();
    services.AddScoped<IInsightService, InsightService>();
    services.AddScoped<ITrendService, TrendService>();

    // Package 4 (Labs). The reference and alias tables are singletons loaded once at startup.
    services.AddSingleton<ILabReferenceRepository, LabReferenceRepository>();
    services.AddSingleton<IMarkerAliasRepository, MarkerAliasRepository>();
    services.AddHostedService<LabCatalogueLoader>();
    services.AddScoped<ILabReportRepository, LabReportRepository>();
    services.AddSingleton<LabSummaryPdfRenderer>();
    services.AddScoped<ILabService, LabService>();

    // Package 5 (Connect): the OAuth server for /mcp, personal tokens, assistant memory, the tools.
    services.AddSingleton(McpSettings.From(configuration));
    services.AddHttpClient(OAuthService.CimdHttpClientName)
      .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
      {
        AllowAutoRedirect = false,
        // Re-check the address actually dialled, so a DNS answer that changes between the check
        // and the connect cannot reach a private address.
        ConnectCallback = async (context, cancellationToken) =>
        {
          var host = context.DnsEndPoint.Host;
          var candidates = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
          var allowed = candidates.Where(PublicAddresses.IsPublic).ToArray();
          if (allowed.Length == 0)
            throw new HttpRequestException("The client metadata host does not resolve to a public address.");
          var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
          try
          {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
          }
          catch
          {
            socket.Dispose();
            throw;
          }
        },
      });
    services.AddScoped<IOAuthService, OAuthService>();
    services.AddScoped<IPersonalTokenService, PersonalTokenService>();
    services.AddScoped<IAiMemoryService, AiMemoryService>();
    services.AddScoped<IMcpTool, GetTodayTool>();
    services.AddScoped<IMcpTool, ListMetricsTool>();
    services.AddScoped<IMcpTool, GetDailySummaryTool>();
    services.AddScoped<IMcpTool, GetTrendTool>();
    services.AddScoped<IMcpTool, ComparePeriodsTool>();
    services.AddScoped<IMcpTool, GetInsightsTool>();
    services.AddScoped<IMcpTool, GetCheckinsTool>();
    services.AddScoped<IMcpTool, GetSourcesTool>();
    services.AddScoped<IMcpTool, GetLabMarkersTool>();
    services.AddScoped<IMcpTool, GetLabHistoryTool>();
    services.AddScoped<IMcpTool, RecallMemoryTool>();
    services.AddScoped<IMcpTool, RememberTool>();
    services.AddScoped<McpToolService>();

    // Oura: off until an admin enters an Oura application in the app, or Oura:ClientId and
    // Oura:ClientSecret are set in the environment.
    var ouraSettings = OuraSettings.From(configuration);
    services.AddSingleton(ouraSettings);
    services.AddHttpClient(OuraApi.HttpClientName, client =>
    {
      client.Timeout = TimeSpan.FromSeconds(30);
      // Oura may refuse a request without a User-Agent; a neutral one says what is calling.
      client.DefaultRequestHeaders.UserAgent.ParseAdd($"{configuration["Brand:Codename"] ?? "health-server"}/1.0");
    });
    services.AddScoped<OuraAppCredentials>();
    services.AddScoped<IOuraApi, OuraApi>();
    services.AddScoped<IOuraService, OuraService>();
    services.AddScoped<IExportService, ExportService>();

    // Provider sign-in (Pocket ID and the like): off until Sso:Authority, Sso:ClientId and
    // Sso:ClientSecret are set. The provider client is a singleton so it can cache the
    // discovery document and signing keys.
    services.AddSingleton(SsoSettings.From(configuration));
    services.AddHttpClient(SsoProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
    services.AddSingleton<ISsoProvider, SsoProvider>();
    services.AddScoped<ISsoService, SsoService>();

    // Scheduled jobs: only the copy holding the Postgres job lock runs them (see BackgroundJobLock.cs).
    services.AddBackgroundJobLock(inMemory ? null : connectionString);
    services.AddScoped<Housekeeping>();
    services.AddJobLockedHostedService<HousekeepingJob>();
    services.AddJobLockedHostedService<InterpretNightlyJob>();
    services.AddJobLockedHostedService<OuraPollJob>();

    return services;
  }

  private static void TryAddTimeProvider(this IServiceCollection services)
  {
    if (services.All(d => d.ServiceType != typeof(TimeProvider)))
    {
      services.AddSingleton(TimeProvider.System);
    }
  }
}
