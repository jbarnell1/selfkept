import 'package:intl/intl.dart';

import '../utils/json_reader.dart';
import 'band_position.dart';

/// Panel groups, in the order the Labs page lists them.
enum LabGroup {
  hormones,
  thyroid,
  heart,
  metabolic,
  blood,
  kidney,
  liver,
  other;

  String get wireValue => name;
  String toJson() => wireValue;

  static LabGroup fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : other;

  static LabGroup fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final g in values) {
      if (g.name == lower) return g;
    }
    return other;
  }

  String get displayName => switch (this) {
    hormones => 'Hormones',
    thyroid => 'Thyroid',
    heart => 'Heart',
    metabolic => 'Metabolic',
    blood => 'Blood count',
    kidney => 'Kidney',
    liver => 'Liver',
    other => 'Other',
  };
}

/// How much attention an out-of-range marker asks for. Never red: watch is
/// amber, clinician is terracotta.
enum LabSeverity {
  watch,
  clinician;

  String get wireValue => name;
  String toJson() => wireValue;

  static LabSeverity fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : watch;

  /// Null for an absent or unknown value: no severity means in range.
  static LabSeverity? fromString(String? value) {
    if (value == null) return null;
    final lower = value.trim().toLowerCase();
    for (final s in values) {
      if (s.name == lower) return s;
    }
    return null;
  }

  String get displayName => switch (this) {
    watch => 'Watch',
    clinician => 'Clinician',
  };
}

/// The three markers the server computes from others.
const Set<String> labRatioKeys = {
  'lab.ldl_apob_ratio',
  'lab.tc_hdl_ratio',
  'lab.tg_hdl_ratio',
};

/// A lab value as text: whole numbers plain, otherwise up to two decimals
/// with trailing zeros dropped. Thousands get a separator.
String formatLabValue(num value) {
  if (value == value.roundToDouble()) {
    return NumberFormat('#,##0').format(value.round());
  }
  return NumberFormat('#,##0.##').format(value);
}

/// The date as the server wants it: yyyy-MM-dd.
String labDateOnly(DateTime date) => DateFormat('yyyy-MM-dd').format(date);

/// One value the parser read from a line of a results screen.
class LabCandidate {
  final String metricKey;
  final String displayName;
  final double value;
  final String? unit;
  final String? flag;
  final String rawText;
  final double confidence;
  final int line;

  const LabCandidate({
    required this.metricKey,
    this.displayName = '',
    required this.value,
    this.unit,
    this.flag,
    this.rawText = '',
    this.confidence = 0,
    this.line = 0,
  });

  factory LabCandidate.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabCandidate(
      metricKey: r.string('metricKey'),
      displayName: r.string('displayName'),
      value: readDouble(r.raw('value')) ?? 0,
      unit: r.stringOrNull('unit'),
      flag: r.stringOrNull('flag'),
      rawText: r.string('rawText'),
      confidence: readDouble(r.raw('confidence')) ?? 0,
      line: r.integer('line'),
    );
  }
}

/// Answer of POST /labs/parse.
class ParseResult {
  final List<LabCandidate> candidates;
  final List<String> unmatchedLines;

  const ParseResult({
    this.candidates = const [],
    this.unmatchedLines = const [],
  });

  factory ParseResult.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ParseResult(
      candidates: [
        for (final c in r.objects('candidates')) LabCandidate.fromJson(c),
      ],
      unmatchedLines: r.strings('unmatchedLines'),
    );
  }

  /// The best candidate per marker: highest confidence, first on a tie.
  Map<String, LabCandidate> bestByMetric() {
    final best = <String, LabCandidate>{};
    for (final c in candidates) {
      final current = best[c.metricKey];
      if (current == null || c.confidence > current.confidence) {
        best[c.metricKey] = c;
      }
    }
    return best;
  }
}

/// One value to store, as sent in POST /labs/reports.
class LabResultInput {
  final String metricKey;
  final double value;
  final String unit;
  final String? flag;
  final String? rawText;

