import 'package:intl/intl.dart';

import '../models/enums.dart';
import '../models/target.dart';
import 'body_units.dart';
import 'metric_format.dart';

/// How a target and its tally read as text. Values are stored metric; kg and
/// cm are shown in the viewer's units, sleep as hours and minutes.
abstract final class TargetFormat {
  static const String sleepMetric = 'sleep_stage';

  static final NumberFormat _number = NumberFormat('#,##0.#');

  static bool _isSleep(Target target) => target.metricKey == sleepMetric;

  /// A value of this target without its unit: "86", "10,000", "7 h 30".
  static String number(Target target, double value, Units units) {
    if (_isSleep(target)) return formatSleepMinutes(value);
    return _number.format(BodyUnits.fromStored(value, target.unit, units));
  }

  /// The unit as shown, or empty when the number says it all.
  static String unit(Target target, Units units) {
    if (_isSleep(target)) return '';
    final unit = BodyUnits.displayUnit(target.unit, units);
    return unit == 'count' ? '' : unit;
  }

  /// A value with its unit: "120 g", "2 entries", "7 h 30".
  static String withUnit(Target target, double value, Units units) {
    final text = number(target, value, units);
    final u = unit(target, units);
    if (u.isEmpty) return text;
    if (u == '%') return '$text%';
    return '$text $u';
  }

  /// Where the target stands: "86 / 120 g", or the target alone when
  /// nothing has been counted.
  static String standing(Target target, double? current, Units units) {
    final goal = withUnit(target, target.value, units);
    if (current == null) return goal;
    return '${number(target, current, units)} / $goal';
  }

  /// The rule in words: "At least 120 g a day".
  static String rule(Target target, Units units) =>
      '${target.comparator.displayName} '
      '${withUnit(target, target.value, units)} ${target.period.words}';
}
