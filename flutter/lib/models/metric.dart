import 'enums.dart';

/// How a metric's readings relate to time.
enum MetricKind {
  /// One reading at one moment: `end` is null.
  instant,

  /// A reading that covers a span: `end` is set.
  interval,

  /// One value per calendar day.
  daily;

  String get wireValue => name;
  String toJson() => wireValue;

  static MetricKind fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : instant;

  static MetricKind fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final k in values) {
      if (k.name == lower) return k;
    }
    return instant;
  }

  String get displayName => switch (this) {
    instant => 'Instant',
    interval => 'Interval',
    daily => 'Daily',
  };
}

/// The first-release metric catalogue from docs/ingest-contract.md. Keys are
/// stable wire identifiers; the enum carries the stored unit, the kind and the
/// sharing category so collectors can label samples without a round trip.
///
/// Lab markers (`lab.*`) are not enumerated: they arrive through the labs
/// endpoints, never through ingest.
enum MetricKey {
  steps('steps', 'count', MetricKind.interval, ShareCategory.activity),
  distanceM('distance_m', 'm', MetricKind.interval, ShareCategory.activity),
  activeEnergyKcal(
    'active_energy_kcal',
    'kcal',
    MetricKind.interval,
    ShareCategory.activity,
  ),
  basalEnergyKcal(
    'basal_energy_kcal',
    'kcal',
    MetricKind.interval,
    ShareCategory.activity,
  ),
  exerciseMin(
    'exercise_min',
    'min',
    MetricKind.interval,
    ShareCategory.activity,
  ),
  flights('flights', 'count', MetricKind.interval, ShareCategory.activity),
  heartRateBpm(
    'heart_rate_bpm',
    'bpm',
    MetricKind.instant,
    ShareCategory.vitals,
  ),
  restingHrBpm('resting_hr_bpm', 'bpm', MetricKind.daily, ShareCategory.vitals),
  hrvSdnnMs('hrv_sdnn_ms', 'ms', MetricKind.instant, ShareCategory.vitals),
  hrvRmssdMs('hrv_rmssd_ms', 'ms', MetricKind.instant, ShareCategory.vitals),
  spo2Pct('spo2_pct', '%', MetricKind.instant, ShareCategory.vitals),
  respiratoryRateBpm(
    'respiratory_rate_bpm',
    'breaths/min',
    MetricKind.instant,
    ShareCategory.vitals,
  ),
  wristTempDeltaC(
    'wrist_temp_delta_c',
    'C',
    MetricKind.daily,
    ShareCategory.vitals,
  ),
  bodyTempC('body_temp_c', 'C', MetricKind.instant, ShareCategory.vitals),
  sleepStage('sleep_stage', 'stage', MetricKind.interval, ShareCategory.sleep),
  vo2max('vo2max', 'mL/kg/min', MetricKind.instant, ShareCategory.body),
  weightKg('weight_kg', 'kg', MetricKind.instant, ShareCategory.body),
  bodyFatPct('body_fat_pct', '%', MetricKind.instant, ShareCategory.body),
  heightCm('height_cm', 'cm', MetricKind.instant, ShareCategory.body),
  menstrualFlow(
    'menstrual_flow',
    'level',
    MetricKind.interval,
    ShareCategory.cycle,
  ),
  basalBodyTempC(
    'basal_body_temp_c',
    'C',
    MetricKind.instant,
    ShareCategory.cycle,
  ),
  bloodGlucoseMmol(
    'blood_glucose_mmol',
    'mmol/L',
    MetricKind.instant,
    ShareCategory.vitals,
  ),
  bloodPressureSys(
    'blood_pressure_sys',
    'mmHg',
    MetricKind.instant,
    ShareCategory.vitals,
  ),
  bloodPressureDia(
    'blood_pressure_dia',
    'mmHg',
    MetricKind.instant,
    ShareCategory.vitals,
  ),
  ouraReadiness(
    'oura_readiness',
    'score',
    MetricKind.daily,
    ShareCategory.vitals,
  ),
  ouraSleepScore(
    'oura_sleep_score',
    'score',
    MetricKind.daily,
    ShareCategory.vitals,
  ),
  ouraActivityScore(
    'oura_activity_score',
    'score',
    MetricKind.daily,
    ShareCategory.vitals,
  ),
  ouraResilience(
    'oura_resilience',
    'level',
    MetricKind.daily,
    ShareCategory.vitals,
  ),
  ouraStressHighS(
    'oura_stress_high_s',
    's',
    MetricKind.daily,
    ShareCategory.vitals,
  ),
  checkinMood(
    'checkin_mood',
    'level',
    MetricKind.instant,
    ShareCategory.checkins,
  );

