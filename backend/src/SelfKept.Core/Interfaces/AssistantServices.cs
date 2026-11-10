using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;

namespace SelfKept.Core.Interfaces;

// The in-app assistant (docs/assistant-design.md). Conversations, messages, the morning brief and
// nudge settings are own-only: never shared, so every repository method takes the caller's own
// person id and is named Own. There is no viewer and target pair anywhere in this package.

public interface IAssistantRepository
{
  Task<AssistantConversation> AddOwnConversationAsync(AssistantConversation conversation, CancellationToken ct = default);

  /// <summary>Null when the conversation is not the person's own or does not exist.</summary>
  Task<AssistantConversation?> GetOwnConversationAsync(Guid personId, Guid conversationId, CancellationToken ct = default);

  /// <summary>Most recently updated first.</summary>
  Task<IReadOnlyList<AssistantConversation>> ListOwnConversationsAsync(Guid personId, int limit, CancellationToken ct = default);

  /// <summary>The person's evening check-in for a local day, or null.</summary>
  Task<AssistantConversation?> GetOwnCheckinAsync(Guid personId, DateOnly day, CancellationToken ct = default);

  Task UpdateOwnConversationAsync(AssistantConversation conversation, CancellationToken ct = default);

  /// <summary>Removes the conversation and its messages.</summary>
  Task RemoveOwnConversationAsync(AssistantConversation conversation, CancellationToken ct = default);

  Task<AssistantMessage> AddOwnMessageAsync(AssistantMessage message, CancellationToken ct = default);

  /// <summary>Null when the message is not the person's own.</summary>
  Task<AssistantMessage?> GetOwnMessageAsync(Guid personId, Guid messageId, CancellationToken ct = default);

  /// <summary>The last <paramref name="limit"/> messages of the conversation, oldest first. Empty when it is not the person's.</summary>
  Task<IReadOnlyList<AssistantMessage>> ListOwnMessagesAsync(Guid personId, Guid conversationId, int limit, CancellationToken ct = default);

  Task UpdateOwnMessageAsync(AssistantMessage message, CancellationToken ct = default);

  Task<AssistantBrief?> GetOwnBriefAsync(Guid personId, DateOnly day, CancellationToken ct = default);

  Task SaveOwnBriefAsync(AssistantBrief brief, CancellationToken ct = default);
}

public interface INudgeRepository
{
  Task<NudgeSetting?> GetOwnAsync(Guid personId, CancellationToken ct = default);

  /// <summary>Creates or replaces the person's own row.</summary>
  Task SaveOwnAsync(NudgeSetting setting, CancellationToken ct = default);

  /// <summary>Server-internal, for the nudge job only: every enabled row.</summary>
  Task<IReadOnlyList<NudgeSetting>> ListEnabledForJobAsync(CancellationToken ct = default);

  Task<bool> HasOwnLogAsync(Guid personId, string kind, DateOnly day, CancellationToken ct = default);

  /// <summary>False when a row for (person, kind, day) is already there.</summary>
  Task<bool> TryAddOwnLogAsync(NudgeLog log, CancellationToken ct = default);

  Task<DateOnly?> LastOwnLogDayAsync(Guid personId, string kind, CancellationToken ct = default);
}

/// <summary>One playbook entry that applies to the person today, with the reason in plain words.</summary>
public sealed record PlaybookMatch(string Key, string Topic, string Title, string Text, string EvidenceGrade, string Source, string SourceUrl, string Why, IReadOnlyList<string> Cautions);

/// <summary>The few facts the templates speak from when the model is off or its answer is refused.</summary>
public sealed record BriefFacts(
  string? SleepText,
  IReadOnlyList<string> ReadingTexts,
  string? TargetsText,
  string? InsightText,
  IReadOnlyList<string> Goals);

/// <summary>
/// The text the model reasons over, built by code from the person's own record, capped at about
/// 6000 characters, and every number in it (canonical form) for the guard.
/// </summary>
public sealed record StateBrief(string Text, IReadOnlySet<string> Numbers, IReadOnlyList<PlaybookMatch> Playbook, BriefFacts Facts);

public interface IStateBriefBuilder
{
  Task<StateBrief> BuildAsync(Person person, DateOnly today, CancellationToken ct = default);
}

/// <summary>A day log entry picked out of what the person said, ready for IJournalService.SaveDraftAsync.</summary>
public sealed record CaptureDraft(string Kind, string? Text, IReadOnlyList<string> Tags, string? Portion, DateTime At);

/// <summary>A durable fact for ai_memory ("I'm vegetarian").</summary>
public sealed record MemoryFact(string Type, string Key, string Value);

public sealed record CaptureResult(IReadOnlyList<CaptureDraft> Drafts, IReadOnlyList<MemoryFact> Facts)
{
  public static readonly CaptureResult Empty = new([], []);
}

