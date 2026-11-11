namespace SelfKept.Core.Entities;

// The in-app assistant (docs/assistant-design.md). Everything here is the person's own: none of
// these rows is ever shared with another household member, so there is no share category for them.

/// <summary>One chat, or one day's evening check-in. The title is encrypted; the state carries no free text.</summary>
public class AssistantConversation
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }

  /// <summary>chat or checkin. See <see cref="ConversationKinds"/>.</summary>
  public string Kind { get; set; } = ConversationKinds.Chat;

  public string? TitleCipher { get; set; }

  /// <summary>The local day a check-in belongs to. Null for a chat.</summary>
  public DateOnly? Day { get; set; }

  /// <summary>JSON for the check-in state machine: topic keys and flags only, never anything the person typed.</summary>
  public string State { get; set; } = "{}";

  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
  public DateTime? EndedAt { get; set; }
}

/// <summary>One turn. The content is encrypted at rest; meta carries ids and keys only.</summary>
public class AssistantMessage
{
  public Guid Id { get; set; }
  public Guid ConversationId { get; set; }
  public Guid PersonId { get; set; }

  /// <summary>user or assistant. See <see cref="MessageRoles"/>.</summary>
  public string Role { get; set; } = MessageRoles.User;

  public string ContentCipher { get; set; } = string.Empty;

  /// <summary>Who worded it: model, template, guard or code. See <see cref="MessageOrigins"/>.</summary>
  public string Origin { get; set; } = MessageOrigins.Code;

  /// <summary>JSON: remembered fact ids, the check-in topic, the playbook key. No free text.</summary>
  public string Meta { get; set; } = "{}";

  public DateTime CreatedAt { get; set; }
}

/// <summary>Where and when one person wants the evening reminder. The address and token are encrypted.</summary>
public class NudgeSetting
{
  public Guid PersonId { get; set; }
  public bool Enabled { get; set; }
  public string? NtfyUrlCipher { get; set; }
  public string? NtfyTokenCipher { get; set; }

  /// <summary>Local time in the person's zone.</summary>
  public TimeOnly EveningTime { get; set; } = NudgeDefaults.EveningTime;

  public DateTime UpdatedAt { get; set; }
}

/// <summary>One row per reminder sent, so a person gets at most one of a kind a day.</summary>
public class NudgeLog
{
  public Guid PersonId { get; set; }

  /// <summary>See <see cref="NudgeKinds"/>.</summary>
  public string Kind { get; set; } = NudgeKinds.Evening;

  public DateOnly Day { get; set; }
  public DateTime SentAt { get; set; }
}

/// <summary>The morning brief of one person, kept for the day and replaced when its facts change.</summary>
public class AssistantBrief
{
  public Guid PersonId { get; set; }
  public DateOnly Day { get; set; }

  /// <summary>Hex SHA-256 of the facts the text was written from.</summary>
  public string FactsHash { get; set; } = string.Empty;

  public string TextCipher { get; set; } = string.Empty;

  /// <summary>True when the model reworded the templated text and the result passed the guard.</summary>
  public bool Generated { get; set; }

  public DateTime ComputedAt { get; set; }
}

public static class ConversationKinds
{
  public const string Chat = "chat";
  public const string Checkin = "checkin";
}

public static class MessageRoles
{
  public const string User = "user";
  public const string Assistant = "assistant";
}

public static class MessageOrigins
{
  /// <summary>Worded by the local model and passed by the guard.</summary>
  public const string Model = "model";
  /// <summary>Worded from a template (the model is off, unreachable, or was not asked).</summary>
  public const string Template = "template";
  /// <summary>The guard replaced what the model wrote, or answered a red-flag symptom itself.</summary>
  public const string Guard = "guard";
  /// <summary>Typed by the person, or produced by the check-in state machine.</summary>
  public const string Code = "code";

  public static readonly string[] All = [Model, Template, Guard, Code];
}

public static class NudgeKinds
{
  public const string Evening = "evening";
}

public static class NudgeDefaults
{
  public static readonly TimeOnly EveningTime = new(20, 0);
}

/// <summary>The five things the evening check-in asks about, in the order it asks.</summary>
public static class CheckinTopics
{
  public const string Eating = "eating";
  public const string Training = "training";
  public const string Drinks = "drinks";
  public const string Symptoms = "symptoms";
  public const string Mood = "mood";

  public static readonly string[] All = [Eating, Training, Drinks, Symptoms, Mood];

  public static bool IsKnown(string? topic) => topic != null && Array.IndexOf(All, topic) >= 0;
}

/// <summary>
/// Day log tags only the evening check-in writes. Each says "asked, and the answer was none", so
/// the day counts as known to a target (TargetTally needs an entry that day) without a new column.
/// The journal accepts any well-shaped tag. Training uses the built-in rest_day tag and eating the
/// built-in skipped_meal tag for the same purpose.
/// </summary>
public static class AssistantTags
{
  public const string NoAlcohol = "no_alcohol";
  public const string NoCaffeine = "no_caffeine";
  public const string NoSymptoms = "no_symptoms";

  public static readonly string[] All = [NoAlcohol, NoCaffeine, NoSymptoms];
}

public static class AssistantAuditActions
{
  public const string ConversationCreated = "assistant.conversation_created";
  public const string ConversationDeleted = "assistant.conversation_deleted";
  public const string CapturesUndone = "assistant.captures_undone";
  public const string CheckinStarted = "assistant.checkin_started";
  public const string RedFlagAnswered = "assistant.red_flag_answered";
  public const string NudgesChanged = "assistant.nudges_changed";
  public const string NudgeTested = "assistant.nudge_tested";
  public const string ModelChecked = "assistant.model_checked";
}

/// <summary>Purposes written to ai_call for assistant work (32 characters at most).</summary>
public static class AssistantAiPurposes
{
  public const string Chat = "assistant_chat";
  public const string CheckinLine = "assistant_checkin";
  public const string Brief = "assistant_brief";
  public const string ModelCheck = "assistant_model_check";
}

/// <summary>The states GET /api/assistant/checkin/today reports.</summary>
public static class CheckinStatuses
{
  public const string NotStarted = "not_started";
  public const string Partial = "partial";
  public const string Done = "done";
}