  /// Stable identifier on the wire.
  final String wireValue;

  /// Unit the server stores. Clients send this unit.
  final String unit;
  final MetricKind kind;
  final ShareCategory category;

  const MetricKey(this.wireValue, this.unit, this.kind, this.category);

  String toJson() => wireValue;

  static MetricKey fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : steps;

  /// Null for an unknown key, so callers skip the sample rather than guess.
  static MetricKey? fromString(String? value) {
    if (value == null) return null;
    final lower = value.trim().toLowerCase();
    for (final key in values) {
      if (key.wireValue == lower) return key;
    }
    return null;
  }

  /// Metrics the phone collectors read. Oura and check-in keys are produced
  /// elsewhere and never requested from HealthKit or Health Connect.
  static Set<MetricKey> get collectable => {
    for (final key in values)
      if (!key.wireValue.startsWith('oura_') && key != checkinMood) key,
  };

  String get displayName => switch (this) {
    steps => 'Steps',
    distanceM => 'Distance',
    activeEnergyKcal => 'Active energy',
    basalEnergyKcal => 'Resting energy',
    exerciseMin => 'Exercise minutes',
    flights => 'Flights climbed',
    heartRateBpm => 'Heart rate',
    restingHrBpm => 'Resting heart rate',
    hrvSdnnMs => 'HRV (SDNN)',
    hrvRmssdMs => 'HRV (RMSSD)',
    spo2Pct => 'Blood oxygen',
    respiratoryRateBpm => 'Respiratory rate',
    wristTempDeltaC => 'Wrist temperature change',
    bodyTempC => 'Body temperature',
    sleepStage => 'Sleep',
    vo2max => 'VO2 max',
    weightKg => 'Weight',
    bodyFatPct => 'Body fat',
    heightCm => 'Height',
    menstrualFlow => 'Menstrual flow',
    basalBodyTempC => 'Basal body temperature',
    bloodGlucoseMmol => 'Blood glucose',
    bloodPressureSys => 'Blood pressure (systolic)',
    bloodPressureDia => 'Blood pressure (diastolic)',
    ouraReadiness => 'Readiness',
    ouraSleepScore => 'Sleep score',
    ouraActivityScore => 'Activity score',
    ouraResilience => 'Resilience',
    ouraStressHighS => 'Time in high stress',
    checkinMood => 'Mood',
  };
}

/// Sleep stage values in `sleep_stage` samples.
enum SleepStage {
  inBed(0, 'in_bed'),
  awake(1, 'awake'),
  light(2, 'light'),
  deep(3, 'deep'),
  rem(4, 'rem'),
  unspecified(5, 'unspecified');

  final int value;
  final String wireValue;

  const SleepStage(this.value, this.wireValue);

  int toJson() => value;

  static SleepStage fromInt(int value) {
    for (final s in values) {
      if (s.value == value) return s;
    }
    return unspecified;
  }

  static SleepStage fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final s in values) {
      if (s.wireValue == lower || s.name.toLowerCase() == lower) return s;
    }
    return unspecified;
  }

  String get displayName => switch (this) {
    inBed => 'In bed',
    awake => 'Awake',
    light => 'Light',
    deep => 'Deep',
    rem => 'REM',
    unspecified => 'Asleep',
  };
}

/// Flow values in `menstrual_flow` samples.
enum MenstrualFlow {
  none(0, 'none'),
  light(1, 'light'),
  medium(2, 'medium'),
  heavy(3, 'heavy'),
  unspecified(4, 'unspecified');

  final int value;
  final String wireValue;

  const MenstrualFlow(this.value, this.wireValue);

  int toJson() => value;

  static MenstrualFlow fromInt(int value) {
    for (final f in values) {
      if (f.value == value) return f;
    }
    return unspecified;
  }

  static MenstrualFlow fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final f in values) {
      if (f.wireValue == lower) return f;
    }
    return unspecified;
  }

  String get displayName => switch (this) {
    none => 'None',
    light => 'Light',
    medium => 'Medium',
    heavy => 'Heavy',
    unspecified => 'Unspecified',
  };
}
