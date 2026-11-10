import '../utils/json_reader.dart';
import 'date_only.dart';

/// The lifts offered by name. Anything else is a custom lift.
enum CommonLift {
  squat('squat', 'Squat'),
  deadlift('deadlift', 'Deadlift'),
  benchPress('bench_press', 'Bench press'),
  overheadPress('overhead_press', 'Overhead press'),
  row('row', 'Row'),
  pullUp('pull_up', 'Pull-up');

  final String wireValue;
  final String displayName;

  const CommonLift(this.wireValue, this.displayName);

  String toJson() => wireValue;

  /// Null when the lift is one of the person's own.
  static CommonLift? fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final lift in values) {
      if (lift.wireValue == lower) return lift;
    }
    return null;
  }
}

/// The top set of one lift on one day. Weight is stored in kg.
class StrengthSet {
  final String id;
  final DateTime? day;
  final String lift;
  final String liftName;
  final double weightKg;
  final int reps;

  /// Rate of perceived effort, 1 to 10; halves are allowed.
  final double? rpe;

  /// Null above 12 reps, where the estimate stops being useful.
  final double? estimatedMaxKg;

  const StrengthSet({
    required this.id,
    this.day,
    required this.lift,
    required this.liftName,
    required this.weightKg,
    required this.reps,
    this.rpe,
    this.estimatedMaxKg,
  });

  factory StrengthSet.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return StrengthSet(
      id: r.string('id'),
      day: r.dateTime('day'),
      lift: r.string('lift'),
      liftName: r.string('liftName', fallback: r.string('lift')),
      weightKg: readDouble(r.raw('weightKg')) ?? 0,
      reps: r.integer('reps'),
      rpe: readDouble(r.raw('rpe')),
      estimatedMaxKg: readDouble(r.raw('estimatedMaxKg')),
    );
  }
}

/// One lift with its sets, oldest first.
class StrengthLift {
  final String lift;
  final String liftName;
  final List<StrengthSet> sets;
  final double? bestEstimatedMaxKg;
  final double? changePct;

  const StrengthLift({
    required this.lift,
    required this.liftName,
    this.sets = const [],
    this.bestEstimatedMaxKg,
    this.changePct,
  });

  StrengthSet? get latest => sets.isEmpty ? null : sets.last;

  factory StrengthLift.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return StrengthLift(
      lift: r.string('lift'),
      liftName: r.string('liftName', fallback: r.string('lift')),
      sets: [for (final s in r.objects('sets')) StrengthSet.fromJson(s)],
      bestEstimatedMaxKg: readDouble(r.raw('bestEstimatedMaxKg')),
      changePct: readDouble(r.raw('changePct')),
    );
  }

  static List<StrengthLift> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) StrengthLift.fromJson(item),
  ];
}

/// Body of POST /strength.
class StrengthSetDraft {
  final String lift;
  final double weightKg;
  final int reps;
  final double? rpe;
  final DateTime? day;

  const StrengthSetDraft({
    required this.lift,
    required this.weightKg,
    required this.reps,
    this.rpe,
    this.day,
  });

  Map<String, dynamic> toJson() => {
    'lift': lift,
    'weightKg': weightKg,
    'reps': reps,
    'rpe': rpe,
    'day': day == null ? null : dateOnly(day!),
  };
}
