// Enums shared across the client. Every enum here follows the same pattern:
// `fromInt`, `fromString` (case-insensitive, tolerant of unknown values) and
// `displayName`. Code never compares raw `.index` values.

/// What a household member may see of another member's data.
enum ShareCategory {
  activity,
  sleep,
  vitals,
  body,
  labs,
  cycle,
  checkins,
  insights;

  /// Wire value used by the API.
  String get wireValue => name;
  String toJson() => wireValue;

  static ShareCategory fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : activity;

  /// Returns null for an unknown value so callers can skip it rather than
  /// silently granting the wrong category.
  static ShareCategory? fromString(String? value) {
    if (value == null) return null;
    final lower = value.trim().toLowerCase();
    for (final category in values) {
      if (category.name == lower) return category;
    }
    return null;
  }

  String get displayName => switch (this) {
    activity => 'Activity',
    sleep => 'Sleep',
    vitals => 'Vitals',
    body => 'Body',
    labs => 'Labs',
    cycle => 'Cycle',
    checkins => 'Check-ins',
    insights => 'Insights',
  };

  /// One calm line describing what the category covers.
  String get description => switch (this) {
    activity => 'Steps, workouts and movement.',
    sleep => 'Nights, duration and timing.',
    vitals => 'Heart rate, blood pressure and oxygen.',
    body => 'Weight and measurements.',
    labs => 'Blood work and other results.',
    cycle => 'Menstrual cycle and symptoms.',
    checkins => 'Mood and daily notes.',
    insights => 'What the app notices over time.',
  };
}

/// Household role.
enum Role {
  admin,
  member;

  String get wireValue => name;
  String toJson() => wireValue;

  static Role fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : member;

  static Role fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final role in values) {
      if (role.name == lower) return role;
    }
    return member;
  }

  String get displayName => switch (this) {
    admin => 'Admin',
    member => 'Member',
  };
}

/// Sex recorded at birth. Drives reference ranges later; shown as a choice
/// on the profile, never inferred.
enum SexAtBirth {
  female,
  male,
  unspecified;

  String get wireValue => name;
  String toJson() => wireValue;

  static SexAtBirth fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : unspecified;

  static SexAtBirth fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final sex in values) {
      if (sex.name == lower) return sex;
    }
    return unspecified;
  }

  String get displayName => switch (this) {
    female => 'Female',
    male => 'Male',
    unspecified => 'Prefer not to say',
  };
}

/// Measurement units for display.
enum Units {
  metric,
  imperial;

  String get wireValue => name;
  String toJson() => wireValue;

  static Units fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : metric;

  static Units fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final units in values) {
      if (units.name == lower) return units;
    }
    return metric;
  }

  String get displayName => switch (this) {
    metric => 'Metric',
    imperial => 'Imperial',
  };
}
