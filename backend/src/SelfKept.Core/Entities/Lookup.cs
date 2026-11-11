namespace SelfKept.Core.Entities;

// The cloud lookup (docs/lookup-design.md). Own-only like the rest of the assistant: nobody can
// turn the switch on for someone else, read what someone else sent, or delete it.

/// <summary>One person's own switch for the cloud lookup. No row means off.</summary>
public class LookupSetting
{
  public Guid PersonId { get; set; }
  public bool Enabled { get; set; }

  /// <summary>When the person last turned it on (with the acknowledgement ticked). Null while it was never on.</summary>
  public DateTime? EnabledAt { get; set; }

  public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// One question sent to the cloud model. The outgoing text and the sources are encrypted. The
/// answer itself is an assistant_message with origin cloud whose meta holds this row's id.
/// </summary>
public class AssistantLookup
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }
  public Guid ConversationId { get; set; }

  /// <summary>The person's own message the lookup was drafted from.</summary>
  public Guid? QuestionMessageId { get; set; }

  /// <summary>The stored answer, once there is one.</summary>
  public Guid? AnswerMessageId { get; set; }

  /// <summary>anonymized or detailed. See <see cref="LookupModes"/>.</summary>
  public string Mode { get; set; } = LookupModes.Anonymized;

  /// <summary>sent, answered or failed. See <see cref="LookupStatuses"/>.</summary>
  public string Status { get; set; } = LookupStatuses.Sent;

  /// <summary>Exactly the text that left the server, encrypted.</summary>
  public string QuestionCipher { get; set; } = string.Empty;

  /// <summary>JSON list of { title, url }, encrypted. Null until answered.</summary>
  public string? SourcesCipher { get; set; }

  public string Provider { get; set; } = string.Empty;

  /// <summary>The model asked, replaced by the model that answered once there is an answer.</summary>
  public string Model { get; set; } = string.Empty;

  public int? InputTokens { get; set; }
  public int? OutputTokens { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime? AnsweredAt { get; set; }
}

public static class LookupModes
{
  /// <summary>The person's question with hard identifiers, dates, ages and exact numbers taken out.</summary>
  public const string Anonymized = "anonymized";
  /// <summary>The question minus hard identifiers, plus an "About me" block from the state brief.</summary>
  public const string Detailed = "detailed";

  public static readonly string[] All = [Anonymized, Detailed];

  public static bool IsKnown(string? mode) => mode != null && Array.IndexOf(All, mode) >= 0;
}

public static class LookupStatuses
{
  public const string Sent = "sent";
  public const string Answered = "answered";
  public const string Failed = "failed";
}

/// <summary>Why a lookup came back without an answer: the failed event's reason.</summary>
public static class LookupFailureReasons
{
  public const string Refused = "refused";
  public const string CutOff = "cut_off";
  public const string SearchFailed = "search_failed";
  public const string Unreachable = "unreachable";
  public const string RateLimited = "rate_limited";
  public const string KeyRejected = "key_rejected";
}

/// <summary>What a draft changed, and what the send check found: the kind of each change.</summary>
public static class LookupChangeKinds
{
  public const string Name = "name";
  public const string Email = "email";
  public const string Phone = "phone";
  public const string Link = "link";
  public const string Date = "date";
  public const string Age = "age";
  public const string Number = "number";
  /// <summary>The "About me" block from the state brief was added.</summary>
  public const string Record = "record";
}
