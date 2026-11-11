using Microsoft.Extensions.DependencyInjection;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Repositories;

namespace SelfKept.Infrastructure.Services.Assistant;

public static class AssistantRegistration
{
  /// <summary>
  /// The in-app assistant. Its only outbound calls are the local model (the existing "ai" client)
  /// and the person's own ntfy topic (the "ntfy" client below, which follows no redirects).
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
    services.AddHttpClient(NtfySender.HttpClientName)
      .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    services.AddScoped<INtfySender, NtfySender>();
    services.AddJobLockedHostedService<NudgeJob>();
    return services;
  }
}
