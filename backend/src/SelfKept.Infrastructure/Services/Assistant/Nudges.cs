using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// POSTs to the ntfy topic the person set (https://docs.ntfy.sh/publish/): the message as the
/// body, the title as a header, the token as a bearer. No redirects are followed, so the request
/// only ever reaches the address the person typed. Failures come back as a short reason that
/// never includes the address or the token.
/// </summary>
public sealed class NtfySender : INtfySender
{
  public const string HttpClientName = "ntfy";

  private readonly IHttpClientFactory _httpClientFactory;

  public NtfySender(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

  public async Task<string?> SendAsync(string url, string? token, string title, string message, CancellationToken ct = default)
  {
    if (NudgeUrls.Validate(url) != null) return "The address is not a valid http or https address.";
    var client = _httpClientFactory.CreateClient(HttpClientName);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
      request.Content = new StringContent(message, Encoding.UTF8, "text/plain");
      request.Headers.TryAddWithoutValidation("Title", title);
      request.Headers.TryAddWithoutValidation("Priority", "default");
      if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
      using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
      return response.IsSuccessStatusCode ? null : $"The server answered {(int)response.StatusCode}.";
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (OperationCanceledException)
    {
      return "The server did not answer in time.";
    }
    catch (HttpRequestException)
    {
      return "The server could not be reached.";
    }
  }
}

/// <summary>Checks and masks an ntfy address. The path is the topic, which is the secret part, so only the scheme and host are ever shown.</summary>
public static class NudgeUrls
{
  public const int MaxLength = 500;

  /// <summary>Null when the address is usable, else the message for the person.</summary>
  public static string? Validate(string? url)
  {
    if (string.IsNullOrWhiteSpace(url)) return "Enter the address of your ntfy topic.";
    if (url.Length > MaxLength) return $"The address is at most {MaxLength} characters.";
    if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
      return "The address must start with http:// or https://.";
    }
    if (!string.IsNullOrEmpty(uri.UserInfo)) return "Put the token in the token field, not in the address.";
    if (uri.AbsolutePath.Trim('/').Length == 0) return "The address must include the topic, for example /evening.";
    return null;
  }

