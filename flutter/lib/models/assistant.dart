import '../utils/json_reader.dart';
import 'connect.dart';
import 'journal.dart';

// The in-app assistant (docs/assistant-design.md). Everything here is the
// caller's own; nothing is shared with the household.

/// GET /assistant/status.
class AssistantStatus {
  final bool enabled;
  final String? model;
  final bool reachable;

  /// Photos are turned on and the model settings are usable. The photo
  /// button shows only then.
  final bool photos;

  const AssistantStatus({
    this.enabled = false,
    this.model,
    this.reachable = false,
    this.photos = false,
  });

  /// True when the home model is set up and answered just now. Otherwise the
  /// server still replies, from templates.
  bool get modelAnswering => enabled && reachable;

  factory AssistantStatus.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return AssistantStatus(
      enabled: r.boolean('enabled'),
      model: r.stringOrNull('model'),
      reachable: r.boolean('reachable'),
      photos: r.boolean('photos'),
    );
  }
}

/// A plain chat, or the evening check-in of one day.
enum ConversationKind {
  chat,
  checkin;

  String get wireValue => name;
  String toJson() => wireValue;

  static ConversationKind fromString(String? value) =>
      value?.trim().toLowerCase() == 'checkin' ? checkin : chat;

  String get displayName => switch (this) {
    chat => 'Conversation',
    checkin => 'Evening check-in',
  };
}

/// One row of GET /assistant/conversations.
class ConversationSummary {
  final String id;
  final ConversationKind kind;
  final String title;

  /// The local day of a check-in; null for a chat.
  final DateTime? day;
  final DateTime? createdAt;
  final DateTime? updatedAt;

  const ConversationSummary({
    required this.id,
    this.kind = ConversationKind.chat,
    this.title = '',
    this.day,
    this.createdAt,
    this.updatedAt,
  });

  factory ConversationSummary.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ConversationSummary(
      id: r.string('id'),
      kind: ConversationKind.fromString(r.stringOrNull('kind')),
      title: r.string('title'),
      day: r.dateTime('day'),
      createdAt: r.dateTime('createdAt'),
      updatedAt: r.dateTime('updatedAt'),
    );
  }

  static List<ConversationSummary> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) ConversationSummary.fromJson(item),
  ];
}

enum MessageRole {
  user,
  assistant;

  String get wireValue => name;
  String toJson() => wireValue;

  static MessageRole fromString(String? value) =>
      value?.trim().toLowerCase() == 'user' ? user : assistant;

  String get displayName => switch (this) {
    user => 'You',
    assistant => 'Assistant',
  };
}

/// Who worded a message: the model (and the guard passed it), a template,
/// the guard (it replaced the answer, or code answered a red flag), the
/// person, or a cloud model the person sent one question to.
enum MessageOrigin {
  model,
  template,
  guard,
  code,
  cloud;

  String get wireValue => name;
  String toJson() => wireValue;

  static MessageOrigin fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final o in values) {
      if (o.name == lower) return o;
    }
    return template;
  }

  String get displayName => switch (this) {
    model => 'Home model',
    template => 'From your record',
    guard => 'Checked reply',
    code => 'You',
    cloud => 'Cloud model',
  };
}

/// What one user message saved: a day log entry, a remembered fact or a
/// mood check-in.
enum CaptureType {
  journal,
  memory,
  mood;

  String get wireValue => name;
  String toJson() => wireValue;

  static CaptureType fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final t in values) {
      if (t.name == lower) return t;
    }
    return journal;
  }

  String get displayName => switch (this) {
    journal => 'Day log',
    memory => 'Remembered',
    mood => 'Mood',
  };
}

/// One saved thing. [text] is filled only from a live capture (the entry's
/// words, or the fact as written); a reloaded message has kind and tags.
class CaptureRef {
  final CaptureType type;
  final String id;
  final String? kind;
  final List<String> tags;
  final String? text;

  const CaptureRef({
    required this.type,
    required this.id,
    this.kind,
    this.tags = const [],
    this.text,
  });

