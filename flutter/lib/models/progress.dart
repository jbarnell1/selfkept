import '../utils/json_reader.dart';
import 'goal.dart';
import 'strength.dart';
import 'target.dart';

/// The three readings a person can type in.
enum BodyMeasure {
  weight('weight_kg', 'Weight'),
  bodyFat('body_fat_pct', 'Body fat'),
  waist('waist_cm', 'Waist');

  final String wireValue;
  final String displayName;

  const BodyMeasure(this.wireValue, this.displayName);

  String toJson() => wireValue;

  static BodyMeasure fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final m in values) {
      if (m.wireValue == lower) return m;
    }
    return weight;
  }
}

/// A typed-in reading, as the server stored it.
class Measurement {
  final String id;
  final String metricKey;
  final double value;
  final String unit;
  final DateTime? at;

  const Measurement({
    required this.id,
    required this.metricKey,
    required this.value,
    this.unit = '',
    this.at,
  });

  factory Measurement.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Measurement(
      id: r.string('id'),
      metricKey: r.string('metricKey'),
      value: readDouble(r.raw('value')) ?? 0,
      unit: r.string('unit'),
      at: r.dateTime('at'),
    );
  }
}

class BodyPoint {
  final DateTime day;
  final double value;

  const BodyPoint({required this.day, required this.value});

  /// Null when the day or the value is missing.
  static BodyPoint? tryFromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final day = r.dateTime('day');
    final value = readDouble(r.raw('value'));
    if (day == null || value == null) return null;
    return BodyPoint(day: day, value: value);
  }

  static List<BodyPoint> listFrom(List<Map<String, dynamic>> items) => [
    for (final item in items) ?tryFromJson(item),
  ];
}

/// One body series for a small chart. Values are stored metric.
class BodySeries {
  final String key;
  final String displayName;
  final String unit;
  final List<BodyPoint> points;

  /// The smoothed line. Only weight has one.
  final List<BodyPoint> trend;
  final double? latest;
  final double? change;

  const BodySeries({
    required this.key,
    required this.displayName,
    this.unit = '',
    this.points = const [],
    this.trend = const [],
    this.latest,
    this.change,
  });

  factory BodySeries.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return BodySeries(
      key: r.string('key'),
      displayName: r.string('displayName', fallback: r.string('key')),
      unit: r.string('unit'),
      points: BodyPoint.listFrom(r.objects('points')),
      trend: BodyPoint.listFrom(r.objects('trend')),
      latest: readDouble(r.raw('latest')),
      change: readDouble(r.raw('change')),
    );
  }
}

/// Where the body stands now. Every field is optional: only what has a value
/// is shown.
class BodySnapshot {
  final double? weightKg;
  final double? trendWeightKg;
  final double? weeklyRateKg;
  final double? weeklyRatePct;
  final double? bodyFatPct;
  final double? fatMassKg;
  final double? leanMassKg;
  final double? bmi;
  final double? leanMassIndex;
  final double? waistCm;
  final double? waistToHeight;
  final double? leanShareOfLoss;
  final double? heightCm;
  final int readingsSetAside;
  final DateTime? lastWeighedOn;

  const BodySnapshot({
    this.weightKg,
    this.trendWeightKg,
    this.weeklyRateKg,
    this.weeklyRatePct,
    this.bodyFatPct,
    this.fatMassKg,
    this.leanMassKg,
    this.bmi,
    this.leanMassIndex,
    this.waistCm,
    this.waistToHeight,
    this.leanShareOfLoss,
    this.heightCm,
    this.readingsSetAside = 0,
    this.lastWeighedOn,
  });

  bool get hasAnything =>
      weightKg != null ||
      trendWeightKg != null ||
      bodyFatPct != null ||
      fatMassKg != null ||
      leanMassKg != null ||
      bmi != null ||
      waistCm != null ||
      waistToHeight != null;

  factory BodySnapshot.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    double? d(String key) => readDouble(r.raw(key));
    return BodySnapshot(
      weightKg: d('weightKg'),
      trendWeightKg: d('trendWeightKg'),
      weeklyRateKg: d('weeklyRateKg'),
      weeklyRatePct: d('weeklyRatePct'),
      bodyFatPct: d('bodyFatPct'),
      fatMassKg: d('fatMassKg'),
      leanMassKg: d('leanMassKg'),
      bmi: d('bmi'),
      leanMassIndex: d('leanMassIndex'),
      waistCm: d('waistCm'),
      waistToHeight: d('waistToHeight'),
      leanShareOfLoss: d('leanShareOfLoss'),
      heightCm: d('heightCm'),
      readingsSetAside: r.integer('readingsSetAside'),
      lastWeighedOn: r.dateTime('lastWeighedOn'),
    );
  }
}

/// GET /progress: goals, targets, the body and the strength log in one read.
class Progress {
  final DateTime? day;
  final List<GoalProgress> goals;
  final List<TargetTally> targets;
  final BodySnapshot body;
  final List<BodySeries> series;
  final List<StrengthLift> strength;
  final List<GoalSuggestion> suggestions;

  const Progress({
    this.day,
    this.goals = const [],
    this.targets = const [],
    this.body = const BodySnapshot(),
    this.series = const [],
    this.strength = const [],
    this.suggestions = const [],
  });

  factory Progress.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Progress(
      day: r.dateTime('day'),
      goals: [for (final g in r.objects('goals')) GoalProgress.fromJson(g)],
      targets: [for (final t in r.objects('targets')) TargetTally.fromJson(t)],
      body: BodySnapshot.fromJson(r.object('body') ?? const {}),
      series: [for (final s in r.objects('series')) BodySeries.fromJson(s)],
      strength: [
        for (final s in r.objects('strength')) StrengthLift.fromJson(s),
      ],
      suggestions: [
        for (final s in r.objects('suggestions')) GoalSuggestion.fromJson(s),
      ],
    );
  }
}
