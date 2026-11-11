import '../utils/api_error.dart';
import '../utils/json_reader.dart';
import 'assistant.dart';

// The cloud lookup (docs/lookup-design.md): one question a person chose to
// send to a cloud model, after reading the exact text. Everything here is the
// caller's own; there is no personId on any route.

/// GET and PUT /assistant/lookup.
class LookupStatus {
  final bool available;
  final bool enabled;

  /// The provider's name as the server gives it. Never typed in the app.
  final String provider;
  final String model;

  /// True when the home model will explain the answer afterwards.
  final bool localModel;

  const LookupStatus({
    this.available = false,
    this.enabled = false,
    this.provider = '',
    this.model = '',
    this.localModel = false,
  });

  factory LookupStatus.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LookupStatus(
      available: r.boolean('available'),
      enabled: r.boolean('enabled'),
      provider: r.string('provider'),
      model: r.string('model'),
      localModel: r.boolean('localModel'),
    );
  }
}

/// How the outgoing text is built.
enum LookupMode {
  /// The question only, with dates removed and numbers rounded.
  anonymized,

  /// The question as typed plus an "About me" block from the record.
  detailed;

  String get wireValue => name;
  String toJson() => wireValue;

  static LookupMode fromString(String? value) =>
      value?.trim().toLowerCase() == 'detailed' ? detailed : anonymized;

  String get displayName => switch (this) {
    anonymized => 'Anonymized',
    detailed => 'With my details',
  };
}

/// What the scrubber removed, changed or added.
enum LookupChangeKind {
  name,
  email,
  phone,
  link,
  date,
  age,
  number,
  record,
  other;

  String get wireValue => this.name;
  String toJson() => wireValue;

  static LookupChangeKind fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final k in values) {
      if (k.name == lower) return k;
    }
    return other;
  }

  String get displayName => switch (this) {
    name => 'Name',
    email => 'Email address',
    phone => 'Phone number',
    link => 'Web address',
    date => 'Date',
    age => 'Age',
    number => 'Number',
    record => 'Details from your record',
    other => 'Other',
  };
}

/// One line of `changes` (or of `findings` on a refused send).
class LookupChange {
  final LookupChangeKind kind;
  final int count;

  const LookupChange({required this.kind, this.count = 1});

  /// The change in everyday words: "your name", "2 dates", "3 numbers
  /// rounded", "details from your record added".
  String get label {
    final n = count < 1 ? 1 : count;
    final one = n == 1;
    return switch (kind) {
      LookupChangeKind.name => one ? 'your name' : '$n names',
      LookupChangeKind.email => one ? 'an email address' : '$n email addresses',
      LookupChangeKind.phone => one ? 'a phone number' : '$n phone numbers',
      LookupChangeKind.link => one ? 'a web address' : '$n web addresses',
      LookupChangeKind.date => one ? 'a date' : '$n dates',
      LookupChangeKind.age => 'your age, as a decade',
      LookupChangeKind.number =>
        one ? 'a number rounded' : '$n numbers rounded',
      LookupChangeKind.record => 'details from your record added',
      LookupChangeKind.other => one ? 'one other change' : '$n other changes',
    };
  }

  /// What a refused send still holds: "a name", "2 phone numbers".
  String get foundLabel {
    final n = count < 1 ? 1 : count;
    final one = n == 1;
    return switch (kind) {
      LookupChangeKind.name => one ? 'a name' : '$n names',
      LookupChangeKind.email => one ? 'an email address' : '$n email addresses',
      LookupChangeKind.phone => one ? 'a phone number' : '$n phone numbers',
      LookupChangeKind.link => one ? 'a web address' : '$n web addresses',
      LookupChangeKind.date => one ? 'a date' : '$n dates',
      LookupChangeKind.age => one ? 'an age' : '$n ages',
      LookupChangeKind.number => one ? 'a number' : '$n numbers',
      LookupChangeKind.record => 'details from your record',
      LookupChangeKind.other =>
        one ? 'something that identifies you' : '$n things that identify you',
    };
  }

  factory LookupChange.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LookupChange(
      kind: LookupChangeKind.fromString(r.stringOrNull('kind')),
      count: r.integer('count', fallback: 1),
    );
  }

  static List<LookupChange> listFrom(List<Map<String, dynamic>> items) => [
    for (final c in items) LookupChange.fromJson(c),
  ];
}

/// POST /assistant/lookups/draft. Nothing is sent and nothing is stored.
class LookupDraft {
  final String text;
  final LookupMode mode;
  final List<LookupChange> changes;

  /// True when the home model reworded the anonymized draft.
  final bool reworded;
  final String provider;
  final String model;

  const LookupDraft({
    required this.text,
    this.mode = LookupMode.anonymized,
    this.changes = const [],
    this.reworded = false,
    this.provider = '',
    this.model = '',
  });

  factory LookupDraft.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LookupDraft(
      text: r.string('text'),
      mode: LookupMode.fromString(r.stringOrNull('mode')),
      changes: LookupChange.listFrom(r.objects('changes')),
      reworded: r.boolean('reworded'),
      provider: r.string('provider'),
      model: r.string('model'),
    );
  }
}

/// Where a sent lookup stands.
enum LookupState {
  sent,
  answered,
  failed;

  String get wireValue => name;
  String toJson() => wireValue;

  static LookupState fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final s in values) {
      if (s.name == lower) return s;
    }
    return sent;
  }

  String get displayName => switch (this) {
    sent => 'Sent',
    answered => 'Answered',
    failed => 'Not answered',
  };
}

/// One row of GET /assistant/lookups: the exact text that went.
class LookupRecord {
  final String id;
  final String? conversationId;
  final LookupMode mode;
  final LookupState status;
  final String provider;
  final String model;
  final String text;
  final DateTime? sentAt;

