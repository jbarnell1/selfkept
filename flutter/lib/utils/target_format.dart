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

  static const String noneSoFar = 'None so far';

  static bool _isSleep(Target target) => target.metricKey == sleepMetric;

  /// "At most 0": a target to keep something out, which reads as "none".
  static bool isNone(Target target) =>
      target.comparator == TargetComparator.atMost && target.value == 0;

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
  /// nothing has been counted. A target of none reads "None so far" or
  /// "2 logged, aiming for none".
  static String standing(Target target, double? current, Units units) {
    if (isNone(target)) {
      if (current == null || current == 0) return noneSoFar;
      final u = unit(target, units);
      final amount = u.isEmpty || u == 'entries'
          ? number(target, current, units)
          : withUnit(target, current, units);
      return '$amount logged, aiming for none';
    }
    final goal = withUnit(target, target.value, units);
    if (current == null) return goal;
    return '${number(target, current, units)} / $goal';
  }

  /// The rule in words: "At least 120 g a day", or "None in a week" for a
  /// limit of zero.
  static String rule(Target target, Units units) => isNone(target)
      ? 'None in ${target.period.words}'
      : '${target.comparator.displayName} '
            '${withUnit(target, target.value, units)} ${target.period.words}';
}
