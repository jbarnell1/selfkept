import 'package:intl/intl.dart';

/// Sleep arrives as nightly minutes and reads as hours: "7 h 40", "8 h".
String formatSleepMinutes(num minutes) {
  final total = minutes.round();
  final h = total ~/ 60;
  final m = total % 60;
  if (m == 0) return '$h h';
  return '$h h ${m.toString().padLeft(2, '0')}';
}

/// The one place a metric value becomes text. Sleep is hours and minutes;
/// counts and kcal are whole numbers with thousands separators; everything
/// else gets the decimals its unit deserves.
String formatMetricValue(String metric, num value, {String unit = ''}) {
  if (metric == 'sleep_stage' || unit == 'min_asleep') {
    return formatSleepMinutes(value);
  }
  final decimals = _decimalsFor(metric, unit);
  if (decimals == 0) return NumberFormat.decimalPattern().format(value.round());
  return value.toStringAsFixed(decimals);
}

/// Value with its unit, for captions: "58 bpm", "72.4 kg", "7 h 40".
String formatMetricWithUnit(String metric, num value, {String unit = ''}) {
  final text = formatMetricValue(metric, value, unit: unit);
  if (metric == 'sleep_stage' || unit.isEmpty || unit == 'count') return text;
  if (unit == '%') return '$text%';
  return '$text $unit';
}

int _decimalsFor(String metric, String unit) => switch (unit) {
  'count' || 'kcal' || 'min' || 'bpm' || 'breaths/min' || 'ms' => 0,
  'score' || 'level' || 's' || 'stage' => 0,
  'C' => 2,
  'kg' || '%' || 'mL/kg/min' || 'cm' || 'm' || 'mmol/L' || 'mmHg' => 1,
  _ => metric == 'steps' ? 0 : 1,
};