  factory CaptureRef.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return CaptureRef(
      type: CaptureType.fromString(r.stringOrNull('type')),
      id: r.string('id'),
      kind: r.stringOrNull('kind'),
      tags: r.strings('tags'),
    );
  }

  factory CaptureRef.entry(JournalEntry entry) => CaptureRef(
    type: CaptureType.journal,
    id: entry.id,
    kind: entry.kind.wireValue,
    tags: entry.tags,
    text: entry.text,
  );

  factory CaptureRef.memory(AiMemoryItem memory) => CaptureRef(
    type: CaptureType.memory,
    id: memory.id,
    kind: memory.type.wireValue,
    tags: [memory.key],
    text: memory.displayText ?? memory.value,
  );
}

/// One source a cloud answer cites.
class LookupSource {
  final String title;
  final String url;

  const LookupSource({this.title = '', required this.url});

  /// Only a real http or https address is offered as a link.
  Uri? get link {
    final uri = Uri.tryParse(url.trim());
    if (uri == null || uri.host.isEmpty) return null;
    if (uri.scheme != 'https' && uri.scheme != 'http') return null;
    return uri;
  }

  /// The title, or the address when the source has none.
  String get label => title.trim().isEmpty ? url : title.trim();

  factory LookupSource.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LookupSource(title: r.string('title'), url: r.string('url'));
  }
}

/// The `lookup` object on a chat reply's done event: whether the reply may
/// be followed by a cloud lookup. Tapping the offer sends nothing.
class LookupOffer {
  final bool offered;

  /// `question`, `refused` (the guard replaced the reply) or `model_off`.
  final String reason;

  const LookupOffer({this.offered = false, this.reason = ''});

  factory LookupOffer.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LookupOffer(
      offered: r.boolean('offered'),
      reason: r.string('reason'),
    );
  }
}

/// One message of a conversation, oldest first.
class AssistantMessage {
  final String id;
  final MessageRole role;
  final String content;
  final MessageOrigin origin;
  final DateTime? createdAt;

  /// What this user message saved; empty for replies.
  final List<CaptureRef> captures;

  /// True once Undo ran for this message.
  final bool capturesUndone;

  /// True for a reply that code wrote for a red flag (urgent care or crisis
  /// lines). It is shown prominently and in full.
  final bool redFlag;

  /// What a cloud answer cites; empty unless [origin] is cloud.
  final List<LookupSource> sources;

  /// The lookup a cloud answer came from. Null once that lookup was deleted,
  /// and for every other message.
  final String? lookupId;

  const AssistantMessage({
    required this.id,
    required this.role,
    required this.content,
    this.origin = MessageOrigin.template,
    this.createdAt,
    this.captures = const [],
    this.capturesUndone = false,
    this.redFlag = false,
    this.sources = const [],
    this.lookupId,
  });

  factory AssistantMessage.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return AssistantMessage(
      id: r.string('id'),
      role: MessageRole.fromString(r.stringOrNull('role')),
      content: r.string('content'),
      origin: MessageOrigin.fromString(r.stringOrNull('origin')),
      createdAt: r.dateTime('createdAt'),
      captures: [for (final c in r.objects('captures')) CaptureRef.fromJson(c)],
      capturesUndone: r.boolean('capturesUndone'),
      redFlag: r.boolean('redFlag'),
      sources: [for (final s in r.objects('sources')) LookupSource.fromJson(s)],
      lookupId: r.stringOrNull('lookupId'),
    );
  }
}

/// GET /assistant/conversations/{id}.
class Conversation {
  final ConversationSummary summary;
  final List<AssistantMessage> messages;

  const Conversation({required this.summary, this.messages = const []});

  String get id => summary.id;

  factory Conversation.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Conversation(
      summary: ConversationSummary.fromJson(json),
      messages: [
        for (final m in r.objects('messages')) AssistantMessage.fromJson(m),
      ],
    );
  }
}

/// One event of a streamed reply.
sealed class AssistantStreamEvent {
  const AssistantStreamEvent();
}

/// What the user message saved, before the reply starts.
class CaptureEvent extends AssistantStreamEvent {
  final String conversationId;
  final String messageId;
  final List<JournalEntry> entries;
  final List<AiMemoryItem> remembered;

  const CaptureEvent({
    required this.conversationId,
    required this.messageId,
    this.entries = const [],
    this.remembered = const [],
  });

  List<CaptureRef> get refs => [
    for (final e in entries) CaptureRef.entry(e),
    for (final m in remembered) CaptureRef.memory(m),
  ];

