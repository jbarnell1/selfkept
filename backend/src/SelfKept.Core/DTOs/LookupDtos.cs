using System.ComponentModel.DataAnnotations;

namespace SelfKept.Core.DTOs;

// The cloud lookup (docs/lookup-design.md). Everything here is the caller's own.

/// <summary>GET and PUT /api/assistant/lookup. LocalModel says whether the "applied locally" step will run.</summary>
public sealed record LookupStatusDto(bool Available, bool Enabled, string Provider, string Model, bool LocalModel);

/// <summary>PUT /api/assistant/lookup. Turning it on needs Acknowledged; turning it off does not.</summary>
public sealed record SaveLookupSettingRequest(bool Enabled, bool Acknowledged);

/// <summary>One kind of change a draft made, or one kind of hard identifier the send check found.</summary>
public sealed record LookupChangeDto(string Kind, int Count);

/// <summary>POST /api/assistant/lookups/draft. MessageId is one of the caller's own user messages.</summary>
public sealed record LookupDraftRequest([Required] Guid MessageId, [Required, MaxLength(16)] string Mode);

public sealed record LookupDraftDto(
  string Text,
  string Mode,
  IReadOnlyList<LookupChangeDto> Changes,
  bool Reworded,
  string Provider,
  string Model);

/// <summary>POST /api/assistant/lookups. Text is exactly what the person saw and may have edited.</summary>
public sealed record SendLookupRequest(
  [Required] Guid MessageId,
  [Required, MaxLength(16)] string Mode,
  [Required, MaxLength(4000)] string Text,
  bool Acknowledged);

public sealed record LookupSourceDto(string Title, string Url);

/// <summary>The chat done event's lookup field. Reason is question, refused (the guard replaced the reply) or model_off.</summary>
public sealed record LookupOfferDto(bool Offered, string Reason);

/// <summary>SSE event "sent": the request has gone to the provider.</summary>
public sealed record LookupSentEventDto(Guid LookupId);

/// <summary>SSE event "waiting", every 10 seconds while the provider works. Empty on purpose.</summary>
public sealed record LookupWaitingEventDto;

/// <summary>SSE event "done": the stored answer, and the local "applied" message when one passed the guard.</summary>
public sealed record LookupDoneEventDto(Guid LookupId, AssistantMessageDto Message, AssistantMessageDto? Applied);

/// <summary>SSE event "failed": nothing was stored as an answer.</summary>
public sealed record LookupFailedEventDto(Guid LookupId, string Reason, string Text);

/// <summary>GET /api/assistant/lookups: one lookup the caller sent, with the exact outgoing text.</summary>
public sealed record LookupSummaryDto(
  Guid Id,
  Guid ConversationId,
  string Mode,
  string Status,
  string Provider,
  string Model,
  string Text,
  DateTime SentAt);
