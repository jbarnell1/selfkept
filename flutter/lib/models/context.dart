import '../utils/json_reader.dart';
import 'date_only.dart';

/// What kind of thing is going on.
enum ContextKind {
  illness('illness'),
  travel('travel'),
  hardTraining('hard_training'),
  medicationChange('medication_change'),
  cyclePhase('cycle_phase'),
  newGoal('new_goal'),
  scaleChange('scale_change'),
  other('other');

  final String wireValue;

  const ContextKind(this.wireValue);

  String toJson() => wireValue;

  static ContextKind fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : other;

  static ContextKind fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final k in values) {
      if (k.wireValue == lower) return k;
    }
    return other;
  }

  String get displayName => switch (this) {
    illness => 'Illness',
    travel => 'Travel',
    hardTraining => 'Hard training',
    medicationChange => 'Medication change',
    cyclePhase => 'Cycle phase',
    newGoal => 'New goal',
    scaleChange => 'Scale change',
    other => 'Other',
  };
}

/// Who put a context item there.
enum ContextSource {
  user,
  rule,
  derived;

  String get wireValue => name;
  String toJson() => wireValue;

  static ContextSource fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : user;

  static ContextSource fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final s in values) {
      if (s.name == lower) return s;
    }
    return user;
  }

  String get displayName => switch (this) {
    user => 'Added by you',
    rule => 'Added by a rule',
    derived => 'From your record',
  };
}

/// One stored context event.
class ContextEvent {
  final String id;
  final ContextKind kind;
  final String label;
  final DateTime startedOn;
  final DateTime? endedOn;
  final ContextSource source;

  /// Only the person themself gets it back.
  final String? note;
  final bool active;

  const ContextEvent({
    required this.id,
    this.kind = ContextKind.other,
    this.label = '',
    required this.startedOn,
    this.endedOn,
    this.source = ContextSource.user,
    this.note,
    this.active = false,
  });

  factory ContextEvent.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final kind = ContextKind.fromString(r.stringOrNull('kind'));
    return ContextEvent(
      id: r.string('id'),
      kind: kind,
      label: r.string('label', fallback: kind.displayName),
      startedOn:
          r.dateTime('startedOn') ?? DateTime.fromMillisecondsSinceEpoch(0),
      endedOn: r.dateTime('endedOn'),
      source: ContextSource.fromString(r.stringOrNull('source')),
      note: r.stringOrNull('note'),
      active: r.boolean('active'),
    );
  }

  /// The same event as a draft, to put it back after a removal.
  ContextDraft toDraft() => ContextDraft(
    kind: kind,
    startedOn: startedOn,
    endedOn: endedOn,
    note: note ?? '',
  );
}

/// Something counting today: a stored event, or one the server worked out
/// from the record (a tagged symptom, a medicine started).
class ActiveContext {
  final ContextKind kind;
  final String label;
  final DateTime since;
  final DateTime? until;
  final ContextSource source;
  final String reason;
  final String? eventId;
  final String? detail;

  const ActiveContext({
    this.kind = ContextKind.other,
    this.label = '',
    required this.since,
    this.until,
    this.source = ContextSource.user,
    this.reason = '',
    this.eventId,
    this.detail,
  });

  factory ActiveContext.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final kind = ContextKind.fromString(r.stringOrNull('kind'));
    return ActiveContext(
      kind: kind,
      label: r.string('label', fallback: kind.displayName),
      since: r.dateTime('since') ?? DateTime.fromMillisecondsSinceEpoch(0),
      until: r.dateTime('until'),
      source: ContextSource.fromString(r.stringOrNull('source')),
      reason: r.string('reason'),
      eventId: r.stringOrNull('eventId'),
      detail: r.stringOrNull('detail'),
    );
  }
}

/// Answer of GET /context.
class ContextList {
  /// Today in the person's time zone.
  final DateTime day;
  final DateTime? from;
  final DateTime? to;
  final List<ActiveContext> active;
  final List<ContextEvent> events;

  const ContextList({
    required this.day,
    this.from,
    this.to,
    this.active = const [],
    this.events = const [],
  });