  factory CaptureEvent.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return CaptureEvent(
      conversationId: r.string('conversationId'),
      messageId: r.string('messageId'),
      entries: [for (final e in r.objects('entries')) JournalEntry.fromJson(e)],
      remembered: [
        for (final m in r.objects('remembered')) AiMemoryItem.fromJson(m),
      ],
    );
  }
}

/// The next checked sentence. Every delta after the first starts with a
/// space, so they are appended as they come.
class DeltaEvent extends AssistantStreamEvent {
  final String text;

  const DeltaEvent(this.text);

  factory DeltaEvent.fromJson(Map<String, dynamic> json) =>
      DeltaEvent(JsonReader(json).string('text'));
}

/// Throw away what was shown for this reply and show [text] instead.
class ReplaceEvent extends AssistantStreamEvent {
  final String text;

  /// `guard` or `unreachable`.
  final String reason;

  const ReplaceEvent(this.text, {this.reason = ''});

  factory ReplaceEvent.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ReplaceEvent(r.string('text'), reason: r.string('reason'));
  }
}

/// The stored reply. The stream closes after it.
class DoneEvent extends AssistantStreamEvent {
  final String conversationId;
  final String messageId;
  final String userMessageId;
  final MessageOrigin origin;
  final String text;

  /// True when code answered a red flag.
  final bool redFlag;

  /// Whether a cloud lookup is offered under this reply; null from a server
  /// without the lookup.
  final LookupOffer? lookup;

  const DoneEvent({
    required this.conversationId,
    required this.messageId,
    required this.userMessageId,
    required this.origin,
    required this.text,
    this.redFlag = false,
    this.lookup,
  });

  bool get lookupOffered => lookup?.offered ?? false;

  factory DoneEvent.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return DoneEvent(
      conversationId: r.string('conversationId'),
      messageId: r.string('messageId'),
      userMessageId: r.string('userMessageId'),
      origin: MessageOrigin.fromString(r.stringOrNull('origin')),
      text: r.string('text'),
      redFlag: r.boolean('redFlag'),
      lookup: switch (r.object('lookup')) {
        final o? => LookupOffer.fromJson(o),
        null => null,
      },
    );
  }
}

/// DELETE /assistant/messages/{id}/captures.
class UndoResult {
  final String messageId;
  final int removedEntries;
  final int removedMemories;
  final int removedCheckins;

  const UndoResult({
    required this.messageId,
    this.removedEntries = 0,
    this.removedMemories = 0,
    this.removedCheckins = 0,
  });

  factory UndoResult.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return UndoResult(
      messageId: r.string('messageId'),
      removedEntries: r.integer('removedEntries'),
      removedMemories: r.integer('removedMemories'),
      removedCheckins: r.integer('removedCheckins'),
    );
  }
}

/// The evening check-in's topics, in asking order.
enum CheckinTopic {
  eating,
  training,
  drinks,
  symptoms,
  mood;

  String get wireValue => name;
  String toJson() => wireValue;

  static CheckinTopic? fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final t in values) {
      if (t.name == lower) return t;
    }
    return null;
  }

  static List<CheckinTopic> listFrom(List<String> values) => [
    for (final v in values) ?fromString(v),
  ];

  String get displayName => switch (this) {
    eating => 'Eating',
    training => 'Training',
    drinks => 'Drinks',
    symptoms => 'Symptoms',
    mood => 'Mood',
  };
}

enum CheckinStatus {
  notStarted,
  partial,
  done;

  String get wireValue => switch (this) {
    notStarted => 'not_started',
    partial => 'partial',
    done => 'done',
  };

  String toJson() => wireValue;

  static CheckinStatus fromString(String? value) {
    final key = value?.trim().toLowerCase().replaceAll('_', '');
    for (final s in values) {
      if (s.wireValue.replaceAll('_', '') == key) return s;
    }
    return notStarted;
  }

  String get displayName => switch (this) {
    notStarted => 'Not started',
    partial => 'Partly done',
    done => 'Done',
  };
}

/// POST /assistant/checkin.
class CheckinTurn {
  final String conversationId;
  final DateTime? day;
  final CheckinStatus status;

  /// Asked now; null when done or left.
  final CheckinTopic? topic;
  final String? encouragement;
  final String? question;

  /// What to show: the encouragement then the question, or the closing line.
  final String reply;
  final List<JournalEntry> captured;

  /// 1 to 6 when this answer set the mood check-in.
  final int? mood;
  final String? userMessageId;
  final String assistantMessageId;
  final List<CheckinTopic> done;
  final List<CheckinTopic> skipped;
  final List<CheckinTopic> remaining;

