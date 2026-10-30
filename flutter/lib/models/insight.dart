import '../utils/json_reader.dart';

/// How much attention a card asks for. Never red: notice is sage, watch is
/// amber, clinician is terracotta.
enum InsightSeverity {
  notice,
  watch,
  clinician;

  String get wireValue => name;
  String toJson() => wireValue;

  static InsightSeverity fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : notice;

  static InsightSeverity fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final s in values) {
      if (s.name == lower) return s;
    }
    return notice;
  }

  String get displayName => switch (this) {
    notice => 'Notice',
    watch => 'Watch',
    clinician => 'Clinician',
  };
}

/// What the person said about a card. Feeds ranking on the server.
enum InsightReaction {
  helpful,
  knew,
  wrong;

  String get wireValue => name;
  String toJson() => wireValue;

  static InsightReaction fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : helpful;

  /// Null when absent or unknown: no reaction is the common case.
  static InsightReaction? fromString(String? value) {
    if (value == null) return null;
    final lower = value.trim().toLowerCase();
    for (final r in values) {
      if (r.name == lower) return r;
    }
    return null;
  }

  String get displayName => switch (this) {
    helpful => 'Helpful',
    knew => 'Knew that',
    wrong => 'Not right',
  };
}

/// One insight card: a rule's finding for one person on one day.
class Insight {
  final String id;
  final DateTime day;
  final String kind;
  final InsightSeverity severity;
  final List<String> metrics;

  /// Display names for [metrics], same order, when the server sent them.
  final List<String> metricNames;
  final Map<String, dynamic> evidence;
  final String text;

  /// True when a local model phrased [text]; false for the templated sentence.
  final bool generated;
  final DateTime? shownAt;
  final DateTime? dismissedAt;
  final InsightReaction? reaction;

  const Insight({
    required this.id,
    required this.day,
    required this.kind,
    this.severity = InsightSeverity.notice,
    this.metrics = const [],
    this.metricNames = const [],
    this.evidence = const {},
    required this.text,
    this.generated = false,
    this.shownAt,
    this.dismissedAt,
    this.reaction,
  });

  bool get isDismissed => dismissedAt != null;

  /// What a chip shows for the metric at [index]: the server's name when it sent one.
  String? nameAt(int index) =>
      index < metricNames.length && metricNames[index].isNotEmpty
      ? metricNames[index]
      : null;

  factory Insight.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Insight(
      id: r.string('id'),
      day: r.dateTime('day') ?? DateTime.fromMillisecondsSinceEpoch(0),
      kind: r.string('kind'),
      severity: InsightSeverity.fromString(r.stringOrNull('severity')),
      metrics: r.strings('metrics'),
      metricNames: r.strings('metricNames'),
      evidence: r.object('evidence') ?? const {},
      text: r.string('text'),
      generated: r.boolean('generated'),
      shownAt: r.dateTime('shownAt'),
      dismissedAt: r.dateTime('dismissedAt'),
      reaction: InsightReaction.fromString(r.stringOrNull('reaction')),
    );
  }

  Insight copyWith({
    InsightReaction? reaction,
    bool clearReaction = false,
    DateTime? dismissedAt,
    bool clearDismissed = false,
  }) => Insight(
    id: id,
    day: day,
    kind: kind,
    severity: severity,
    metrics: metrics,
    metricNames: metricNames,
    evidence: evidence,
    text: text,
    generated: generated,
    shownAt: shownAt,
    dismissedAt: clearDismissed ? null : (dismissedAt ?? this.dismissedAt),
    reaction: clearReaction ? null : (reaction ?? this.reaction),
  );

  /// Body of POST /insights/{id}/reaction.
  static Map<String, dynamic> toReactionJson(InsightReaction reaction) => {
    'reaction': reaction.toJson(),
  };
}
