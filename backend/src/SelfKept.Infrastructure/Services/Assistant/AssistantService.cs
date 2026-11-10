using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// The in-app chat (docs/assistant-design.md). A message is saved, what it states is captured to
/// the day log at once (with Undo), a red-flag symptom is answered by code, and otherwise the
/// local model answers over the state brief. The reply streams sentence by sentence through the
/// guard; a reply that fails at the end is replaced. With the model off or unreachable the reply
/// is a template built from the brief, and the captures still happen.
/// </summary>
public sealed class AssistantService : IAssistantService
{
  public const int ListLimit = 50;
  public const int HistoryTurns = 12;
  public const int MaxMessageLength = 2000;
  public const int MaxTitleLength = 80;
  private const int AutoTitleLength = 60;

  private static readonly ChatOptions ReplyOptions = new(Temperature: 0.4, MaxTokens: 400);

  private readonly IAssistantRepository _repository;
  private readonly IPersonRepository _persons;
  private readonly IStateBriefBuilder _briefs;
  private readonly IJournalCapture _capture;
  private readonly CaptureSaver _saver;
  private readonly IJournalService _journal;
  private readonly IAiMemoryService _memory;
  private readonly ICheckinService _checkins;
  private readonly IReplyGuard _guard;
  private readonly IChatModel _model;
  private readonly IAiCallRepository _calls;
  private readonly ModelRewording _rewording;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;
  private readonly ILogger<AssistantService> _logger;

  public AssistantService(
    IAssistantRepository repository,
    IPersonRepository persons,
    IStateBriefBuilder briefs,
    IJournalCapture capture,
    CaptureSaver saver,
    IJournalService journal,
    IAiMemoryService memory,
    ICheckinService checkins,
    IReplyGuard guard,
    IChatModel model,
    IAiCallRepository calls,
    ModelRewording rewording,
    IEncryptionService encryption,
    IAuditService audit,
    TimeProvider clock,
    ILogger<AssistantService> logger)
  {
    _repository = repository;
    _persons = persons;
    _briefs = briefs;
    _capture = capture;
    _saver = saver;
    _journal = journal;
    _memory = memory;
    _checkins = checkins;
    _guard = guard;
    _model = model;
    _calls = calls;
    _rewording = rewording;
    _encryption = encryption;
    _audit = audit;
    _clock = clock;
    _logger = logger;
  }

  public async Task<AssistantStatusDto> StatusAsync(CancellationToken ct = default)
  {
    if (!_model.Enabled) return new AssistantStatusDto(false, null, false);
    return new AssistantStatusDto(true, _model.ModelName, await _model.PingAsync(ct));
  }

  public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(Guid personId, CancellationToken ct = default) =>
    (await _repository.ListOwnConversationsAsync(personId, ListLimit, ct)).Select(Summary).ToList();