  public static string Mask(string url) =>
    Uri.TryCreate(url, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Authority}/***" : "***";
}

/// <summary>When a person's evening nudge is due: from their evening time until three hours after, in their own zone. Pure.</summary>
public static class NudgeSchedule
{
  public static readonly TimeSpan Window = TimeSpan.FromHours(3);

  /// <summary>The local day the nudge is for, or null outside the window.</summary>
  public static DateOnly? DueDay(DateTime utcNow, TimeZoneInfo zone, TimeOnly evening)
  {
    var local = TimeZoneInfo.ConvertTimeFromUtc(DayBoundaries.AsUtc(utcNow), zone);
    var day = DateOnly.FromDateTime(local);
    var start = day.ToDateTime(evening);
    var end = start + Window;
    if (local < start || local >= end) return null;
    return day;
  }
}

public sealed class NudgeService : INudgeService
{
  public const int MaxTokenLength = 200;

  private readonly INudgeRepository _nudges;
  private readonly IPersonRepository _persons;
  private readonly INtfySender _sender;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;

  public NudgeService(INudgeRepository nudges, IPersonRepository persons, INtfySender sender, IEncryptionService encryption, IAuditService audit, TimeProvider clock)
  {
    _nudges = nudges;
    _persons = persons;
    _sender = sender;
    _encryption = encryption;
    _audit = audit;
    _clock = clock;
  }

  public async Task<NudgeSettingsDto> GetAsync(Guid personId, CancellationToken ct = default)
  {
    var setting = await _nudges.GetOwnAsync(personId, ct);
    var person = await _persons.GetByIdAsync(personId, ct);
    return await ToDtoAsync(personId, setting, person?.TimeZone ?? "UTC", ct);
  }

  public async Task<NudgeSettingsDto> SaveAsync(Actor actor, SaveNudgeSettingsRequest request, CancellationToken ct = default)
  {
    var existing = await _nudges.GetOwnAsync(actor.PersonId, ct);
    var setting = existing ?? new NudgeSetting { PersonId = actor.PersonId };

    if (request.NtfyUrl != null)
    {
      var url = request.NtfyUrl.Trim();
      if (url.Length == 0)
      {
        setting.NtfyUrlCipher = null;
      }
      else
      {
        if (NudgeUrls.Validate(url) is { } problem) throw new DomainValidationException(problem);
        setting.NtfyUrlCipher = _encryption.Encrypt(url);
      }
    }
    if (request.NtfyToken != null)
    {
      var token = request.NtfyToken.Trim();
      if (token.Length > MaxTokenLength) throw new DomainValidationException($"The token is at most {MaxTokenLength} characters.");
      if (token.Any(char.IsWhiteSpace)) throw new DomainValidationException("The token cannot contain spaces or line breaks.");
      setting.NtfyTokenCipher = token.Length == 0 ? null : _encryption.Encrypt(token);
    }
    if (request.EveningTime != null)
    {
      if (!TimeOnly.TryParseExact(request.EveningTime.Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var evening))
      {
        throw new DomainValidationException("eveningTime must be HH:mm, for example 20:30.");
      }
      setting.EveningTime = evening;
    }
    if (request.Enabled && setting.NtfyUrlCipher == null) throw new DomainValidationException("Add the address of your ntfy topic before turning reminders on.");
    setting.Enabled = request.Enabled;
    setting.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
    await _nudges.SaveOwnAsync(setting, ct);

    await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.NudgesChanged, actor.UserId, actor.HouseholdId, "nudge_setting", actor.PersonId.ToString(),
      new { enabled = setting.Enabled, urlSet = setting.NtfyUrlCipher != null, tokenSet = setting.NtfyTokenCipher != null }, actor.Info.IpAddress), ct);
    var person = await _persons.GetByIdAsync(actor.PersonId, ct);
    return await ToDtoAsync(actor.PersonId, setting, person?.TimeZone ?? "UTC", ct);
  }

  public async Task<NudgeTestResultDto> TestAsync(Actor actor, CancellationToken ct = default)
  {
    var setting = await _nudges.GetOwnAsync(actor.PersonId, ct);
    var url = setting?.NtfyUrlCipher == null ? null : _encryption.TryDecrypt(setting.NtfyUrlCipher);
    if (url == null) throw new DomainValidationException("Add the address of your ntfy topic first.");
    var token = setting!.NtfyTokenCipher == null ? null : _encryption.TryDecrypt(setting.NtfyTokenCipher);
    var error = await _sender.SendAsync(url, token, AssistantTemplates.NudgeTestTitle, AssistantTemplates.NudgeTestMessage, ct);
    await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.NudgeTested, actor.UserId, actor.HouseholdId, "nudge_setting", actor.PersonId.ToString(),
      new { sent = error == null }, actor.Info.IpAddress), ct);
    return new NudgeTestResultDto(error == null, error);
  }

  private async Task<NudgeSettingsDto> ToDtoAsync(Guid personId, NudgeSetting? setting, string timeZone, CancellationToken ct)
  {
    var url = setting?.NtfyUrlCipher == null ? null : _encryption.TryDecrypt(setting.NtfyUrlCipher);
    return new NudgeSettingsDto(
      setting?.Enabled ?? false,
      url != null,
      url == null ? null : NudgeUrls.Mask(url),
      setting?.NtfyTokenCipher != null,
      (setting?.EveningTime ?? NudgeDefaults.EveningTime).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
      timeZone,
      await _nudges.LastOwnLogDayAsync(personId, NudgeKinds.Evening, ct));
  }
}

/// <summary>
/// Job-locked hosted service: every five minutes, for each person with reminders on, from their
/// evening time in their own zone, when today's check-in is not done and no nudge is logged for
/// the day, posts the fixed reminder to their ntfy topic and logs it. One a day at most. A failure
/// is logged with the person id and a short reason, never the address, and retried next tick
/// inside the window.
/// </summary>
public sealed class NudgeJob : BackgroundService
{
  public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

  private readonly IServiceScopeFactory _scopes;
  private readonly TimeProvider _clock;
  private readonly ILogger<NudgeJob> _logger;

  public NudgeJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<NudgeJob> logger)
  {
    _scopes = scopes;
    _clock = clock;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    try
    {
      await Task.Delay(InitialDelay, stoppingToken);
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          await TickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          _logger.LogError("Nudge tick failed ({Error}); retrying next tick", ex.GetType().Name);
        }
        await Task.Delay(CheckInterval, stoppingToken);
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // normal shutdown
    }
  }

  /// <summary>One pass over every enabled setting. Returns how many nudges were sent. Public so a test can drive it.</summary>
  public async Task<int> TickAsync(CancellationToken ct)
  {
    using var scope = _scopes.CreateScope();
    var nudges = scope.ServiceProvider.GetRequiredService<INudgeRepository>();
    var persons = scope.ServiceProvider.GetRequiredService<IPersonRepository>();
    var conversations = scope.ServiceProvider.GetRequiredService<IAssistantRepository>();
    var sender = scope.ServiceProvider.GetRequiredService<INtfySender>();
    var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
    var now = _clock.GetUtcNow().UtcDateTime;

    var sent = 0;
    foreach (var setting in await nudges.ListEnabledForJobAsync(ct))
    {
      var person = await persons.GetByIdAsync(setting.PersonId, ct);
      if (person == null) continue;
      var zone = DayBoundaries.Resolve(person.TimeZone);
      if (NudgeSchedule.DueDay(now, zone, setting.EveningTime) is not { } day) continue;
      if (await nudges.HasOwnLogAsync(person.Id, NudgeKinds.Evening, day, ct)) continue;
      var checkin = await conversations.GetOwnCheckinAsync(person.Id, day, ct);
      if (checkin != null && CheckinMachine.Status(CheckinState.FromJson(checkin.State)) == CheckinStatuses.Done) continue;

      var url = setting.NtfyUrlCipher == null ? null : encryption.TryDecrypt(setting.NtfyUrlCipher);
      if (url == null) continue;
      var token = setting.NtfyTokenCipher == null ? null : encryption.TryDecrypt(setting.NtfyTokenCipher);
      try
      {
        var error = await sender.SendAsync(url, token, AssistantTemplates.NudgeTitle, AssistantTemplates.NudgeMessage, ct);
        if (error != null)
        {
          _logger.LogWarning("Evening nudge for person {PersonId} failed: {Reason}", person.Id, error);
          continue;
        }
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        _logger.LogWarning("Evening nudge for person {PersonId} failed: {Error}", person.Id, ex.GetType().Name);
        continue;
      }
      if (await nudges.TryAddOwnLogAsync(new NudgeLog { PersonId = person.Id, Kind = NudgeKinds.Evening, Day = day, SentAt = now }, ct)) sent++;
    }
    return sent;
  }
}
