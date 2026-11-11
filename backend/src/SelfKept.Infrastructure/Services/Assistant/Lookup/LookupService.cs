using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Assistant.Lookup;

/// <summary>
/// The cloud lookup (docs/lookup-design.md): the person's own switch, the draft built by code,
/// the checked send, the one provider call, and the stored answer with its sources. The state
/// brief never goes to the cloud; facts from the record leave only as the "About me" text the
/// person chose and read. Once a send has gone to the provider it runs to the end on its own,
/// so a closed app still finds the answer in the conversation.
/// </summary>
public sealed class LookupService : ILookupService
{
  public const int ListLimit = 100;
  public const int MaxTextLength = 4000;
  public static readonly TimeSpan WaitingInterval = TimeSpan.FromSeconds(10);

  /// <summary>The local model may reword an anonymized draft into a general question; the scrub then runs again on its words.</summary>
  public const string RewordPrompt =
    "Rewrite the person's message below as one short, general research question that a medical literature search could answer. " +
    "Keep its meaning. Do not add names, numbers, dates or facts that are not in it. Write only the question.";

  /// <summary>The local model words what a cloud answer means for this person, from the state brief, on the box.</summary>
  public const string AppliedPrompt =
    "A cloud model answered a general research question for the person below. In two or three short sentences, say what the " +
    "answer may mean for them, using only the brief and the answer. Do not diagnose, do not promise results, and do not give " +
    "doses or numbers that are not in the brief or the answer.";

  public const string SwitchOffDetail = "Turn on cloud lookup for yourself first.";
  public const string AcknowledgeDetail = "Confirm that you have read what will be sent.";
  public const string AcknowledgeSwitchDetail = "Confirm that you understand what a cloud lookup sends before turning it on.";
  public const string RedFlagDetail = "This names symptoms that need care now, so it cannot be sent for a lookup.";
  public const string HardIdentifierDetail = "The text still holds a name, contact detail or web address. Take it out and send again.";

  private readonly ILookupRepository _lookups;
  private readonly IAssistantRepository _assistant;
  private readonly IPersonRepository _persons;
  private readonly IHouseholdRepository _households;
  private readonly IStateBriefBuilder _briefs;
  private readonly IReplyGuard _guard;
  private readonly IChatModel _model;
  private readonly ModelRewording _rewording;
  private readonly IAiCallRepository _calls;
  private readonly ICloudLookupClient _client;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly IConfiguration _configuration;
  private readonly TimeProvider _clock;
  private readonly ILogger<LookupService> _logger;

  public LookupService(
    ILookupRepository lookups,
    IAssistantRepository assistant,
    IPersonRepository persons,
    IHouseholdRepository households,
    IStateBriefBuilder briefs,
    IReplyGuard guard,
    IChatModel model,
    ModelRewording rewording,
    IAiCallRepository calls,
    ICloudLookupClient client,
    IEncryptionService encryption,
    IAuditService audit,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<LookupService> logger)
  {
    _lookups = lookups;
    _assistant = assistant;
    _persons = persons;
    _households = households;
    _briefs = briefs;
    _guard = guard;
    _model = model;
    _rewording = rewording;
    _calls = calls;
    _client = client;
    _encryption = encryption;
    _audit = audit;
    _configuration = configuration;
    _clock = clock;
    _logger = logger;
  }

  public bool Available => _client.IsUsable;

  /// <summary>How often "waiting" is sent; shorter in tests.</summary>
  internal TimeSpan WaitingEvery { get; set; } = WaitingInterval;

  public async Task<LookupStatusDto> GetAsync(Guid personId, CancellationToken ct = default) =>
    Status(await IsOnAsync(personId, ct));

  public async Task<LookupStatusDto> SaveAsync(Actor actor, SaveLookupSettingRequest request, CancellationToken ct = default)
  {
    if (request.Enabled && !request.Acknowledged) throw new UnprocessableException(AcknowledgeSwitchDetail);
    var existing = await _lookups.GetOwnSettingAsync(actor.PersonId, ct);
    var now = _clock.GetUtcNow().UtcDateTime;
    var wasOn = existing?.Enabled == true;
    await _lookups.SaveOwnSettingAsync(new LookupSetting
    {
      PersonId = actor.PersonId,
      Enabled = request.Enabled,
      EnabledAt = request.Enabled && !wasOn ? now : existing?.EnabledAt,
      UpdatedAt = now,
    }, ct);
    if (wasOn != request.Enabled)
    {
      await _audit.RecordAsync(new AuditEvent(request.Enabled ? AssistantAuditActions.LookupEnabled : AssistantAuditActions.LookupDisabled,
        actor.UserId, actor.HouseholdId, "lookup_setting", actor.PersonId.ToString(), IpAddress: actor.Info.IpAddress), ct);
    }
    return Status(request.Enabled);
  }