  const LookupRecord({
    required this.id,
    this.conversationId,
    this.mode = LookupMode.anonymized,
    this.status = LookupState.sent,
    this.provider = '',
    this.model = '',
    this.text = '',
    this.sentAt,
  });

  factory LookupRecord.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LookupRecord(
      id: r.string('id'),
      conversationId: r.stringOrNull('conversationId'),
      mode: LookupMode.fromString(r.stringOrNull('mode')),
      status: LookupState.fromString(r.stringOrNull('status')),
      provider: r.string('provider'),
      model: r.string('model'),
      text: r.string('text'),
      sentAt: r.dateTime('sentAt'),
    );
  }

  static List<LookupRecord> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) LookupRecord.fromJson(item),
  ];
}

/// Why a lookup came back without an answer.
enum LookupFailure {
  refused,
  cutOff,
  searchFailed,
  unreachable,
  rateLimited,
  keyRejected,
  unknown;

  String get wireValue => switch (this) {
    refused => 'refused',
    cutOff => 'cut_off',
    searchFailed => 'search_failed',
    unreachable => 'unreachable',
    rateLimited => 'rate_limited',
    keyRejected => 'key_rejected',
    unknown => 'unknown',
  };

  String toJson() => wireValue;

  static LookupFailure fromString(String? value) {
    final key = value?.trim().toLowerCase().replaceAll('_', '');
    for (final f in values) {
      if (f.wireValue.replaceAll('_', '') == key) return f;
    }
    return unknown;
  }

  String get displayName => switch (this) {
    refused => 'Declined',
    cutOff => 'Cut off',
    searchFailed => 'Search failed',
    unreachable => 'Not reachable',
    rateLimited => 'Too many requests',
    keyRejected => 'Key not accepted',
    unknown => 'Not answered',
  };
}

/// One event of a sent lookup.
sealed class LookupStreamEvent {
  const LookupStreamEvent();
}

/// The request has gone to the provider.
class LookupSentEvent extends LookupStreamEvent {
  final String lookupId;

  const LookupSentEvent(this.lookupId);

  factory LookupSentEvent.fromJson(Map<String, dynamic> json) =>
      LookupSentEvent(JsonReader(json).string('lookupId'));
}

/// The provider is still working; sent every few seconds.
class LookupWaitingEvent extends LookupStreamEvent {
  const LookupWaitingEvent();
}

/// The answer is stored. [applied] is the home model's note on what it means
/// for this person, when there is a home model.
class LookupDoneEvent extends LookupStreamEvent {
  final String lookupId;
  final AssistantMessage message;
  final AssistantMessage? applied;

  const LookupDoneEvent({
    required this.lookupId,
    required this.message,
    this.applied,
  });

  factory LookupDoneEvent.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final applied = r.object('applied');
    return LookupDoneEvent(
      lookupId: r.string('lookupId'),
      message: AssistantMessage.fromJson(r.object('message') ?? const {}),
      applied: applied == null ? null : AssistantMessage.fromJson(applied),
    );
  }
}

/// Nothing was stored as an answer. [text] is the server's plain message.
class LookupFailedEvent extends LookupStreamEvent {
  final String lookupId;
  final LookupFailure reason;
  final String text;

  const LookupFailedEvent({
    required this.lookupId,
    this.reason = LookupFailure.unknown,
    this.text = '',
  });

  factory LookupFailedEvent.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LookupFailedEvent(
      lookupId: r.string('lookupId'),
      reason: LookupFailure.fromString(r.stringOrNull('reason')),
      text: r.string('text'),
    );
  }
}

/// Why a lookup route said no, in the terms a screen needs.
enum LookupRefusalKind {
  /// 404: the server switch is off or there is no provider key.
  serverOff,

  /// 409: the person's own switch is off.
  personOff,

  /// 422 on the switch or a send: the acknowledgement was missing.
  notAcknowledged,

  /// 422 on a draft: the message is a red flag and is never looked up.
  redFlag,

  /// 422 on a send: the text still holds a name or contact detail.
  findings,

  /// 404 on a delete: the lookup is already gone.
  missing,
}

/// A refusal from a lookup route. Other failures (offline, a server error)
/// stay as the [ApiException] they were.
class LookupRefused implements Exception {
  final LookupRefusalKind kind;

  /// The server's own words, for a log or a fallback line.
  final String message;

  /// What the text still holds, for [LookupRefusalKind.findings].
  final List<LookupChange> findings;

  const LookupRefused(this.kind, {this.message = '', this.findings = const []});

  /// The refusal for [error] on [route], or null when [error] is not one.
  static LookupRefused? from(Object error, LookupRoute route) {
    if (error is! ApiException || error.isNetwork) return null;
    final body = error.body;
    final findings = body == null
        ? const <LookupChange>[]
        : LookupChange.listFrom(JsonReader(body).objects('findings'));
    final kind = switch (error.statusCode) {
      404 when route == LookupRoute.delete => LookupRefusalKind.missing,
      404 => LookupRefusalKind.serverOff,
      409 => LookupRefusalKind.personOff,
      422 when findings.isNotEmpty => LookupRefusalKind.findings,
      422 when route == LookupRoute.draft => LookupRefusalKind.redFlag,
      422 => LookupRefusalKind.notAcknowledged,
      _ => null,
    };
    if (kind == null) return null;
    return LookupRefused(kind, message: error.message, findings: findings);
  }

  @override
  String toString() => 'LookupRefused(${kind.name}): $message';
}

/// The lookup routes, so one status code can mean the right thing.
enum LookupRoute { status, settings, draft, send, history, delete }
