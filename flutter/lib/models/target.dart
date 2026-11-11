import '../utils/json_reader.dart';
import 'goal.dart';

/// What a target counts.
enum TargetKind {
  metric,
  journal,
  strength;

  String get wireValue => name;
  String toJson() => wireValue;

  static TargetKind fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final kind in values) {
      if (kind.name == lower) return kind;
    }
    return metric;
  }

  String get displayName => switch (this) {
    metric => 'A reading',
    journal => 'A journal tag',
    strength => 'Lifting',
  };
}

enum TargetComparator {
  atLeast('at_least', 'At least'),
  atMost('at_most', 'At most');

  final String wireValue;
  final String displayName;

  const TargetComparator(this.wireValue, this.displayName);

  String toJson() => wireValue;

  static TargetComparator fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final c in values) {
      if (c.wireValue == lower) return c;
    }
    return atLeast;
  }

  /// Lower case, for the middle of a line: "at least 120 g".
  String get words => displayName.toLowerCase();
}

enum TargetPeriod {
  day,
  week;

  String get wireValue => name;
  String toJson() => wireValue;

  static TargetPeriod fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final p in values) {
      if (p.name == lower) return p;
    }
    return day;
  }

  String get displayName => switch (this) {
    day => 'Each day',
    week => 'Each week',
  };

  /// "a day" or "a week", for the end of a line.
  String get words => switch (this) {
    day => 'a day',
    week => 'a week',
  };
}

/// One target: a number to reach (or stay under) each day or week.
class Target {
  final String id;
  final String? goalId;
  final String label;
  final TargetKind kind;
  final String? metricKey;
  final String? tag;
  final TargetComparator comparator;
  final double value;
  final TargetPeriod period;
  final bool active;
  final int sortOrder;

  /// The server's unit: the metric's own, "entries", "days" or "lifts".
  final String unit;

  const Target({
    required this.id,
    this.goalId,
    required this.label,
    required this.kind,
    this.metricKey,
    this.tag,
    this.comparator = TargetComparator.atLeast,
    required this.value,
    this.period = TargetPeriod.day,
    this.active = true,
    this.sortOrder = 0,
    this.unit = '',
  });

  factory Target.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Target(
      id: r.string('id'),
      goalId: r.stringOrNull('goalId'),
      label: r.string('label'),
      kind: TargetKind.fromString(r.stringOrNull('kind')),
      metricKey: r.stringOrNull('metricKey'),
      tag: r.stringOrNull('tag'),
      comparator: TargetComparator.fromString(r.stringOrNull('comparator')),
      value: readDouble(r.raw('value')) ?? 0,
      period: TargetPeriod.fromString(r.stringOrNull('period')),
      active: r.boolean('active', fallback: true),
      sortOrder: r.integer('sortOrder'),
      unit: r.string('unit'),
    );
  }

  static List<Target> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) Target.fromJson(item),
  ];
}

/// Body of POST /targets and PUT /targets/{id}.
class TargetDraft {
  final String? goalId;
  final String label;
  final TargetKind kind;
  final String? metricKey;
  final String? tag;
  final TargetComparator comparator;
  final double value;
  final TargetPeriod period;
  final bool? active;
  final int? sortOrder;

  const TargetDraft({
    this.goalId,
    required this.label,
    required this.kind,
    this.metricKey,
    this.tag,
    required this.comparator,
    required this.value,
    required this.period,
    this.active,
    this.sortOrder,
  });

  /// The draft that saves [target] as it is, with any of the given changes.
  factory TargetDraft.fromTarget(Target target, {int? sortOrder}) =>
      TargetDraft(
        goalId: target.goalId,
        label: target.label,
        kind: target.kind,
        metricKey: target.metricKey,
        tag: target.tag,
        comparator: target.comparator,
        value: target.value,
        period: target.period,
        active: target.active,
        sortOrder: sortOrder ?? target.sortOrder,
      );

  Map<String, dynamic> toJson() => {
    'goalId': goalId,
    'label': label,
    'kind': kind.wireValue,
    'metricKey': metricKey,
    'tag': tag,
    'comparator': comparator.wireValue,
    'value': value,
    'period': period.wireValue,
    'active': ?active,
    'sortOrder': ?sortOrder,
  };
}

/// A target the server proposes alongside a suggested goal.
class SuggestedTarget {
  final String label;
  final TargetKind kind;
  final String? metricKey;
  final String? tag;
  final TargetComparator comparator;
  final double value;
  final TargetPeriod period;
  final String unit;

  const SuggestedTarget({
    required this.label,
    required this.kind,
    this.metricKey,
    this.tag,
    this.comparator = TargetComparator.atLeast,
    required this.value,
    this.period = TargetPeriod.day,
    this.unit = '',
  });

  factory SuggestedTarget.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SuggestedTarget(
      label: r.string('label'),
      kind: TargetKind.fromString(r.stringOrNull('kind')),
      metricKey: r.stringOrNull('metricKey'),
      tag: r.stringOrNull('tag'),
      comparator: TargetComparator.fromString(r.stringOrNull('comparator')),
      value: readDouble(r.raw('value')) ?? 0,
      period: TargetPeriod.fromString(r.stringOrNull('period')),
      unit: r.string('unit'),
    );
  }

  /// The same rule as an unsaved target, for formatting.
  Target asTarget() => Target(
    id: '',
    label: label,
    kind: kind,
    metricKey: metricKey,
    tag: tag,
    comparator: comparator,
    value: value,
    period: period,
    unit: unit,
  );

  TargetDraft toDraft({String? goalId}) => TargetDraft(
    goalId: goalId,
    label: label,
    kind: kind,
    metricKey: metricKey,
    tag: tag,
    comparator: comparator,
    value: value,
    period: period,
  );
}

/// A target against today and the six days before it.
class TargetTally {
  final Target target;
  final double? current;

  /// Null while the day or week is still open or nothing is known. Never
  /// shown as a miss.
  final bool? met;

  /// Seven days, oldest first: met, not met, or unknown.
  final List<bool?> week;
  final int metDays;
  final int knownDays;

  const TargetTally({
    required this.target,
    this.current,
    this.met,
    this.week = const [],
    this.metDays = 0,
    this.knownDays = 0,
  });

  factory TargetTally.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final rawWeek = r.raw('week');
    return TargetTally(
      target: Target.fromJson(r.object('target') ?? const {}),
      current: readDouble(r.raw('current')),
      met: r.raw('met') is bool ? r.raw('met') as bool : null,
      week: [
        if (rawWeek is List)
          for (final d in rawWeek) d is bool ? d : null,
      ],
      metDays: r.integer('metDays'),
      knownDays: r.integer('knownDays'),
    );
  }
}

/// GET /targets/tally: the active goals and every active target's tally.
class GoalTally {
  final DateTime? day;
  final List<Goal> goals;
  final List<TargetTally> targets;

  const GoalTally({this.day, this.goals = const [], this.targets = const []});

  bool get isEmpty => goals.isEmpty && targets.isEmpty;

  factory GoalTally.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return GoalTally(
      day: r.dateTime('day'),
      goals: [for (final g in r.objects('goals')) Goal.fromJson(g)],
      targets: [for (final t in r.objects('targets')) TargetTally.fromJson(t)],
    );
  }
}