  public async Task<LookupDraftDto?> DraftAsync(Actor actor, LookupDraftRequest request, CancellationToken ct = default)
  {
    var mode = CheckMode(request.Mode);
    if (!await IsOnAsync(actor.PersonId, ct)) throw new ConflictException(SwitchOffDetail);
    var question = await QuestionAsync(actor.PersonId, request.MessageId, ct);
    if (question == null) return null;
    var text = _encryption.TryDecrypt(question.ContentCipher) ?? string.Empty;
    if (_guard.RedFlagReply(text) != null) throw new UnprocessableException(RedFlagDetail);

    var context = await ContextAsync(actor, ct);
    if (mode == LookupModes.Detailed)
    {
      var person = await _persons.GetByIdAsync(actor.PersonId, ct);
      var about = person == null ? null : await AboutMeAsync(person, ct);
      var detailed = LookupScrubber.Detailed(text, about, context);
      return Draft(detailed.Text, mode, detailed.Changes, false);
    }

    var draft = LookupScrubber.Anonymize(text, context);
    var reworded = await RewordAsync(actor.PersonId, draft.Text, context, ct);
    return Draft(reworded ?? draft.Text, mode, draft.Changes, reworded != null);
  }

  public async Task<LookupSend?> PrepareSendAsync(Actor actor, SendLookupRequest request, CancellationToken ct = default)
  {
    var mode = CheckMode(request.Mode);
    if (!await IsOnAsync(actor.PersonId, ct)) throw new ConflictException(SwitchOffDetail);
    if (!request.Acknowledged) throw new UnprocessableException(AcknowledgeDetail);
    var text = (request.Text ?? string.Empty).Trim();
    if (text.Length == 0) throw new DomainValidationException("Write the question to send first.");
    if (text.Length > MaxTextLength) throw new DomainValidationException($"A lookup is at most {MaxTextLength} characters.");

    var question = await QuestionAsync(actor.PersonId, request.MessageId, ct);
    if (question == null) return null;
    var original = _encryption.TryDecrypt(question.ContentCipher) ?? string.Empty;
    if (_guard.RedFlagReply(original) != null || _guard.RedFlagReply(text) != null) throw new UnprocessableException(RedFlagDetail);

    var findings = LookupScrubber.FindHardIdentifiers(text, await ContextAsync(actor, ct));
    if (findings.Count > 0) throw new UnprocessableException(HardIdentifierDetail, findings);
    return new LookupSend(actor, question.ConversationId, question.Id, mode, text);
  }