  /// The answer named a red flag: [reply] is the get-care text and the
  /// check-in stops.
  final bool redFlag;

  const CheckinTurn({
    required this.conversationId,
    this.day,
    this.status = CheckinStatus.partial,
    this.topic,
    this.encouragement,
    this.question,
    this.reply = '',
    this.captured = const [],
    this.mood,
    this.userMessageId,
    this.assistantMessageId = '',
    this.done = const [],
    this.skipped = const [],
    this.remaining = const [],
    this.redFlag = false,
  });

  factory CheckinTurn.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final mood = r.raw('mood');
    return CheckinTurn(
      conversationId: r.string('conversationId'),
      day: r.dateTime('day'),
      status: CheckinStatus.fromString(r.stringOrNull('status')),
      topic: CheckinTopic.fromString(r.stringOrNull('topic')),
      encouragement: r.stringOrNull('encouragement'),
      question: r.stringOrNull('question'),
      reply: r.string('reply'),
      captured: [
        for (final e in r.objects('captured')) JournalEntry.fromJson(e),
      ],
      mood: mood == null ? null : r.integer('mood'),
      userMessageId: r.stringOrNull('userMessageId'),
      assistantMessageId: r.string('assistantMessageId'),
      done: CheckinTopic.listFrom(r.strings('done')),
      skipped: CheckinTopic.listFrom(r.strings('skipped')),
      remaining: CheckinTopic.listFrom(r.strings('remaining')),
      redFlag: r.boolean('redFlag'),
    );
  }
}

/// GET /assistant/checkin/today.
class CheckinToday {
  final DateTime? day;
  final CheckinStatus status;
  final String? conversationId;

  /// The topic waiting for an answer; null when not started, left or done.
  final CheckinTopic? topic;
  final String? question;

  /// Topics today's record has nothing for right now, in asking order.
  final List<CheckinTopic> missing;
  final List<CheckinTopic> done;
  final List<CheckinTopic> skipped;

  const CheckinToday({
    this.day,
    this.status = CheckinStatus.notStarted,
    this.conversationId,
    this.topic,
    this.question,
    this.missing = const [],
    this.done = const [],
    this.skipped = const [],
  });

  factory CheckinToday.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return CheckinToday(
      day: r.dateTime('day'),
      status: CheckinStatus.fromString(r.stringOrNull('status')),
      conversationId: r.stringOrNull('conversationId'),
      topic: CheckinTopic.fromString(r.stringOrNull('topic')),
      question: r.stringOrNull('question'),
      missing: CheckinTopic.listFrom(r.strings('missing')),
      done: CheckinTopic.listFrom(r.strings('done')),
      skipped: CheckinTopic.listFrom(r.strings('skipped')),
    );
  }
}

/// One line of the morning brief.
class BriefItem {
  /// `sleep`, `reading`, `targets`, `insight` or `evening`.
  final String kind;
  final String text;

  const BriefItem({required this.kind, required this.text});

  factory BriefItem.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return BriefItem(kind: r.string('kind'), text: r.string('text'));
  }
}

/// GET /assistant/brief.
class MorningBrief {
  final DateTime? day;
  final String text;

  /// True when the model's rewording of [text] passed the guard.
  final bool generated;
  final List<BriefItem> items;
  final DateTime? computedAt;

  const MorningBrief({
    this.day,
    this.text = '',
    this.generated = false,
    this.items = const [],
    this.computedAt,
  });

  /// With no items the server says nothing is new; the card stays away.
  bool get isEmpty => items.isEmpty;

  factory MorningBrief.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return MorningBrief(
      day: r.dateTime('day'),
      text: r.string('text'),
      generated: r.boolean('generated'),
      items: [for (final i in r.objects('items')) BriefItem.fromJson(i)],
      computedAt: r.dateTime('computedAt'),
    );
  }
}

/// One reviewed suggestion that applies today. Every word is the server's.
class PlaybookEntry {
  final String key;
  final String topic;
  final String title;
  final String text;
  final String evidenceGrade;
  final String source;

  /// Empty when no outside source is quoted.
  final String sourceUrl;
  final String why;
  final List<String> cautions;

