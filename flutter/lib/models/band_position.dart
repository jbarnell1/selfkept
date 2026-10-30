/// Where a value sits against the person's own 28-day band (p25 to p75).
/// Null on the wire means the band is not established yet.
enum BandPosition {
  below,
  inside,
  above;

  String get wireValue => name;
  String toJson() => wireValue;

  static BandPosition fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : inside;

  /// Null for an unknown or absent value: "no band yet" is a real state and
  /// must not be mistaken for "inside".
  static BandPosition? fromString(String? value) {
    if (value == null) return null;
    final lower = value.trim().toLowerCase();
    for (final p in values) {
      if (p.name == lower) return p;
    }
    return null;
  }

  /// Spoken against the person's own baseline, never against a population.
  String get displayName => switch (this) {
    below => 'below your range',
    inside => 'in your range',
    above => 'above your range',
  };
}