  const LabResultInput({
    required this.metricKey,
    required this.value,
    required this.unit,
    this.flag,
    this.rawText,
  });

  Map<String, dynamic> toJson() => {
    'metricKey': metricKey,
    'value': value,
    'unit': unit,
    'flag': ?flag,
    'rawText': ?rawText,
  };
}

/// One confirmed value in a stored report.
class LabResult {
  final String id;
  final String metricKey;
  final String displayName;
  final double value;
  final String unit;

  /// high, low, normal or none, as reported.
  final String flag;
  final String? rawText;
  final bool derived;
  final double? referenceLow;
  final double? referenceHigh;

  const LabResult({
    required this.id,
    required this.metricKey,
    this.displayName = '',
    required this.value,
    this.unit = '',
    this.flag = 'none',
    this.rawText,
    this.derived = false,
    this.referenceLow,
    this.referenceHigh,
  });

  factory LabResult.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabResult(
      id: r.string('id'),
      metricKey: r.string('metricKey'),
      displayName: r.string('displayName'),
      value: readDouble(r.raw('value')) ?? 0,
      unit: r.string('unit'),
      flag: r.string('flag', fallback: 'none'),
      rawText: r.stringOrNull('rawText'),
      derived: r.boolean('derived'),
      referenceLow: readDouble(r.raw('referenceLow')),
      referenceHigh: readDouble(r.raw('referenceHigh')),
    );
  }
}

/// One panel: a collection date and the values read from it.
class LabReport {
  final String id;
  final String personId;
  final String provider;
  final DateTime collectedOn;
  final DateTime? receivedOn;
  final bool hasImage;
  final String? notes;
  final DateTime? createdAt;
  final DateTime? confirmedAt;
  final List<LabResult> results;

  const LabReport({
    required this.id,
    this.personId = '',
    this.provider = 'manual',
    required this.collectedOn,
    this.receivedOn,
    this.hasImage = false,
    this.notes,
    this.createdAt,
    this.confirmedAt,
    this.results = const [],
  });

  /// "Rythm", "Manual": the provider with a capital letter.
  String get providerLabel =>
      provider.isEmpty ? '' : provider[0].toUpperCase() + provider.substring(1);

  factory LabReport.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabReport(
      id: r.string('id'),
      personId: r.string('personId'),
      provider: r.string('provider', fallback: 'manual'),
      collectedOn:
          r.dateTime('collectedOn') ?? DateTime.fromMillisecondsSinceEpoch(0),
      receivedOn: r.dateTime('receivedOn'),
      hasImage: r.boolean('hasImage'),
      notes: r.stringOrNull('notes'),
      createdAt: r.dateTime('createdAt'),
      confirmedAt: r.dateTime('confirmedAt'),
      results: [for (final x in r.objects('results')) LabResult.fromJson(x)],
    );
  }

  static List<LabReport> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) LabReport.fromJson(item),
  ];
}

/// A low and a high, either of which may be missing.
class LabRange {
  final double? low;
  final double? high;

  const LabRange({this.low, this.high});

  bool get isEmpty => low == null && high == null;
  bool get hasBoth => low != null && high != null;

  factory LabRange.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabRange(
      low: readDouble(r.raw('low')),
      high: readDouble(r.raw('high')),
    );
  }

  /// Null when the map is absent or names neither bound.
  static LabRange? maybe(Map<String, dynamic>? json) {
    if (json == null) return null;
    final range = LabRange.fromJson(json);
    return range.isEmpty ? null : range;
  }
}

/// One dated value of one marker.
class LabPoint {
  final DateTime date;
  final double value;
  final String flag;
  final bool derived;
  final String reportId;

  const LabPoint({
    required this.date,
    required this.value,
    this.flag = 'none',
    this.derived = false,
    this.reportId = '',
  });