  const PlaybookEntry({
    required this.key,
    this.topic = '',
    this.title = '',
    this.text = '',
    this.evidenceGrade = '',
    this.source = '',
    this.sourceUrl = '',
    this.why = '',
    this.cautions = const [],
  });

  /// Only a real https address is offered as a link.
  Uri? get sourceLink {
    final uri = Uri.tryParse(sourceUrl.trim());
    if (uri == null || uri.scheme != 'https' || uri.host.isEmpty) return null;
    return uri;
  }

  factory PlaybookEntry.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return PlaybookEntry(
      key: r.string('key'),
      topic: r.string('topic'),
      title: r.string('title'),
      text: r.string('text'),
      evidenceGrade: r.string('evidenceGrade'),
      source: r.string('source'),
      sourceUrl: r.string('sourceUrl'),
      why: r.string('why'),
      cautions: r.strings('cautions'),
    );
  }

  static List<PlaybookEntry> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) PlaybookEntry.fromJson(item),
  ];
}

/// GET and PUT /assistant/nudges. The address comes back masked and the
/// token never comes back.
class NudgeSettings {
  final bool enabled;
  final bool urlSet;
  final String? ntfyUrlMasked;
  final bool tokenSet;

  /// `HH:mm`, local to [timeZone].
  final String eveningTime;
  final String timeZone;
  final DateTime? lastSentOn;

  const NudgeSettings({
    this.enabled = false,
    this.urlSet = false,
    this.ntfyUrlMasked,
    this.tokenSet = false,
    this.eveningTime = '20:00',
    this.timeZone = '',
    this.lastSentOn,
  });

  factory NudgeSettings.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return NudgeSettings(
      enabled: r.boolean('enabled'),
      urlSet: r.boolean('urlSet'),
      ntfyUrlMasked: r.stringOrNull('ntfyUrlMasked'),
      tokenSet: r.boolean('tokenSet'),
      eveningTime: r.string('eveningTime', fallback: '20:00'),
      timeZone: r.string('timeZone'),
      lastSentOn: r.dateTime('lastSentOn'),
    );
  }
}

/// Body of PUT /assistant/nudges. For the address and the token, null keeps
/// what is stored and an empty string clears it.
class NudgeUpdate {
  final bool enabled;
  final String? ntfyUrl;
  final String? ntfyToken;
  final String? eveningTime;

  const NudgeUpdate({
    required this.enabled,
    this.ntfyUrl,
    this.ntfyToken,
    this.eveningTime,
  });

  Map<String, dynamic> toJson() => {
    'enabled': enabled,
    'ntfyUrl': ntfyUrl,
    'ntfyToken': ntfyToken,
    'eveningTime': eveningTime,
  };
}

/// POST /assistant/nudges/test.
class NudgeTestResult {
  final bool sent;
  final String? error;

  const NudgeTestResult({this.sent = false, this.error});

  factory NudgeTestResult.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return NudgeTestResult(
      sent: r.boolean('sent'),
      error: r.stringOrNull('error'),
    );
  }
}

/// One fixed conversation of the model check.
class ModelCheckCase {
  final String name;
  final String checks;
  final bool passed;
  final String detail;
  final int latencyMs;

  const ModelCheckCase({
    required this.name,
    this.checks = '',
    this.passed = false,
    this.detail = '',
    this.latencyMs = 0,
  });

  factory ModelCheckCase.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ModelCheckCase(
      name: r.string('name'),
      checks: r.string('checks'),
      passed: r.boolean('passed'),
      detail: r.string('detail'),
      latencyMs: r.integer('latencyMs'),
    );
  }
}

/// POST /assistant/model-check (admin).
class ModelCheck {
  final bool enabled;
  final String? model;
  final bool passed;
  final int passedCount;
  final int total;
  final List<ModelCheckCase> cases;
  final DateTime? checkedAt;

  const ModelCheck({
    this.enabled = false,
    this.model,
    this.passed = false,
    this.passedCount = 0,
    this.total = 0,
    this.cases = const [],
    this.checkedAt,
  });

  factory ModelCheck.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ModelCheck(
      enabled: r.boolean('enabled'),
      model: r.stringOrNull('model'),
      passed: r.boolean('passed'),
      passedCount: r.integer('passedCount'),
      total: r.integer('total'),
      cases: [for (final c in r.objects('cases')) ModelCheckCase.fromJson(c)],
      checkedAt: r.dateTime('checkedAt'),
    );
  }
}
