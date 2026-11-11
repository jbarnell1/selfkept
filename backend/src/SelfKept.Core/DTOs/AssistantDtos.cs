using System.ComponentModel.DataAnnotations;

namespace SelfKept.Core.DTOs;

// The in-app assistant (docs/assistant-design.md). Everything here is the caller's own.

/// <summary>GET /api/assistant/status. Reachable is false whenever Enabled is false.</summary>
public sealed record AssistantStatusDto(bool Enabled, string? Model, bool Reachable);

public sealed record ConversationSummaryDto(
  Guid Id,
  string Kind,
  string Title,
  DateOnly? Day,
  DateTime CreatedAt,
  DateTime UpdatedAt);

/// <summary>Something one user message saved: a day log entry, a remembered fact or a mood check-in.</summary>
public sealed record CaptureRefDto(string Type, Guid Id, string? Kind, IReadOnlyList<string> Tags);

/// <summary>
/// One turn. Sources is empty unless the origin is cloud; LookupId is null unless the origin is
/// cloud and the lookup that produced it has not been deleted (docs/lookup-design.md).
/// </summary>
public sealed record AssistantMessageDto(
  Guid Id,
  string Role,
  string Content,
  string Origin,
  DateTime CreatedAt,
  IReadOnlyList<CaptureRefDto> Captures,
  bool CapturesUndone,
  bool RedFlag,
  IReadOnlyList<LookupSourceDto> Sources,
  Guid? LookupId);

public sealed record ConversationDto(
  Guid Id,
  string Kind,
  string Title,
  DateOnly? Day,
  DateTime CreatedAt,
  DateTime UpdatedAt,
  IReadOnlyList<AssistantMessageDto> Messages);

public sealed record CreateConversationRequest([MaxLength(80)] string? Title);

public sealed record SendMessageRequest([Required, MaxLength(2000)] string Text);

/// <summary>SSE event "capture": what the user message saved, sent before the reply starts.</summary>
public sealed record CaptureEventDto(
  Guid ConversationId,
  Guid MessageId,
  IReadOnlyList<JournalEntryDto> Entries,
  IReadOnlyList<AiMemoryDto> Remembered);

/// <summary>SSE event "delta": the next checked sentence of the reply, with its trailing space.</summary>
public sealed record DeltaEventDto(string Text);

/// <summary>SSE event "replace": throw away everything shown so far and show this instead.</summary>
public sealed record ReplaceEventDto(string Text, string Reason);

/// <summary>
/// SSE event "done": the stored reply. Origin is model, template or guard. RedFlag is true when
/// code answered a red flag, so the client shows the reply prominently and in full. Lookup is the
/// cloud lookup offer under the reply, or null when there is none.
/// </summary>
public sealed record DoneEventDto(Guid ConversationId, Guid MessageId, Guid UserMessageId, string Origin, string Text, bool RedFlag = false, LookupOfferDto? Lookup = null);

public sealed record UndoResultDto(Guid MessageId, int RemovedEntries, int RemovedMemories, int RemovedCheckins);

/// <summary>POST /api/assistant/checkin. Both fields optional: an empty body starts or resumes.</summary>
public sealed record CheckinTurnRequest([MaxLength(2000)] string? Answer, bool? Leave);

public sealed record CheckinTurnDto(
  Guid ConversationId,
  DateOnly Day,
  string Status,
  string? Topic,
  string? Encouragement,
  string? Question,
  string Reply,
  IReadOnlyList<JournalEntryDto> Captured,
  int? Mood,
  Guid? UserMessageId,
  Guid AssistantMessageId,
  IReadOnlyList<string> Done,
  IReadOnlyList<string> Skipped,
  IReadOnlyList<string> Remaining,
  bool RedFlag);

public sealed record CheckinTodayDto(
  DateOnly Day,
  string Status,
  Guid? ConversationId,
  string? Topic,
  string? Question,
  IReadOnlyList<string> Missing,
  IReadOnlyList<string> Done,
  IReadOnlyList<string> Skipped);

public sealed record MorningBriefItemDto(string Kind, string Text);

public sealed record MorningBriefDto(
  DateOnly Day,
  string Text,
  bool Generated,
  IReadOnlyList<MorningBriefItemDto> Items,
  DateTime ComputedAt);

public sealed record PlaybookEntryDto(
  string Key,
  string Topic,
  string Title,
  string Text,
  string EvidenceGrade,
  string Source,
  string SourceUrl,
  string Why,
  IReadOnlyList<string> Cautions);

public sealed record NudgeSettingsDto(
  bool Enabled,
  bool UrlSet,
  string? NtfyUrlMasked,
  bool TokenSet,
  string EveningTime,
  string TimeZone,
  DateOnly? LastSentOn);

/// <summary>
/// PUT /api/assistant/nudges. For ntfyUrl and ntfyToken, null keeps what is stored and an empty
/// string clears it. eveningTime is "HH:mm" local; null keeps the stored time.
/// </summary>
public sealed record SaveNudgeSettingsRequest(
  bool Enabled,
  [MaxLength(500)] string? NtfyUrl,
  [MaxLength(200)] string? NtfyToken,
  [MaxLength(5)] string? EveningTime);

public sealed record NudgeTestResultDto(bool Sent, string? Error);

public sealed record ModelCheckCaseDto(string Name, string Checks, bool Passed, string Detail, int LatencyMs);

public sealed record ModelCheckDto(
  bool Enabled,
  string? Model,
  bool Passed,
  int PassedCount,
  int Total,
  IReadOnlyList<ModelCheckCaseDto> Cases,
  DateTime CheckedAt);