  factory LabPoint.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabPoint(
      date: r.dateTime('date') ?? DateTime.fromMillisecondsSinceEpoch(0),
      value: readDouble(r.raw('value')) ?? 0,
      flag: r.string('flag', fallback: 'none'),
      derived: r.boolean('derived'),
      reportId: r.string('reportId'),
    );
  }
}

/// One row of GET /labs/markers: a catalogue marker with the person's latest
/// values and the ranges that apply to them.
class LabMarker {
  final String metricKey;
  final String displayName;
  final String unit;
  final LabGroup group;
  final String sexApplicability;
  final bool applicable;

  /// up, down or range.
  final String goodDirection;
  final double? latestValue;
  final DateTime? latestDate;
  final String? latestFlag;
  final double? previousValue;
  final DateTime? previousDate;
  final LabRange? populationRange;
  final LabRange? optimalRange;
  final LabSeverity? tier;
  final BandPosition? position;
  final LabSeverity? severity;

  /// Oldest first.
  final List<LabPoint> lastThree;

  const LabMarker({
    required this.metricKey,
    this.displayName = '',
    this.unit = '',
    this.group = LabGroup.other,
    this.sexApplicability = 'both',
    this.applicable = true,
    this.goodDirection = 'range',
    this.latestValue,
    this.latestDate,
    this.latestFlag,
    this.previousValue,
    this.previousDate,
    this.populationRange,
    this.optimalRange,
    this.tier,
    this.position,
    this.severity,
    this.lastThree = const [],
  });

  bool get isRatio => labRatioKeys.contains(metricKey);
  bool get hasValue => latestValue != null;

  /// The unit as printed beside a value. A plain ratio has none: "1.13",
  /// not "1.13 ratio".
  String get displayUnit => unit == 'ratio' ? '' : unit;

  factory LabMarker.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final points = [
      for (final p in r.objects('lastThree')) LabPoint.fromJson(p),
    ]..sort((a, b) => a.date.compareTo(b.date));
    return LabMarker(
      metricKey: r.string('metricKey'),
      displayName: r.string('displayName'),
      unit: r.string('unit'),
      group: LabGroup.fromString(r.stringOrNull('group')),
      sexApplicability: r.string('sexApplicability', fallback: 'both'),
      applicable: r.boolean('applicable', fallback: true),
      goodDirection: r.string('goodDirection', fallback: 'range'),
      latestValue: readDouble(r.raw('latestValue')),
      latestDate: r.dateTime('latestDate'),
      latestFlag: r.stringOrNull('latestFlag'),
      previousValue: readDouble(r.raw('previousValue')),
      previousDate: r.dateTime('previousDate'),
      populationRange: LabRange.maybe(r.object('populationRange')),
      optimalRange: LabRange.maybe(r.object('optimalRange')),
      tier: LabSeverity.fromString(r.stringOrNull('tier')),
      position: BandPosition.fromString(r.stringOrNull('position')),
      severity: LabSeverity.fromString(r.stringOrNull('severity')),
      lastThree: points,
    );
  }

  static List<LabMarker> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) LabMarker.fromJson(item),
  ];
}

/// Answer of GET /labs/markers/{metricKey}.
class LabMarkerDetail {
  final LabMarker marker;
  final String blurb;
  final String whatMovesIt;
  final String? sourceUrl;

  /// Newest first.
  final List<LabPoint> history;

  const LabMarkerDetail({
    required this.marker,
    this.blurb = '',
    this.whatMovesIt = '',
    this.sourceUrl,
    this.history = const [],
  });

  factory LabMarkerDetail.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabMarkerDetail(
      marker: LabMarker.fromJson(r.object('marker') ?? const {}),
      blurb: r.string('blurb'),
      whatMovesIt: r.string('whatMovesIt'),
      sourceUrl: r.stringOrNull('sourceUrl'),
      history: [for (final p in r.objects('history')) LabPoint.fromJson(p)],
    );
  }
}