  factory ContextList.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ContextList(
      day: r.dateTime('day') ?? localToday(),
      from: r.dateTime('from'),
      to: r.dateTime('to'),
      active: [for (final a in r.objects('active')) ActiveContext.fromJson(a)],
      events: [for (final e in r.objects('events')) ContextEvent.fromJson(e)],
    );
  }

  ContextEvent? eventById(String? id) {
    if (id == null) return null;
    for (final e in events) {
      if (e.id == id) return e;
    }
    return null;
  }
}

/// What the context editor sends.
class ContextDraft {
  final ContextKind kind;
  final DateTime? startedOn;
  final DateTime? endedOn;
  final String note;

  const ContextDraft({
    required this.kind,
    this.startedOn,
    this.endedOn,
    this.note = '',
  });

  Map<String, dynamic> toCreateJson() => {
    'kind': kind.wireValue,
    if (startedOn != null) 'startedOn': dateOnly(startedOn!),
    if (endedOn != null) 'endedOn': dateOnly(endedOn!),
    if (note.trim().isNotEmpty) 'note': note.trim(),
  };

  /// Every field, so the event ends up exactly as drafted. An open end is
  /// sent as reopen.
  Map<String, dynamic> toUpdateJson() => {
    'kind': kind.wireValue,
    if (startedOn != null) 'startedOn': dateOnly(startedOn!),
    if (endedOn != null) 'endedOn': dateOnly(endedOn!) else 'reopen': true,
    'note': note.trim(),
  };
}

/// One tag against a reading: nights after the tag against the rest.
class PersonalPattern {
  final String tag;
  final String tagLabel;
  final String metric;
  final String metricName;
  final String unit;
  final int nightsWith;
  final int nightsWithout;

  /// The server's own sentence. The page shows it as sent.
  final String sentence;

  const PersonalPattern({
    required this.tag,
    this.tagLabel = '',
    this.metric = '',
    this.metricName = '',
    this.unit = '',
    this.nightsWith = 0,
    this.nightsWithout = 0,
    this.sentence = '',
  });

  factory PersonalPattern.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return PersonalPattern(
      tag: r.string('tag'),
      tagLabel: r.string('tagLabel'),
      metric: r.string('metric'),
      metricName: r.string('metricName'),
      unit: r.string('unit'),
      nightsWith: r.integer('nightsWith'),
      nightsWithout: r.integer('nightsWithout'),
      sentence: r.string('sentence'),
    );
  }
}

/// A tag seen, but with too few nights yet.
class PatternBuilding {
  final String tag;
  final String tagLabel;
  final int nightsWith;
  final int nightsWithout;

  const PatternBuilding({
    required this.tag,
    this.tagLabel = '',
    this.nightsWith = 0,
    this.nightsWithout = 0,
  });

  factory PatternBuilding.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return PatternBuilding(
      tag: r.string('tag'),
      tagLabel: r.string('tagLabel'),
      nightsWith: r.integer('nightsWith'),
      nightsWithout: r.integer('nightsWithout'),
    );
  }
}

/// Answer of GET /patterns.
class Patterns {
  final DateTime? day;
  final DateTime? from;
  final int windowDays;
  final int minNightsWith;
  final int minNightsWithout;
  final String note;
  final List<PersonalPattern> patterns;
  final List<PatternBuilding> building;

  const Patterns({
    this.day,
    this.from,
    this.windowDays = 0,
    this.minNightsWith = 0,
    this.minNightsWithout = 0,
    this.note = '',
    this.patterns = const [],
    this.building = const [],
  });

  bool get isEmpty => patterns.isEmpty && building.isEmpty;

  factory Patterns.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Patterns(
      day: r.dateTime('day'),
      from: r.dateTime('from'),
      windowDays: r.integer('windowDays'),
      minNightsWith: r.integer('minNightsWith'),
      minNightsWithout: r.integer('minNightsWithout'),
      note: r.string('note'),
      patterns: [
        for (final p in r.objects('patterns')) PersonalPattern.fromJson(p),
      ],
      building: [
        for (final b in r.objects('building')) PatternBuilding.fromJson(b),
      ],
    );
  }
}
