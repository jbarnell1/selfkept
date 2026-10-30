import '../utils/json_reader.dart';
import 'band_position.dart';
import 'metric.dart';

/// Metrics in the order Trends offers them. Anything the list does not name
/// follows, alphabetically.
const List<String> trendMetricPriority = [
  'sleep_stage',
  'hrv_sdnn_ms',
  'hrv_rmssd_ms',
  'resting_hr_bpm',
  'wrist_temp_delta_c',
  'body_temp_c',
  'spo2_pct',
  'respiratory_rate_bpm',
  'steps',
  'exercise_min',
  'active_energy_kcal',
  'weight_kg',
  'checkin_mood',
  'oura_readiness',
  'oura_sleep_score',
];

int compareTrendMetrics(String a, String b) {
  final ia = trendMetricPriority.indexOf(a);
  final ib = trendMetricPriority.indexOf(b);
  if (ia >= 0 && ib >= 0) return ia.compareTo(ib);
  if (ia >= 0) return -1;
  if (ib >= 0) return 1;
  return a.compareTo(b);
}

/// One row of GET /trends: a metric with at least seven days behind it.
class TrendMetric {
  final String metric;
  final String unit;
  final String displayName;
  final double? latestValue;
  final DateTime? latestDay;
  final BandPosition? band;
  final int baselineDays;

  /// The last 28 days, oldest first, for the row's small line.
  final List<TrendPoint> recent;

  /// The usual range those days are drawn against; null while learning.
  final TrendBand? range;

  const TrendMetric({
    required this.metric,
    this.unit = '',
    this.displayName = '',
    this.latestValue,
    this.latestDay,
    this.band,
    this.baselineDays = 0,
    this.recent = const [],
    this.range,
  });

  /// The latest day sits outside the usual range, on either side.
  bool get isOutside =>
      band == BandPosition.below || band == BandPosition.above;

  MetricKey? get metricKey => MetricKey.fromString(metric);

  /// The server's name, else the catalogue's, else the key itself.
  String get label =>
      displayName.isNotEmpty ? displayName : (metricKey?.displayName ?? metric);

  factory TrendMetric.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final range = r.object('range');
    final recent = [for (final p in r.objects('recent')) TrendPoint.fromJson(p)]
      ..sort((a, b) => a.day.compareTo(b.day));
    return TrendMetric(
      metric: r.string('metric'),
      unit: r.string('unit'),
      displayName: r.string('displayName'),
      latestValue: readDouble(r.raw('latestValue')),
      latestDay: r.dateTime('latestDay'),
      band: BandPosition.fromString(r.stringOrNull('band')),
      baselineDays: r.integer('baselineDays'),
      recent: recent,
      range: range == null ? null : TrendBand.fromJson(range),
    );
  }

  /// Parses the `{ metrics: [...] }` envelope, sorted by product priority.
  /// Accepts the bare array the server sends, or `{ metrics: [...] }`.
  static List<TrendMetric> listFromJson(dynamic body) {
    final items = body is List
        ? asJsonList(body)
        : JsonReader(asJsonMap(body)).objects('metrics');
    final list = [for (final m in items) TrendMetric.fromJson(m)];
    list.sort((a, b) => compareTrendMetrics(a.metric, b.metric));
    return list;
  }
}

/// One day on the Trends chart.
class TrendPoint {
  final DateTime day;
  final double value;
  final double? min;
  final double? max;

  const TrendPoint({
    required this.day,
    required this.value,
    this.min,
    this.max,
  });

  factory TrendPoint.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return TrendPoint(
      day: r.dateTime('day') ?? DateTime.fromMillisecondsSinceEpoch(0),
      value: readDouble(r.raw('value')) ?? 0,
      min: readDouble(r.raw('min')),
      max: readDouble(r.raw('max')),
    );
  }
}

/// The 28-day band: p25 to p75 is drawn, median28 is the hairline, and
/// anything outside p10 to p90 is marked.
class TrendBand {
  final double median28;
  final double p25;
  final double p75;
  final double p10;
  final double p90;

  const TrendBand({
    required this.median28,
    required this.p25,
    required this.p75,
    required this.p10,
    required this.p90,
  });

  bool isOutlier(double value) => value < p10 || value > p90;

  BandPosition positionOf(double value) {
    if (value < p25) return BandPosition.below;
    if (value > p75) return BandPosition.above;
    return BandPosition.inside;
  }

  factory TrendBand.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return TrendBand(
      median28: readDouble(r.raw('median28')) ?? 0,
      p25: readDouble(r.raw('p25')) ?? 0,
      p75: readDouble(r.raw('p75')) ?? 0,
      p10: readDouble(r.raw('p10')) ?? 0,
      p90: readDouble(r.raw('p90')) ?? 0,
    );
  }
}

/// Answer of GET /trends/{metric}.
class TrendSeries {
  final String metric;
  final String unit;
  final List<TrendPoint> points;

  /// Null while the band is not established (fewer than 14 days).
  final TrendBand? band;
  final int baselineDays;

  const TrendSeries({
    required this.metric,
    this.unit = '',
    this.points = const [],
    this.band,
    this.baselineDays = 0,
  });

  bool get isEmpty => points.isEmpty;

  /// The catalogue's name for the metric, else the key itself.
  String get label => MetricKey.fromString(metric)?.displayName ?? metric;

  factory TrendSeries.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final band = r.object('band');
    final points = [
      for (final p in r.objects('points')) TrendPoint.fromJson(p),
    ];
    points.sort((a, b) => a.day.compareTo(b.day));
    return TrendSeries(
      metric: r.string('metric'),
      unit: r.string('unit'),
      points: points,
      band: band == null ? null : TrendBand.fromJson(band),
      baselineDays: r.integer('baselineDays'),
    );
  }
}
