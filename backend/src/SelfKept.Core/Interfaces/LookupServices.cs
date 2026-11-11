using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;

namespace SelfKept.Core.Interfaces;

// The cloud lookup (docs/lookup-design.md): one question the person read and confirmed, sent to a
// cloud model with web search. Own-only: every repository method takes the caller's own person id
// and is named Own, and there is no viewer and target pair anywhere here.

public interface ILookupRepository
{
  Task<LookupSetting?> GetOwnSettingAsync(Guid personId, CancellationToken ct = default);

  /// <summary>Creates or replaces the person's own row.</summary>
  Task SaveOwnSettingAsync(LookupSetting setting, CancellationToken ct = default);

  Task<AssistantLookup> AddOwnAsync(AssistantLookup lookup, CancellationToken ct = default);

  /// <summary>Null when the lookup is not the person's own or does not exist.</summary>
  Task<AssistantLookup?> GetOwnAsync(Guid personId, Guid lookupId, CancellationToken ct = default);

  /// <summary>Newest first.</summary>
  Task<IReadOnlyList<AssistantLookup>> ListOwnAsync(Guid personId, int limit, CancellationToken ct = default);

  /// <summary>The person's lookups whose id is in the list (for attaching sources to a conversation's cloud messages).</summary>
  Task<IReadOnlyList<AssistantLookup>> ListOwnByIdsAsync(Guid personId, IReadOnlyCollection<Guid> lookupIds, CancellationToken ct = default);

  Task UpdateOwnAsync(AssistantLookup lookup, CancellationToken ct = default);

  /// <summary>False when the lookup is not the person's own.</summary>
  Task<bool> RemoveOwnAsync(Guid personId, Guid lookupId, CancellationToken ct = default);
}

/// <summary>Why a cloud call came back without an answer. See <see cref="LookupFailureReasons"/> for the wire words.</summary>
public enum CloudLookupOutcome
{
  Answered,
  Refused,
  CutOff,
  SearchFailed,
  Unreachable,
  RateLimited,
  KeyRejected,
}

/// <summary>
/// What the provider gave back. Text and Sources are empty unless the outcome is Answered. Model
/// is the model that answered (a server-side fallback can differ from the one asked). Error is a
/// short reason for ai_call (an exception type or a stop reason), never provider text.
/// </summary>
public sealed record CloudLookupResult(
  CloudLookupOutcome Outcome,
  string Text,
  IReadOnlyList<LookupSourceDto> Sources,
  string? Model,
  int? InputTokens,
  int? OutputTokens,
  string? Error = null)
{
  public static CloudLookupResult Failed(CloudLookupOutcome outcome, string error, string? model = null, int? inputTokens = null, int? outputTokens = null) =>
    new(outcome, string.Empty, [], model, inputTokens, outputTokens, error);
}

/// <summary>
/// The one cloud call. The request holds the fixed system prompt and the text, nothing else. An
/// implementation never logs the text, the answer or the key, and never throws for a provider
/// failure: every failure is an outcome. Only cancellation of <paramref name="ct"/> throws.
/// </summary>
public interface ICloudLookupClient
{
  /// <summary>The provider's name as the person sees it ("Anthropic").</summary>
  string Provider { get; }

  /// <summary>The model asked.</summary>
  string Model { get; }

  /// <summary>Enabled and a key is set.</summary>
  bool IsUsable { get; }

  Task<CloudLookupResult> AskAsync(string text, CancellationToken ct = default);
}

/// <summary>A checked send, ready to go to the provider. Built before the stream starts so problems are plain 4xx answers.</summary>
public sealed record LookupSend(
  Actor Actor,
  Guid ConversationId,
  Guid QuestionMessageId,
  string Mode,
  string Text);

/// <summary>The SSE event names of POST /api/assistant/lookups.</summary>
public static class LookupStreamEvents
{
  public const string Sent = "sent";
  public const string Waiting = "waiting";
  public const string Done = "done";
  public const string Failed = "failed";
}

public interface ILookupService
{
  /// <summary>The server switch: enabled and a key set. Every route answers 404 when false.</summary>
  bool Available { get; }

  Task<LookupStatusDto> GetAsync(Guid personId, CancellationToken ct = default);

  /// <summary>Turning it on without the acknowledgement throws <see cref="Exceptions.UnprocessableException"/>.</summary>
  Task<LookupStatusDto> SaveAsync(Actor actor, SaveLookupSettingRequest request, CancellationToken ct = default);

  /// <summary>Null when the message is not one of the caller's own user messages. Nothing is sent or stored.</summary>
  Task<LookupDraftDto?> DraftAsync(Actor actor, LookupDraftRequest request, CancellationToken ct = default);

  /// <summary>Checks a send (switch, acknowledgement, red flag, hard identifiers). Null when the message is not the caller's.</summary>
  Task<LookupSend?> PrepareSendAsync(Actor actor, SendLookupRequest request, CancellationToken ct = default);

  /// <summary>
  /// Sends, waits and stores: sent, waiting every 10 seconds, then done or failed. Once sent the
  /// work runs to the end whatever happens to <paramref name="ct"/> or to the reader, so the
  /// answer the person paid for is always stored.
  /// </summary>
  IAsyncEnumerable<AssistantStreamEvent> SendAsync(LookupSend send, CancellationToken ct = default);

  Task<IReadOnlyList<LookupSummaryDto>> ListAsync(Guid personId, CancellationToken ct = default);

  /// <summary>False when the lookup is not the caller's. The answer stays in the conversation.</summary>
  Task<bool> DeleteAsync(Actor actor, Guid lookupId, CancellationToken ct = default);

  /// <summary>The offer under a chat reply, or null (server or person switch off, not a question, a red flag).</summary>
  Task<LookupOfferDto?> OfferAsync(Guid personId, string userMessage, string replyOrigin, bool redFlag, CancellationToken ct = default);
}
