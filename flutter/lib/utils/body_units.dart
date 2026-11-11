import '../models/enums.dart';

/// Body values are stored metric (kg, cm). This is the one place they turn
/// into the viewer's units for display, and back again for input. The
/// factors are the server's, so both sides round the same way.
abstract final class BodyUnits {
  static const double lbPerKg = 2.2046226218;
  static const double cmPerInch = 2.54;
  static const int inchesPerFoot = 12;

  static String massUnit(Units units) => switch (units) {
    Units.metric => 'kg',
    Units.imperial => 'lb',
  };

  static String lengthUnit(Units units) => switch (units) {
    Units.metric => 'cm',
    Units.imperial => 'in',
  };

  static double massFromKg(double kg, Units units) => switch (units) {
    Units.metric => kg,
    Units.imperial => kg * lbPerKg,
  };

  static double massToKg(double value, Units units) => switch (units) {
    Units.metric => value,
    Units.imperial => value / lbPerKg,
  };

  static double lengthFromCm(double cm, Units units) => switch (units) {
    Units.metric => cm,
    Units.imperial => cm / cmPerInch,
  };

  static double lengthToCm(double value, Units units) => switch (units) {
    Units.metric => value,
    Units.imperial => value * cmPerInch,
  };

  /// A stored value in the viewer's units, by its stored unit. Anything that
  /// is not kg or cm (a percentage, a ratio) passes through.
  static double fromStored(double value, String storedUnit, Units units) =>
      switch (storedUnit) {
        'kg' => massFromKg(value, units),
        'cm' => lengthFromCm(value, units),
        _ => value,
      };

  /// The unit a stored unit is shown in: "kg" becomes "lb" for imperial.
  static String displayUnit(String storedUnit, Units units) =>
      switch (storedUnit) {
        'kg' => massUnit(units),
        'cm' => lengthUnit(units),
        _ => storedUnit,
      };

  /// "82.4 kg" or "181.7 lb".
  static String formatMass(double kg, Units units, {int decimals = 1}) =>
      '${massFromKg(kg, units).toStringAsFixed(decimals)} ${massUnit(units)}';

  /// "84.0 cm" or "33.1 in".
  static String formatLength(double cm, Units units, {int decimals = 1}) =>
      '${lengthFromCm(cm, units).toStringAsFixed(decimals)} '
      '${lengthUnit(units)}';

  /// A change with its sign: "+0.3 kg", "-0.4 lb", "0.0 kg".
  static String formatMassChange(double kg, Units units, {int decimals = 1}) {
    final shown = massFromKg(kg, units);
    final text = shown.abs().toStringAsFixed(decimals);
    final zero = double.parse(text) == 0;
    final sign = zero ? '' : (shown < 0 ? '-' : '+');
    return '$sign$text ${massUnit(units)}';
  }

  /// A stored value with its unit in the viewer's units: "82.4 kg",
  /// "21.5%", "33.1 in".
  static String formatStored(
    double value,
    String storedUnit,
    Units units, {
    int decimals = 1,
  }) {
    final text = fromStored(value, storedUnit, units).toStringAsFixed(decimals);
    final unit = displayUnit(storedUnit, units);
    if (unit.isEmpty) return text;
    if (unit == '%') return '$text%';
    return '$text $unit';
  }

  /// Whole feet and the inches left over, rounded to the nearest inch.
  static ({int feet, int inches}) feetAndInches(double cm) {
    final total = (cm / cmPerInch).round();
    return (feet: total ~/ inchesPerFoot, inches: total % inchesPerFoot);
  }

  static double feetAndInchesToCm(int feet, num inches) =>
      (feet * inchesPerFoot + inches) * cmPerInch;

  /// "178 cm" or "5 ft 10 in".
  static String formatHeight(double cm, Units units) {
    if (units == Units.metric) return '${cm.round()} cm';
    final parts = feetAndInches(cm);
    return '${parts.feet} ft ${parts.inches} in';
  }

  /// Reads what a person typed: "82.5", "82,5", " 82 ". Null when it is not
  /// a number.
  static double? parse(String text) {
    final cleaned = text.trim().replaceAll(',', '.');
    if (cleaned.isEmpty) return null;
    final value = double.tryParse(cleaned);
    if (value == null || !value.isFinite) return null;
    return value;
  }

  /// A number for a text field: no trailing ".0", at most [decimals] places.
  static String forField(double value, {int decimals = 1}) {
    final text = value.toStringAsFixed(decimals);
    if (!text.contains('.')) return text;
    return text.replaceFirst(RegExp(r'\.?0+$'), '');
  }
}
