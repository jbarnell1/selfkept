import '../utils/json_reader.dart';
import 'date_only.dart';
import 'target.dart';

/// Which of the two places a goal holds.
enum GoalSlot {
  main,
  secondary;

  String get wireValue => name;
  String toJson() => wireValue;

  static GoalSlot fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final slot in values) {
      if (slot.name == lower) return slot;
    }
    return main;
  }

  String get displayName => switch (this) {
    main => 'Main',
    secondary => 'Secondary',
  };
}

/// What a goal is for. The labels match the server's.
enum GoalKind {
  loseFat('lose_fat', 'Lose fat'),
  gainMuscle('gain_muscle', 'Gain muscle'),
  recomposition('recomposition', 'Lose fat and gain muscle'),
  maintain('maintain', 'Hold steady'),
  keepStrength('keep_strength', 'Keep strength'),
  fitness('fitness', 'Get fitter'),
  sleepBetter('sleep_better', 'Sleep better'),
  improveLabs('improve_labs', 'Improve blood work'),
  custom('custom', 'Your goal');

  final String wireValue;
  final String displayName;

  const GoalKind(this.wireValue, this.displayName);

  String toJson() => wireValue;

  static GoalKind fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final kind in values) {
      if (kind.wireValue == lower) return kind;
    }
    return custom;
  }
}

enum GoalStatus {
  active,
  done,
  dropped;

  String get wireValue => name;
  String toJson() => wireValue;

  static GoalStatus fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final status in values) {
      if (status.name == lower) return status;
    }
    return active;
  }

  String get displayName => switch (this) {
    active => 'Active',
    done => 'Done',
    dropped => 'Dropped',
  };
}

/// How a goal is moving against its plan, as the server judged it.
enum PaceStatus {
  onTrack('on_track'),
  ahead('ahead'),
  behind('behind'),
  wrongWay('wrong_way'),
  reached('reached'),
  unknown('unknown');

  final String wireValue;

  const PaceStatus(this.wireValue);

  String toJson() => wireValue;

  static PaceStatus fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final status in values) {
      if (status.wireValue == lower) return status;
    }
    return unknown;
  }

  /// Plain words for the status. [unknown] says so rather than guessing.
  String get displayName => switch (this) {
    onTrack => 'On track',
    ahead => 'Ahead of plan',
    behind => 'Behind plan',
    wrongWay => 'Moving the wrong way',
    reached => 'Reached',
    unknown => 'Too early to say',
  };

  /// On track, ahead and reached read as good news; behind and the wrong
  /// way as worth a look. Unknown is neither.
  bool get isGood => this == onTrack || this == ahead || this == reached;
  bool get needsAttention => this == behind || this == wrongWay;
}

/// One goal. Values are stored metric (kg, cm).
class Goal {
  final String id;
  final GoalSlot slot;
  final GoalKind kind;
  final String title;
  final String? metricKey;
  final double? startValue;
  final double? targetValue;
  final double? pacePerWeek;
  final DateTime? startedOn;
  final DateTime? targetDate;
  final GoalStatus status;
  final DateTime? endedAt;

  const Goal({
    required this.id,
    required this.slot,
    required this.kind,
    required this.title,
    this.metricKey,
    this.startValue,
    this.targetValue,
    this.pacePerWeek,
    this.startedOn,
    this.targetDate,
    this.status = GoalStatus.active,
    this.endedAt,
  });

  bool get isActive => status == GoalStatus.active;

  factory Goal.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Goal(
      id: r.string('id'),
      slot: GoalSlot.fromString(r.stringOrNull('slot')),
      kind: GoalKind.fromString(r.stringOrNull('kind')),
      title: r.string('title'),
      metricKey: r.stringOrNull('metricKey'),
      startValue: readDouble(r.raw('startValue')),
      targetValue: readDouble(r.raw('targetValue')),
      pacePerWeek: readDouble(r.raw('pacePerWeek')),
      startedOn: r.dateTime('startedOn'),
      targetDate: r.dateTime('targetDate'),
      status: GoalStatus.fromString(r.stringOrNull('status')),
      endedAt: r.dateTime('endedAt'),
    );
  }

  static List<Goal> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) Goal.fromJson(item),
  ];
}

/// Body of POST /goals and PUT /goals/{id}.
class GoalDraft {
  final GoalSlot slot;
  final GoalKind kind;
  final String? title;
  final String? metricKey;
  final double? startValue;
  final double? targetValue;
  final double? pacePerWeek;
  final DateTime? startedOn;
  final DateTime? targetDate;

  const GoalDraft({
    required this.slot,
    required this.kind,
    this.title,
    this.metricKey,
    this.startValue,
    this.targetValue,
    this.pacePerWeek,
    this.startedOn,
    this.targetDate,
  });

  Map<String, dynamic> toJson() => {
    'slot': slot.wireValue,
    'kind': kind.wireValue,
    'title': title,
    'metricKey': metricKey,
    'startValue': startValue,
    'targetValue': targetValue,
    'pacePerWeek': pacePerWeek,
    'startedOn': startedOn == null ? null : dateOnly(startedOn!),
    'targetDate': targetDate == null ? null : dateOnly(targetDate!),
  };
}

/// A goal with where it stands. [summary] is the server's sentence, already
/// in the viewer's units.
class GoalProgress {
  final Goal goal;
  final String? unit;
  final double? current;

  /// 0 to 1 when the goal has a start and a target; null otherwise.
  final double? fraction;
  final double? actualPacePerWeek;
  final PaceStatus paceStatus;
  final DateTime? projectedDate;
  final String summary;

  const GoalProgress({
    required this.goal,
    this.unit,
    this.current,
    this.fraction,
    this.actualPacePerWeek,
    this.paceStatus = PaceStatus.unknown,
    this.projectedDate,
    this.summary = '',
  });

  factory GoalProgress.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return GoalProgress(
      goal: Goal.fromJson(r.object('goal') ?? const {}),
      unit: r.stringOrNull('unit'),
      current: readDouble(r.raw('current')),
      fraction: readDouble(r.raw('fraction')),
      actualPacePerWeek: readDouble(r.raw('actualPacePerWeek')),
      paceStatus: PaceStatus.fromString(r.stringOrNull('paceStatus')),
      projectedDate: r.dateTime('projectedDate'),
      summary: r.string('summary'),
    );
  }
}

/// A goal the server offers when none is set, with the targets that go
/// with it. [why] is the server's sentence.
class GoalSuggestion {
  final GoalKind kind;
  final String title;
  final String why;
  final String? metricKey;
  final double? startValue;
  final List<SuggestedTarget> targets;

  const GoalSuggestion({
    required this.kind,
    required this.title,
    required this.why,
    this.metricKey,
    this.startValue,
    this.targets = const [],
  });

  factory GoalSuggestion.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return GoalSuggestion(
      kind: GoalKind.fromString(r.stringOrNull('kind')),
      title: r.string('title'),
      why: r.string('why'),
      metricKey: r.stringOrNull('metricKey'),
      startValue: readDouble(r.raw('startValue')),
      targets: [
        for (final t in r.objects('targets')) SuggestedTarget.fromJson(t),
      ],
    );
  }
}