  public async IAsyncEnumerable<AssistantStreamEvent> SendAsync(LookupSend send, [EnumeratorCancellation] CancellationToken ct = default)
  {
    // From here on nothing takes the request's token: the person pays for the call once it has
    // gone, so the row, the answer and the audit are written whatever happens to the request.
    var lookup = new AssistantLookup
    {
      Id = Guid.CreateVersion7(),
      PersonId = send.Actor.PersonId,
      ConversationId = send.ConversationId,
      QuestionMessageId = send.QuestionMessageId,
      Mode = send.Mode,
      Status = LookupStatuses.Sent,
      QuestionCipher = _encryption.Encrypt(send.Text),
      Provider = _client.Provider,
      Model = _client.Model,
      CreatedAt = _clock.GetUtcNow().UtcDateTime,
    };
    await _lookups.AddOwnAsync(lookup, CancellationToken.None);
    await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.LookupSent, send.Actor.UserId, send.Actor.HouseholdId,
      "assistant_lookup", lookup.Id.ToString(), new { mode = send.Mode, provider = lookup.Provider, model = lookup.Model, characters = send.Text.Length },
      send.Actor.Info.IpAddress), CancellationToken.None);

    var work = RunAsync(send, lookup);
    using var ticks = new CancellationTokenSource();
    try
    {
      yield return new AssistantStreamEvent(LookupStreamEvents.Sent, new LookupSentEventDto(lookup.Id));
      while (!work.IsCompleted)
      {
        var tick = Task.Delay(WaitingEvery, _clock, ticks.Token);
        if (await Task.WhenAny(work, tick) == work) break;
        yield return new AssistantStreamEvent(LookupStreamEvents.Waiting, new LookupWaitingEventDto());
      }
      yield return await work;
    }
    finally
    {
      await ticks.CancelAsync();
      // A reader that stopped early (the app was closed) still waits here, so the request, and
      // the scope the work writes through, stay alive until the answer is stored.
      if (!work.IsCompleted) await work;
    }
  }

  public async Task<IReadOnlyList<LookupSummaryDto>> ListAsync(Guid personId, CancellationToken ct = default) =>
    (await _lookups.ListOwnAsync(personId, ListLimit, ct))
      .Select(l => new LookupSummaryDto(l.Id, l.ConversationId, l.Mode, l.Status, l.Provider, l.Model,
        _encryption.TryDecrypt(l.QuestionCipher) ?? string.Empty, l.CreatedAt))
      .ToList();

  public async Task<bool> DeleteAsync(Actor actor, Guid lookupId, CancellationToken ct = default)
  {
    if (!await _lookups.RemoveOwnAsync(actor.PersonId, lookupId, ct)) return false;
    await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.LookupDeleted, actor.UserId, actor.HouseholdId,
      "assistant_lookup", lookupId.ToString(), IpAddress: actor.Info.IpAddress), ct);
    return true;
  }

  public async Task<LookupOfferDto?> OfferAsync(Guid personId, string userMessage, string replyOrigin, bool redFlag, CancellationToken ct = default)
  {
    if (!Available || redFlag || !QuestionHeuristic.IsQuestion(userMessage)) return null;
    if (!await IsOnAsync(personId, ct)) return null;
    var reason = replyOrigin switch
    {
      MessageOrigins.Guard => "refused",
      MessageOrigins.Template => "model_off",
      _ => "question",
    };
    return new LookupOfferDto(true, reason);
  }

  /// <summary>The provider call and everything stored after it. Never cancelled, never throws for a provider failure.</summary>
  private async Task<AssistantStreamEvent> RunAsync(LookupSend send, AssistantLookup lookup)
  {
    var call = _rewording.NewCall(send.Actor.PersonId, AssistantAiPurposes.Lookup, send.Text);
    var watch = Stopwatch.StartNew();
    CloudLookupResult result;
    try
    {
      result = await _client.AskAsync(send.Text, CancellationToken.None);
    }
    catch (Exception ex)
    {
      result = CloudLookupResult.Failed(CloudLookupOutcome.Unreachable, ex.GetType().Name);
    }
    call.LatencyMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);
    call.PromptTokens = result.InputTokens;
    call.CompletionTokens = result.OutputTokens;
    call.Ok = result.Outcome == CloudLookupOutcome.Answered;
    call.Error = call.Ok ? null : Reason(result.Outcome) + (result.Error is { Length: > 0 } e ? ": " + e : string.Empty);
    if (call.Error is { Length: > 200 }) call.Error = call.Error[..200];
    await _calls.RecordAsync(call, CancellationToken.None);

    lookup.InputTokens = result.InputTokens;
    lookup.OutputTokens = result.OutputTokens;
    if (!call.Ok)
    {
      _logger.LogWarning("Cloud lookup {LookupId} came back without an answer: {Reason}", lookup.Id, call.Error);
      lookup.Status = LookupStatuses.Failed;
      await _lookups.UpdateOwnAsync(lookup, CancellationToken.None);
      return new AssistantStreamEvent(LookupStreamEvents.Failed, new LookupFailedEventDto(lookup.Id, Reason(result.Outcome), FailureText(result.Outcome)));
    }

    var now = _clock.GetUtcNow().UtcDateTime;
    var answer = new AssistantMessage
    {
      Id = Guid.CreateVersion7(),
      ConversationId = send.ConversationId,
      PersonId = send.Actor.PersonId,
      Role = MessageRoles.Assistant,
      ContentCipher = _encryption.Encrypt(result.Text),
      Origin = MessageOrigins.Cloud,
      Meta = new AssistantMeta(LookupId: lookup.Id).ToJson(),
      CreatedAt = now,
    };
    await _assistant.AddOwnMessageAsync(answer, CancellationToken.None);
    lookup.Status = LookupStatuses.Answered;
    lookup.AnswerMessageId = answer.Id;
    lookup.SourcesCipher = LookupSources.Encode(result.Sources, _encryption);
    lookup.Model = string.IsNullOrWhiteSpace(result.Model) ? lookup.Model : Truncate(result.Model, 64);
    lookup.AnsweredAt = now;
    await _lookups.UpdateOwnAsync(lookup, CancellationToken.None);

    var applied = await AppliedAsync(send, result.Text);
    var conversation = await _assistant.GetOwnConversationAsync(send.Actor.PersonId, send.ConversationId, CancellationToken.None);
    if (conversation != null)
    {
      conversation.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
      await _assistant.UpdateOwnConversationAsync(conversation, CancellationToken.None);
    }
    return new AssistantStreamEvent(LookupStreamEvents.Done, new LookupDoneEventDto(lookup.Id,
      AssistantService.ToDto(answer, _encryption, lookup),
      applied == null ? null : AssistantService.ToDto(applied, _encryption)));
  }

  /// <summary>
  /// The local model's two or three sentences on what the answer means for this person, from the
  /// brief, through the reply guard. The numbers it may use are the brief's and the answer's.
  /// Null when there is no local model or the guard refused what it wrote.
  /// </summary>
  private async Task<AssistantMessage?> AppliedAsync(LookupSend send, string answer)
  {
    if (!_rewording.Enabled) return null;
    try
    {
      var person = await _persons.GetByIdAsync(send.Actor.PersonId, CancellationToken.None);
      if (person == null) return null;
      var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
      var brief = await _briefs.BuildAsync(person, today, CancellationToken.None);
      var context = GuardNumbers.Context(brief.Text, answer);
      var system = AppliedPrompt + "\n\nBrief:\n" + brief.Text;
      var text = await _rewording.RewordAsync(send.Actor.PersonId, AssistantAiPurposes.LookupApplied, system, "Cloud answer:\n" + answer, context,
        asLine: false, maxTokens: 300, CancellationToken.None);
      if (string.IsNullOrWhiteSpace(text)) return null;
      var message = new AssistantMessage
      {
        Id = Guid.CreateVersion7(),
        ConversationId = send.ConversationId,
        PersonId = send.Actor.PersonId,
        Role = MessageRoles.Assistant,
        ContentCipher = _encryption.Encrypt(text),
        Origin = MessageOrigins.Model,
        CreatedAt = _clock.GetUtcNow().UtcDateTime,
      };
      await _assistant.AddOwnMessageAsync(message, CancellationToken.None);
      return message;
    }
    catch (Exception ex)
    {
      // The answer is stored already; the local step is polish and must not lose the done event.
      _logger.LogWarning("The applied step of a cloud lookup failed: {Error}", ModelRewording.Describe(ex));
      return null;
    }
  }

  /// <summary>
  /// The local model's version of an anonymized draft, scrubbed again, or null to keep the draft:
  /// no model, a failed call, or words that hold a hard identifier or a number the draft did not.
  /// </summary>
  private async Task<string?> RewordAsync(Guid personId, string draft, LookupScrubContext context, CancellationToken ct)
  {
    if (!_model.Enabled || draft.Length == 0) return null;
    var call = _rewording.NewCall(personId, AssistantAiPurposes.LookupDraft, RewordPrompt + "\n" + draft);
    var watch = Stopwatch.StartNew();
    string? kept = null;
    try
    {
      var completion = await _model.CompleteAsync(RewordPrompt, draft, new ChatOptions(0.2, 200), ct);
      call.PromptTokens = completion.PromptTokens;
      call.CompletionTokens = completion.CompletionTokens;
      var verdict = CheckRewording(draft, completion.Content, context);
      call.Ok = verdict.Kept != null;
      call.Error = verdict.Reason;
      kept = verdict.Kept;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      call.Ok = false;
      call.Error = ModelRewording.Describe(ex);
    }
    finally
    {
      call.LatencyMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);
      await _calls.RecordAsync(call, CancellationToken.None);
    }
    return kept;
  }

  /// <summary>
  /// The drop rule for a reworded draft (pure): the code scrub runs again on what the model wrote,
  /// and the model's version is dropped when it held a hard identifier or a number the scrubbed
  /// draft does not have.
  /// </summary>
  public static (string? Kept, string? Reason) CheckRewording(string draft, string? reworded, LookupScrubContext context)
  {
    var raw = (reworded ?? string.Empty).Trim().Trim('"', '“', '”').Trim();
    if (raw.Length == 0) return (null, "rejected: empty");
    if (raw.Length > Math.Max(400, draft.Length * 2)) return (null, "rejected: too long");
    if (LookupScrubber.FindHardIdentifiers(raw, context).Count > 0) return (null, "rejected: identifier");
    var scrubbed = LookupScrubber.Anonymize(raw, context).Text;
    var allowed = GuardNumbers.Of(draft);
    if (GuardNumbers.Of(scrubbed).Any(n => !allowed.Contains(n))) return (null, "rejected: number");
    return (scrubbed, null);
  }

  private LookupDraftDto Draft(string text, string mode, IReadOnlyList<LookupChangeDto> changes, bool reworded) =>
    new(text, mode, changes, reworded, _client.Provider, _client.Model);

  private LookupStatusDto Status(bool enabled) => new(Available, enabled, _client.Provider, _client.Model, _model.Enabled);

  private async Task<bool> IsOnAsync(Guid personId, CancellationToken ct) =>
    (await _lookups.GetOwnSettingAsync(personId, ct))?.Enabled == true;

  private async Task<AssistantMessage?> QuestionAsync(Guid personId, Guid messageId, CancellationToken ct)
  {
    var message = await _assistant.GetOwnMessageAsync(personId, messageId, ct);
    return message is { Role: MessageRoles.User } ? message : null;
  }

  /// <summary>The names of everyone in the caller's household (their own first) and the server's host name.</summary>
  private async Task<LookupScrubContext> ContextAsync(Actor actor, CancellationToken ct)
  {
    var names = new List<string>();
    var person = await _persons.GetByIdAsync(actor.PersonId, ct);
    if (person != null) names.Add(person.DisplayName);
    if (actor.HouseholdId is { } household)
    {
      names.AddRange((await _households.GetMembersAsync(household, ct)).Select(m => m.Person?.DisplayName).OfType<string>());
    }
    var host = Uri.TryCreate(_configuration["App:BaseUrl"], UriKind.Absolute, out var origin) ? origin.Host : null;
    return new LookupScrubContext(names, host);
  }

  /// <summary>Age as a decade, sex when set, and the brief's goal, reading and medicine lines. Never the birth date.</summary>
  private async Task<LookupAboutMe> AboutMeAsync(Person person, CancellationToken ct)
  {
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
    var brief = await _briefs.BuildAsync(person, today, ct);
    var facts = new List<string>();
    facts.AddRange(StateBriefComposer.SectionLines(brief.Text, StateBriefBuilder.GoalsTitle).Where(l => l != StateBriefBuilder.NoGoalLine));
    facts.AddRange(StateBriefComposer.SectionLines(brief.Text, StateBriefBuilder.ReadingsTitle));
    facts.AddRange(StateBriefComposer.SectionLines(brief.Text, StateBriefBuilder.MedicinesTitle));
    return new LookupAboutMe(LookupScrubber.AgeOn(person.BirthDate, today), person.SexAtBirth, facts);
  }

  private static string CheckMode(string? mode) =>
    LookupModes.IsKnown(mode) ? mode! : throw new DomainValidationException("The mode is anonymized or detailed.");

  public static string Reason(CloudLookupOutcome outcome) => outcome switch
  {
    CloudLookupOutcome.Refused => LookupFailureReasons.Refused,
    CloudLookupOutcome.CutOff => LookupFailureReasons.CutOff,
    CloudLookupOutcome.SearchFailed => LookupFailureReasons.SearchFailed,
    CloudLookupOutcome.RateLimited => LookupFailureReasons.RateLimited,
    CloudLookupOutcome.KeyRejected => LookupFailureReasons.KeyRejected,
    _ => LookupFailureReasons.Unreachable,
  };

  /// <summary>The plain message under a failed lookup. Nothing was stored as an answer.</summary>
  public static string FailureText(CloudLookupOutcome outcome) => outcome switch
  {
    CloudLookupOutcome.Refused => "The cloud model declined to answer this question. Nothing was saved as an answer.",
    CloudLookupOutcome.CutOff => "The answer was cut off before it was complete, so it was not saved. You can send it again.",
    CloudLookupOutcome.SearchFailed => "The web search did not work this time, so there is no answer with sources. You can send it again.",
    CloudLookupOutcome.RateLimited => "The provider is busy or this server's account has reached its limit. Try again later.",
    CloudLookupOutcome.KeyRejected => "The provider did not accept this server's key. The person who runs the server can check it.",
    _ => "The cloud model could not be reached. Nothing was saved as an answer.",
  };

  private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
