import '../utils/json_reader.dart';
import 'metric.dart';

/// One reading of one metric. The same class covers a sample read back from
/// the server (`id` and `sourceId` set) and a sample a collector produced
/// (`sourceUid` set, `id` empty).
class Sample {
  /// Server id; empty before the sample has been accepted.
  final String id;

  /// Stable id from the originating platform. The server deduplicates on
  /// `(sourceId, sourceUid)`, so a resend never doubles a reading.
  final String sourceUid;

  /// Catalogue key, kept as a string so unknown keys survive a round trip.
  final String metric;
  final DateTime start;

  /// Null for an instant reading, set for an interval.
  final DateTime? end;
  final double value;
  final String unit;
  final String sourceId;
  final Map<String, dynamic> meta;

  const Sample({
    this.id = '',
    this.sourceUid = '',
    required this.metric,
    required this.start,
    this.end,
    required this.value,
    required this.unit,
    this.sourceId = '',
    this.meta = const {},
  });

  /// Convenience constructor for a catalogued metric: the unit is implied.
  Sample.of(
    MetricKey key, {
    required this.sourceUid,
    required this.start,
    this.end,
    required this.value,
    this.meta = const {},
  }) : id = '',
       metric = key.wireValue,
       unit = key.unit,
       sourceId = '';

  MetricKey? get metricKey => MetricKey.fromString(metric);
  bool get isInterval => end != null;

  factory Sample.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Sample(
      id: r.string('id'),
      sourceUid: r.string('sourceUid'),
      metric: r.string('metric'),
      start: r.dateTime('start') ?? DateTime.fromMillisecondsSinceEpoch(0),
      end: r.dateTime('end'),
      value: readDouble(r.raw('value')) ?? 0,
      unit: r.string('unit'),
      sourceId: r.string('sourceId'),
      meta: r.object('meta') ?? const {},
    );
  }

  /// The shape inside `samples` of POST /ingest/batch.
  Map<String, dynamic> toIngestJson() => {
    'sourceUid': sourceUid,
    'metric': metric,
    'start': start.toUtc().toIso8601String(),
    'end': end?.toUtc().toIso8601String(),
    'value': value,
    'unit': unit,
    'meta': meta,
  };

  Sample copyWith({String? sourceId}) => Sample(
    id: id,
    sourceUid: sourceUid,
    metric: metric,
    start: start,
    end: end,
    value: value,
    unit: unit,
    sourceId: sourceId ?? this.sourceId,
    meta: meta,
  );
}

/// A page of GET /data/samples.
class SamplesPage {
  final List<Sample> items;
  final String? nextCursor;

  const SamplesPage({this.items = const [], this.nextCursor});

  factory SamplesPage.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SamplesPage(
      items: [for (final s in r.objects('items')) Sample.fromJson(s)],
      nextCursor: r.stringOrNull('nextCursor'),
    );
  }
}

/// A workout session. Energy, distance and heart rate are optional because
/// not every platform or activity records them.
class Workout {
  final String id;
  final String sourceUid;

  /// Lower-case activity name, for example `running` or `strength_training`.
  final String type;
  final DateTime start;
  final DateTime end;
  final double? distanceM;
  final double? energyKcal;
  final double? avgHr;
  final double? maxHr;
  final Map<String, dynamic>? routeGeoJson;
  final Map<String, dynamic> meta;

  const Workout({
    this.id = '',
    this.sourceUid = '',
    required this.type,
    required this.start,
    required this.end,
    this.distanceM,
    this.energyKcal,
    this.avgHr,
    this.maxHr,
    this.routeGeoJson,
    this.meta = const {},
  });

  Duration get duration => end.difference(start);

  factory Workout.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final start = r.dateTime('start') ?? DateTime.fromMillisecondsSinceEpoch(0);
    return Workout(
      id: r.string('id'),
      sourceUid: r.string('sourceUid'),
      type: r.string('type', fallback: 'other'),
      start: start,
      end: r.dateTime('end') ?? start,
      distanceM: readDouble(r.raw('distanceM')),
      energyKcal: readDouble(r.raw('energyKcal')),
      avgHr: readDouble(r.raw('avgHr')),
      maxHr: readDouble(r.raw('maxHr')),
      routeGeoJson: r.object('routeGeoJson'),
      meta: r.object('meta') ?? const {},
    );
  }

  /// The shape inside `workouts` of POST /ingest/batch.
  Map<String, dynamic> toIngestJson() => {
    'sourceUid': sourceUid,
    'type': type,
    'start': start.toUtc().toIso8601String(),
    'end': end.toUtc().toIso8601String(),
    'distanceM': distanceM,
    'energyKcal': energyKcal,
    'avgHr': avgHr,
    'maxHr': maxHr,
    'routeGeoJson': routeGeoJson,
    'meta': meta,
  };
}

/// One rejected row in an ingest response.
class IngestRejection {
  final String sourceUid;
  final String reason;

  const IngestRejection({required this.sourceUid, required this.reason});

  factory IngestRejection.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return IngestRejection(
      sourceUid: r.string('sourceUid'),
      reason: r.string('reason'),
    );
  }
}

/// Response of POST /ingest/batch.
class IngestResult {
  final int accepted;
  final int duplicates;
  final List<IngestRejection> rejected;
  final String? cursor;

  const IngestResult({
    this.accepted = 0,
    this.duplicates = 0,
    this.rejected = const [],
    this.cursor,
  });

  factory IngestResult.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return IngestResult(
      accepted: r.integer('accepted'),
      duplicates: r.integer('duplicates'),
      rejected: [
        for (final j in r.objects('rejected')) IngestRejection.fromJson(j),
      ],
      cursor: r.stringOrNull('cursor'),
    );
  }
}