  public async Task<ConversationSummaryDto> CreateAsync(Actor actor, CreateConversationRequest request, CancellationToken ct = default)
  {
    var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
    if (title is { Length: > MaxTitleLength }) throw new DomainValidationException($"A title is at most {MaxTitleLength} characters.");
    var now = _clock.GetUtcNow().UtcDateTime;
    var conversation = new AssistantConversation
    {
      Id = Guid.CreateVersion7(),
      PersonId = actor.PersonId,
      Kind = ConversationKinds.Chat,
      TitleCipher = title == null ? null : _encryption.Encrypt(title),
      CreatedAt = now,
      UpdatedAt = now,
    };
    await _repository.AddOwnConversationAsync(conversation, ct);
    await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.ConversationCreated, actor.UserId, actor.HouseholdId,
      "assistant_conversation", conversation.Id.ToString(), IpAddress: actor.Info.IpAddress), ct);
    return Summary(conversation);
  }

  public async Task<ConversationDto?> GetAsync(Guid personId, Guid conversationId, CancellationToken ct = default)
  {
    var conversation = await _repository.GetOwnConversationAsync(personId, conversationId, ct);
    if (conversation == null) return null;
    var messages = await _repository.ListOwnMessagesAsync(personId, conversationId, 500, ct);
    var summary = Summary(conversation);
    return new ConversationDto(summary.Id, summary.Kind, summary.Title, summary.Day, summary.CreatedAt, summary.UpdatedAt,
      messages.Select(ToDto).ToList());
  }

  public async Task<bool> DeleteAsync(Actor actor, Guid conversationId, CancellationToken ct = default)
  {
    var conversation = await _repository.GetOwnConversationAsync(actor.PersonId, conversationId, ct);
    if (conversation == null) return false;
    await _repository.RemoveOwnConversationAsync(conversation, ct);
    await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.ConversationDeleted, actor.UserId, actor.HouseholdId,
      "assistant_conversation", conversationId.ToString(), new { kind = conversation.Kind }, actor.Info.IpAddress), ct);
    return true;
  }

  public async Task<AssistantTurn?> BeginTurnAsync(Actor actor, Guid conversationId, string text, CancellationToken ct = default)
  {
    var message = (text ?? string.Empty).Trim();
    if (message.Length == 0) throw new DomainValidationException("Say something first.");
    if (message.Length > MaxMessageLength) throw new DomainValidationException($"A message is at most {MaxMessageLength} characters.");

    var conversation = await _repository.GetOwnConversationAsync(actor.PersonId, conversationId, ct);
    if (conversation == null) return null;
    if (conversation.Kind == ConversationKinds.Checkin) throw new DomainValidationException("The evening check-in is answered through /api/assistant/checkin.");
    var person = await _persons.GetByIdAsync(actor.PersonId, ct)
      ?? throw new DomainValidationException("No person profile for this account.");
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var now = _clock.GetUtcNow().UtcDateTime;

    // History first, so the message being answered is not in it twice.
    var history = await _repository.ListOwnMessagesAsync(actor.PersonId, conversationId, HistoryTurns, ct);

    var userMessage = new AssistantMessage
    {
      Id = Guid.CreateVersion7(),
      ConversationId = conversationId,
      PersonId = actor.PersonId,
      Role = MessageRoles.User,
      ContentCipher = _encryption.Encrypt(message),
      Origin = MessageOrigins.Code,
      CreatedAt = now,
    };
    await _repository.AddOwnMessageAsync(userMessage, ct);

    var found = _capture.Extract(message, now, zone);
    var (entries, remembered) = await _saver.SaveAsync(actor, found, JournalSources.Assistant, userMessage.Id, ct);
    if (entries.Count > 0 || remembered.Count > 0)
    {
      userMessage.Meta = new AssistantMeta(CaptureSaver.Refs(entries, remembered)).ToJson();
      await _repository.UpdateOwnMessageAsync(userMessage, ct);
    }

    conversation.UpdatedAt = now;
    if (conversation.TitleCipher == null)
    {
      conversation.TitleCipher = _encryption.Encrypt(message.Length > AutoTitleLength ? message[..AutoTitleLength].TrimEnd() : message);
    }
    await _repository.UpdateOwnConversationAsync(conversation, ct);

    var capture = new CaptureEventDto(conversationId, userMessage.Id, entries, remembered);
    var saved = entries.Count > 0 || remembered.Count > 0;

    var redFlag = _guard.RedFlagReply(message);
    if (redFlag != null)
    {
      await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.RedFlagAnswered, actor.UserId, actor.HouseholdId,
        "assistant_message", userMessage.Id.ToString(), IpAddress: actor.Info.IpAddress), ct);
      return new AssistantTurn(actor, conversationId, userMessage.Id, capture, redFlag, [], GuardContext.Empty, redFlag, redFlag);
    }

    var today = DayBoundaries.LocalDay(now, zone);
    var brief = await _briefs.BuildAsync(person, today, ct);
    var guard = GuardNumbers.Context(brief.Text, message);
    var refused = AssistantTemplates.Refused(brief.Facts, saved);
    var unavailable = AssistantTemplates.Unavailable(brief.Facts, saved);

    var system = new StringBuilder(AssistantTemplates.SystemPrompt).Append("\n\nBrief:\n").Append(brief.Text);
    if (saved)
    {
      system.Append("\n\nSaved to the day log from the person's latest message: ")
        .Append(string.Join("; ", entries.Select(e => e.Kind + (e.Tags.Count > 0 ? " (" + string.Join(", ", e.Tags.Select(JournalTags.DisplayName)) + ")" : string.Empty))))
        .Append(remembered.Count > 0 ? (entries.Count > 0 ? "; " : string.Empty) + "a fact to remember" : string.Empty)
        .Append('.');
    }
    var turns = new List<ChatTurn> { new(ChatTurn.System, system.ToString()) };
    foreach (var past in history)
    {
      var content = _encryption.TryDecrypt(past.ContentCipher);
      if (string.IsNullOrEmpty(content)) continue;
      turns.Add(new ChatTurn(past.Role == MessageRoles.Assistant ? ChatTurn.Assistant : ChatTurn.User, content));
    }
    turns.Add(new ChatTurn(ChatTurn.User, message));

    return new AssistantTurn(actor, conversationId, userMessage.Id, capture, null, turns, guard, refused, unavailable);
  }

  public async IAsyncEnumerable<AssistantStreamEvent> ReplyAsync(AssistantTurn turn, [EnumeratorCancellation] CancellationToken ct = default)
  {
    if (turn.FixedReply != null)
    {
      yield return Delta(turn.FixedReply);
      yield return await FinishAsync(turn, turn.FixedReply, MessageOrigins.Guard, AssistantMeta.RedFlagReason, ct);
      yield break;
    }
    if (!_model.Enabled)
    {
      yield return Delta(turn.Unavailable);
      yield return await FinishAsync(turn, turn.Unavailable, MessageOrigins.Template, "model off", ct);
      yield break;
    }

    var call = _rewording.NewCall(turn.Actor.PersonId, AssistantAiPurposes.Chat, string.Join("\n", turn.ModelTurns.Select(t => t.Role + ": " + t.Content)));
    var watch = Stopwatch.StartNew();
    var buffer = new SentenceBuffer();
    var shown = new StringBuilder();
    string? refusal = null;
    Exception? failure = null;

    var stream = _model.StreamAsync(turn.ModelTurns, ReplyOptions, ct).GetAsyncEnumerator(ct);
    try
    {
      while (refusal == null && failure == null)
      {
        string piece;
        try
        {
          if (!await stream.MoveNextAsync()) break;
          piece = stream.Current;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          failure = ex;
          break;
        }

        foreach (var sentence in buffer.Push(piece))
        {
          var verdict = _guard.CheckSentence(sentence, turn.Guard);
          if (!verdict.Ok)
          {
            refusal = verdict.Reason;
            break;
          }
          if (verdict.Text.Length == 0) continue;
          var text = (shown.Length > 0 ? " " : string.Empty) + verdict.Text;
          shown.Append(text);
          yield return Delta(text);
        }
      }
    }
    finally
    {
      await stream.DisposeAsync();
    }

    if (refusal == null && failure == null)
    {
      var rest = buffer.Flush();
      if (rest.Length > 0)
      {
        var verdict = _guard.CheckSentence(rest, turn.Guard);
        if (verdict.Ok && verdict.Text.Length > 0)
        {
          var text = (shown.Length > 0 ? " " : string.Empty) + verdict.Text;
          shown.Append(text);
          yield return Delta(text);
        }
        else if (!verdict.Ok)
        {
          refusal = verdict.Reason;
        }
      }
    }

    var final = shown.ToString();
    if (refusal == null && failure == null)
    {
      var whole = _guard.CheckReply(final, turn.Guard);
      if (!whole.Ok) refusal = whole.Reason;
    }

    call.LatencyMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);
    call.Ok = refusal == null && failure == null;
    call.Error = failure != null ? ModelRewording.Describe(failure) : refusal != null ? "rejected: " + refusal : null;
    await _calls.RecordAsync(call, CancellationToken.None);
    if (failure != null) _logger.LogWarning("Assistant reply failed: {Error}", call.Error);

    if (refusal == null && failure == null)
    {
      yield return await FinishAsync(turn, final, MessageOrigins.Model, null, ct);
      yield break;
    }

    // The model could not be reached part way, or the guard refused: the template takes over.
    var (replacement, origin, reason) = failure != null
      ? (turn.Unavailable, MessageOrigins.Template, "unreachable")
      : (turn.Fallback, MessageOrigins.Guard, "guard");
    yield return shown.Length > 0
      ? new AssistantStreamEvent(AssistantStreamEvent.Replace, new ReplaceEventDto(replacement, reason))
      : Delta(replacement);
    yield return await FinishAsync(turn, replacement, origin, reason == "guard" ? "guard: " + refusal : reason, ct);
  }

  public async Task<UndoResultDto?> UndoCapturesAsync(Actor actor, Guid messageId, CancellationToken ct = default)
  {
    var message = await _repository.GetOwnMessageAsync(actor.PersonId, messageId, ct);
    if (message == null || message.Role != MessageRoles.User) return null;
    var meta = AssistantMeta.From(message.Meta);
    var entries = await _journal.DeleteByMessageAsync(actor, messageId, ct);
    var memories = 0;
    var checkins = 0;
    if (meta.Undone != true)
    {
      foreach (var capture in meta.Captures ?? [])
      {
        if (capture.Type == AssistantMeta.Memory && await _memory.DeleteAsync(actor.PersonId, capture.Id, ct)) memories++;
        if (capture.Type == AssistantMeta.Mood && await _checkins.DeleteAsync(actor.PersonId, capture.Id, ct)) checkins++;
      }
    }
    message.Meta = (meta with { Undone = true }).ToJson();
    await _repository.UpdateOwnMessageAsync(message, ct);
    await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.CapturesUndone, actor.UserId, actor.HouseholdId,
      "assistant_message", messageId.ToString(), new { entries, memories, checkins }, actor.Info.IpAddress), ct);
    return new UndoResultDto(messageId, entries, memories, checkins);
  }

  private async Task<AssistantStreamEvent> FinishAsync(AssistantTurn turn, string text, string origin, string? reason, CancellationToken ct)
  {
    var now = _clock.GetUtcNow().UtcDateTime;
    var reply = new AssistantMessage
    {
      Id = Guid.CreateVersion7(),
      ConversationId = turn.ConversationId,
      PersonId = turn.Actor.PersonId,
      Role = MessageRoles.Assistant,
      ContentCipher = _encryption.Encrypt(text),
      Origin = origin,
      Meta = new AssistantMeta(Reason: reason).ToJson(),
      CreatedAt = now,
    };
    await _repository.AddOwnMessageAsync(reply, ct);
    var conversation = await _repository.GetOwnConversationAsync(turn.Actor.PersonId, turn.ConversationId, ct);
    if (conversation != null)
    {
      conversation.UpdatedAt = now;
      await _repository.UpdateOwnConversationAsync(conversation, ct);
    }
    return new AssistantStreamEvent(AssistantStreamEvent.Done, new DoneEventDto(turn.ConversationId, reply.Id, turn.UserMessageId, origin, text, reason == AssistantMeta.RedFlagReason));
  }

  private static AssistantStreamEvent Delta(string text) => new(AssistantStreamEvent.Delta, new DeltaEventDto(text));

  private ConversationSummaryDto Summary(AssistantConversation c) => new(
    c.Id,
    c.Kind,
    (c.TitleCipher == null ? null : _encryption.TryDecrypt(c.TitleCipher))
      ?? (c.Kind == ConversationKinds.Checkin && c.Day is { } day ? "Evening check-in, " + day.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture) : "New conversation"),
    c.Day,
    c.CreatedAt,
    c.UpdatedAt);

  private AssistantMessageDto ToDto(AssistantMessage m)
  {
    var meta = AssistantMeta.From(m.Meta);
    return new AssistantMessageDto(
      m.Id,
      m.Role,
      _encryption.TryDecrypt(m.ContentCipher) ?? string.Empty,
      m.Origin,
      m.CreatedAt,
      meta.Captures ?? [],
      meta.Undone == true,
      meta.Reason == AssistantMeta.RedFlagReason);
  }
}
