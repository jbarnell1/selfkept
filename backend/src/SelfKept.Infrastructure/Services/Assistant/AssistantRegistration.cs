using Microsoft.Extensions.DependencyInjection;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services.Assistant.Lookup;

namespace SelfKept.Infrastructure.Services.Assistant;

public static class AssistantRegistration
{
  /// <summary>
  /// The in-app assistant. Its outbound calls are the local model (the existing "ai" client), the
  /// person's own ntfy topic (the "ntfy" client below, which follows no redirects) and, only when
  /// the owner set Lookup:Enabled and a key and the person sends one, the cloud lookup (the
  /// "lookup" client, to the provider's API alone; docs/lookup-design.md).
  /// </summary>
  public static IServiceCollection AddAssistant(this IServiceCollection services)
  {
    services.AddScoped<IAssistantRepository, AssistantRepository>();
    services.AddScoped<INudgeRepository, NudgeRepository>();
    services.AddSingleton<IJournalCapture, JournalCapture>();
    services.AddSingleton<IReplyGuard, ReplyGuard>();
    services.AddScoped<CaptureSaver>();
    services.AddScoped<ModelRewording>();
    services.AddScoped<IPlaybookService, PlaybookService>();
    services.AddScoped<IStateBriefBuilder, StateBriefBuilder>();
    services.AddScoped<IAssistantService, AssistantService>();
    services.AddScoped<ICheckinFlowService, CheckinFlowService>();
    services.AddScoped<IMorningBriefService, MorningBriefService>();
    services.AddScoped<INudgeService, NudgeService>();
    services.AddScoped<IModelCheckService, ModelCheckService>();
    services.AddScoped<IPhotoAnalyzer, PhotoAnalyzer>();
    services.AddHttpClient(NtfySender.HttpClientName)
      .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    services.AddScoped<INtfySender, NtfySender>();
    services.AddJobLockedHostedService<NudgeJob>();

    services.AddOptions<LookupOptions>().BindConfiguration(LookupOptions.SectionName);
    services.AddScoped<ILookupRepository, LookupRepository>();
    // The SDK keeps its own timeout (Lookup:TimeoutSeconds); the HttpClient's would cut it at 100 seconds.
    services.AddHttpClient(AnthropicLookupClient.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan)
      .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    services.AddSingleton<ICloudLookupClient, AnthropicLookupClient>();
    services.AddScoped<ILookupService, LookupService>();
    return services;
  }
}