/// <summary>The lexicon extractor: statements become day log drafts; questions and plans do not.</summary>
public interface IJournalCapture
{
  /// <param name="topic">A check-in topic hint (eating, training, drinks, symptoms): fragments count as statements about it.</param>
  CaptureResult Extract(string message, DateTime nowUtc, TimeZoneInfo zone, string? topic = null);
}

/// <summary>What a reply may draw on: the numbers it may state and the text that names allowed units and remedies.</summary>
public sealed record GuardContext(IReadOnlySet<string> Numbers, string AllowedText)
{
  public static readonly GuardContext Empty = new(new HashSet<string>(), string.Empty);
}

public sealed record GuardVerdict(bool Ok, string Text, string? Reason)
{
  public static GuardVerdict Pass(string text) => new(true, text, null);
  public static GuardVerdict Fail(string reason) => new(false, string.Empty, reason);
}

public interface IReplyGuard
{
  /// <summary>The fixed "get care" reply when the message names a red-flag symptom, else null. The model is never called for these.</summary>
  string? RedFlagReply(string userMessage);

  /// <summary>One sentence of a streamed reply, checked before it is sent.</summary>
  GuardVerdict CheckSentence(string sentence, GuardContext context);

  /// <summary>The whole reply: every sentence rule plus length.</summary>
  GuardVerdict CheckReply(string reply, GuardContext context);

  /// <summary>An encouragement or brief line: no number beyond the allowed set, no promise, short.</summary>
  GuardVerdict CheckLine(string line, GuardContext context);
}

/// <summary>A user message saved and captured, ready to be answered.</summary>
public sealed record AssistantTurn(
  Actor Actor,
  Guid ConversationId,
  Guid UserMessageId,
  CaptureEventDto Capture,
  string? FixedReply,
  IReadOnlyList<ChatTurn> ModelTurns,
  GuardContext Guard,
  string Fallback,
  string Unavailable);

/// <summary>One server-sent event: capture, delta, replace or done, with its JSON payload.</summary>
public sealed record AssistantStreamEvent(string Event, object Data)
{
  public const string Capture = "capture";
  public const string Delta = "delta";
  public const string Replace = "replace";
  public const string Done = "done";
}

public interface IAssistantService
{
  Task<AssistantStatusDto> StatusAsync(CancellationToken ct = default);

  Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(Guid personId, CancellationToken ct = default);

  Task<ConversationSummaryDto> CreateAsync(Actor actor, CreateConversationRequest request, CancellationToken ct = default);

  /// <summary>Null when the conversation is not the caller's.</summary>
  Task<ConversationDto?> GetAsync(Guid personId, Guid conversationId, CancellationToken ct = default);

  /// <summary>False when the conversation is not the caller's. What it saved to the day log stays.</summary>
  Task<bool> DeleteAsync(Actor actor, Guid conversationId, CancellationToken ct = default);

  /// <summary>Saves the message, captures what it says and prepares the reply. Null when the conversation is not the caller's.</summary>
  Task<AssistantTurn?> BeginTurnAsync(Actor actor, Guid conversationId, string text, CancellationToken ct = default);

  /// <summary>The reply as checked sentences, ending with done. Stores the reply.</summary>
  IAsyncEnumerable<AssistantStreamEvent> ReplyAsync(AssistantTurn turn, CancellationToken ct = default);

  /// <summary>Removes what one user message saved. Null when the message is not the caller's.</summary>
  Task<UndoResultDto?> UndoCapturesAsync(Actor actor, Guid messageId, CancellationToken ct = default);
}

public interface ICheckinFlowService
{
  Task<CheckinTurnDto> TurnAsync(Actor actor, CheckinTurnRequest request, CancellationToken ct = default);

  Task<CheckinTodayDto> TodayAsync(Guid personId, CancellationToken ct = default);
}

public interface IMorningBriefService
{
  /// <summary>Null when the person does not exist.</summary>
  Task<MorningBriefDto?> GetAsync(Guid personId, CancellationToken ct = default);
}

public interface IPlaybookService
{
  /// <summary>The reviewed entries that apply to the person today, with why.</summary>
  Task<IReadOnlyList<PlaybookMatch>> ApplicableAsync(Person person, DateOnly today, CancellationToken ct = default);
}

public interface INudgeService
{
  Task<NudgeSettingsDto> GetAsync(Guid personId, CancellationToken ct = default);

  Task<NudgeSettingsDto> SaveAsync(Actor actor, SaveNudgeSettingsRequest request, CancellationToken ct = default);

  Task<NudgeTestResultDto> TestAsync(Actor actor, CancellationToken ct = default);
}

/// <summary>Posts a fixed text to the person's own ntfy topic. The only outbound call besides the local model.</summary>
public interface INtfySender
{
  /// <summary>Null on success, else a short reason that never contains the address or the token.</summary>
  Task<string?> SendAsync(string url, string? token, string title, string message, CancellationToken ct = default);
}

public interface IModelCheckService
{
  Task<ModelCheckDto> RunAsync(Actor actor, CancellationToken ct = default);
}
