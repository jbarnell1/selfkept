import 'package:intl/intl.dart' show DateFormat;

import '../models/enums.dart';
import '../models/goal.dart';
import 'body_units.dart';
import 'metric_format.dart';

/// A metric a goal can be measured by, with its stored unit.
class GoalMetric {
  final String key;
  final String displayName;
  final String unit;

  const GoalMetric(this.key, this.displayName, this.unit);
}

/// How goals and their numbers read. Values are stored metric; kg and cm are
/// shown in the viewer's units and sleep as hours and minutes.
abstract final class GoalFormat {
  static const List<GoalMetric> metrics = [
    GoalMetric('weight_kg', 'Weight', 'kg'),
    GoalMetric('body_fat_pct', 'Body fat', '%'),
    GoalMetric('waist_cm', 'Waist', 'cm'),
    GoalMetric('lean_mass_kg', 'Lean mass', 'kg'),
    GoalMetric('vo2max', 'VO2 max', 'mL/kg/min'),
    GoalMetric('sleep_stage', 'Time asleep', 'min'),
  ];

  /// The metric a goal of [kind] is measured by when it names none, as the
  /// server decides it. Null for the kinds measured by targets alone.
  static String? defaultMetricKey(GoalKind kind) => switch (kind) {
    GoalKind.loseFat || GoalKind.gainMuscle || GoalKind.maintain => 'weight_kg',
    GoalKind.recomposition => 'body_fat_pct',
    GoalKind.fitness => 'vo2max',
    GoalKind.sleepBetter => 'sleep_stage',
    GoalKind.keepStrength || GoalKind.improveLabs || GoalKind.custom => null,
  };

  static GoalMetric? metricFor(String? key) {
    if (key == null) return null;
    for (final m in metrics) {
      if (m.key == key) return m;
    }
    return null;
  }

  /// The metric a goal is measured by: its own, or the usual one.
  static GoalMetric? metricOf(Goal goal) =>
      metricFor(goal.metricKey ?? defaultMetricKey(goal.kind));

  /// A value with its unit: "82.4 kg", "181.7 lb", "24.0%", "7 h 30".
  static String value(double value, String unit, Units units) {
    if (unit == 'min') return formatSleepMinutes(value);
    return BodyUnits.formatStored(value, unit, units);
  }

  /// A weekly pace in words: "0.4 kg down a week", "Level".
  static String pace(double perWeek, String unit, Units units) {
    final shown = unit == 'min'
        ? perWeek
        : BodyUnits.fromStored(perWeek, unit, units);
    if (shown.abs() < 0.05) return 'Level';
    final amount = unit == 'min'
        ? _minutes(perWeek.abs())
        : BodyUnits.formatStored(perWeek.abs(), unit, units);
    return '$amount ${perWeek < 0 ? 'down' : 'up'} a week';
  }

  /// "15 min" under an hour, "7 h 30" from an hour up.
  static String _minutes(double minutes) =>
      minutes < 59.5 ? '${minutes.round()} min' : formatSleepMinutes(minutes);

  /// What the field shows for a stored value: kg as lb for imperial, sleep
  /// minutes as hours.
  static double toField(double stored, String unit, Units units) =>
      unit == 'min' ? stored / 60 : BodyUnits.fromStored(stored, unit, units);

  /// What is stored for a typed value.
  static double fromField(double typed, String unit, Units units) =>
      switch (unit) {
        'min' => typed * 60,
        'kg' => BodyUnits.massToKg(typed, units),
        'cm' => BodyUnits.lengthToCm(typed, units),
        _ => typed,
      };

  /// The unit beside a field: "kg", "lb", "%", "hours".
  static String fieldUnit(String unit, Units units) =>
      unit == 'min' ? 'hours' : BodyUnits.displayUnit(unit, units);

  /// "Main goal" or "Second goal".
  static String slotName(GoalSlot slot) => switch (slot) {
    GoalSlot.main => 'Main goal',
    GoalSlot.secondary => 'Second goal',
  };

  /// "Around March 2027".
  static String projected(DateTime date) =>
      'Around ${DateFormat.yMMMM().format(date)}';

  /// "Since 1 Sep 2026".
  static String since(DateTime date) =>
      'Since ${DateFormat.yMMMd().format(date)}';
}
