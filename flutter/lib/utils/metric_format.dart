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

/// Value with its unit, for captions: "58 bpm", "72.4 kg", "7 h 40",
/// "0.18 °C".
String formatMetricWithUnit(String metric, num value, {String unit = ''}) {
  final text = formatMetricValue(metric, value, unit: unit);
  if (metric == 'sleep_stage' || unit.isEmpty || unit == 'count') return text;
  if (unit == '%') return '$text%';
  if (unit == 'C') return '$text °C';
  return '$text $unit';
}

/// A name as it reads inside a sentence: "Resting heart rate" becomes
/// "resting heart rate", while a name that opens with an acronym or a
/// figure ("HRV (RMSSD)", "VO2 max") keeps its capitals.
String nameInSentence(String name) {
  if (name.isEmpty) return name;
  final space = name.indexOf(' ');
  final first = space < 0 ? name : name.substring(0, space);
  if (RegExp(r'[A-Z0-9]').hasMatch(first.substring(1))) return name;
  return name[0].toLowerCase() + name.substring(1);
}

int _decimalsFor(String metric, String unit) => switch (unit) {
  'count' || 'kcal' || 'min' || 'bpm' || 'breaths/min' || 'ms' => 0,
  'score' || 'level' || 's' || 'stage' => 0,
  'C' => 2,
  'kg' || '%' || 'mL/kg/min' || 'cm' || 'm' || 'mmol/L' || 'mmHg' => 1,
  _ => metric == 'steps' ? 0 : 1,
};
