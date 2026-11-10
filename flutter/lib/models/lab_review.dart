import '../utils/json_reader.dart';
import 'trial.dart';

/// Whether the person had fasted before the blood draw.
enum DrawFasting {
  yes,
  no,
  unknown;

  String get wireValue => name;
  String toJson() => wireValue;

  static DrawFasting fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : unknown;

  static DrawFasting fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final f in values) {
      if (f.name == lower) return f;
    }
    return unknown;
  }

  String get displayName => switch (this) {
    yes => 'Fasting',
    no => 'Not fasting',
    unknown => 'Not known',
  };
}

/// The part of the day the blood was drawn in.
enum DrawPeriod {
  morning,
  afternoon,
  evening,
  unknown;

  String get wireValue => name;
  String toJson() => wireValue;

  static DrawPeriod fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : unknown;

  static DrawPeriod fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final p in values) {
      if (p.name == lower) return p;
    }
    return unknown;
  }

  String get displayName => switch (this) {
    morning => 'Morning',
    afternoon => 'Afternoon',
    evening => 'Evening',
    unknown => 'Not known',
  };
}

/// How one panel's blood was drawn, as stored. `defaulted` is true while
/// these are the provider's pre-filled answers and nobody has changed them.
class DrawConditions {
  final DrawFasting fasting;
  final int? fastingHours;

  /// Local time of day, "HH:mm".
  final String? drawTime;
  final DrawPeriod drawPeriod;
  final bool? hardTraining;
  final bool? unwell;
  final bool defaulted;

  const DrawConditions({
    this.fasting = DrawFasting.unknown,
    this.fastingHours,
    this.drawTime,
    this.drawPeriod = DrawPeriod.unknown,
    this.hardTraining,
    this.unwell,
    this.defaulted = false,
  });

  factory DrawConditions.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final hours = r.raw('fastingHours');
    final training = r.raw('hardTraining');
    final unwell = r.raw('unwell');
    return DrawConditions(
      fasting: DrawFasting.fromString(r.stringOrNull('fasting')),
      fastingHours: hours == null ? null : r.integer('fastingHours'),
      drawTime: r.stringOrNull('drawTime'),
      drawPeriod: DrawPeriod.fromString(r.stringOrNull('drawPeriod')),
      hardTraining: training == null ? null : r.boolean('hardTraining'),
      unwell: unwell == null ? null : r.boolean('unwell'),
      defaulted: r.boolean('defaulted'),
    );
  }

  /// Nothing has been answered.
  bool get isEmpty =>
      fasting == DrawFasting.unknown &&
      fastingHours == null &&
      drawTime == null &&
      drawPeriod == DrawPeriod.unknown &&
      hardTraining == null &&
      unwell == null;

  /// The body of PUT /labs/reports/{id}/conditions. Unknown answers are
  /// left out, so they stay unknown.
  Map<String, dynamic> toInputJson() => {
    if (fasting != DrawFasting.unknown) 'fasting': fasting.wireValue,
    'fastingHours': ?fastingHours,
    'drawTime': ?drawTime,
    if (drawTime == null && drawPeriod != DrawPeriod.unknown)
      'drawPeriod': drawPeriod.wireValue,
    'hardTraining': ?hardTraining,
    'unwell': ?unwell,
  };
}

/// A draw condition that differs between two panels, in the stored words.
class ConditionDifference {
  /// fasting, drawPeriod, hardTraining, unwell or `substance:<tag>`.
  final String condition;
  final String? now;
  final String? then;

  const ConditionDifference({required this.condition, this.now, this.then});

  factory ConditionDifference.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ConditionDifference(
      condition: r.string('condition'),
      now: r.stringOrNull('now'),
      then: r.stringOrNull('then'),
    );
  }
}

/// A link from a draw condition or a substance that applies on this draw.
class ReadWithCare {
  final String linkId;
  final String from;
  final String explanation;
  final String source;

  const ReadWithCare({
    required this.linkId,
    this.from = '',
    this.explanation = '',
    this.source = '',
  });

  factory ReadWithCare.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ReadWithCare(
      linkId: r.string('linkId'),
      from: r.string('from'),
      explanation: r.string('explanation'),
      source: r.string('source'),
    );
  }
}

/// Where a test-to-test variation figure comes from.
class VariationSource {
  final double cviPercent;
  final String source;
  final String sourceUrl;
  final String? caveat;

  const VariationSource({
    required this.cviPercent,
    this.source = '',
    this.sourceUrl = '',
    this.caveat,
  });

  factory VariationSource.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return VariationSource(
      cviPercent: readDouble(r.raw('cviPercent')) ?? 0,
      source: r.string('source'),
      sourceUrl: r.string('sourceUrl'),
      caveat: r.stringOrNull('caveat'),
    );
  }
}

/// The review's word for one marker against the panel before.
enum LabVerdict {
  working('working'),
  rightDirection('right_direction'),
  noClearChange('no_clear_change'),
  notWorking('not_working'),
  cannotTell('cannot_tell');

  final String wireValue;

  const LabVerdict(this.wireValue);

  String toJson() => wireValue;

  static LabVerdict fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : cannotTell;

  static LabVerdict fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final v in values) {
      if (v.wireValue == lower) return v;
    }
    return cannotTell;
  }

  String get displayName => switch (this) {
    working => 'Working',
    rightDirection => 'Right direction',
    noClearChange => 'No clear change',
    notWorking => 'Not working',
    cannotTell => 'Cannot tell',
  };
}

/// A reason code behind a verdict, said in plain words.
String labReasonText(String reason) => switch (reason) {
  'no_previous' => 'There is no earlier panel to compare with.',
  'no_variation_figure' =>
    'No checked figure says how much this test varies by itself.',
  'draw_conditions_differ' =>
    'The two draws were taken under different conditions.',
  'substance_at_draw' =>
    'Something taken around the draw can move this result.',
  'no_wanted_direction' =>
    'It sat inside its range on both panels, so neither way is better.',
  _ => reason.replaceAll('_', ' '),
};

/// One marker on a reviewed panel.
class LabReviewMarker {
  final String metricKey;
  final String displayName;
  final String unit;
  final double value;
  final double? previousValue;
  final double? change;
  final double? changePercent;
  final double? rcvPercent;
  final bool? beyondRcv;

  /// up, down or none; null without a previous value.
  final String? moved;
  final String wanted;
  final LabVerdict verdict;
  final List<String> reasons;
  final List<ReadWithCare> readWithCare;
  final VariationSource? variation;
  final List<String> prep;
  final String? retest;

  const LabReviewMarker({
    required this.metricKey,
    this.displayName = '',
    this.unit = '',
    required this.value,
    this.previousValue,
    this.change,
    this.changePercent,
    this.rcvPercent,
    this.beyondRcv,
    this.moved,
    this.wanted = 'none',
    this.verdict = LabVerdict.cannotTell,
    this.reasons = const [],
    this.readWithCare = const [],
    this.variation,
    this.prep = const [],
    this.retest,
  });

  String get displayUnit => unit == 'ratio' ? '' : unit;

  factory LabReviewMarker.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final beyond = r.raw('beyondRcv');
    final variation = r.object('variation');
    return LabReviewMarker(
      metricKey: r.string('metricKey'),
      displayName: r.string('displayName'),
      unit: r.string('unit'),
      value: readDouble(r.raw('value')) ?? 0,
      previousValue: readDouble(r.raw('previousValue')),
      change: readDouble(r.raw('change')),
      changePercent: readDouble(r.raw('changePercent')),
      rcvPercent: readDouble(r.raw('rcvPercent')),
      beyondRcv: beyond == null ? null : r.boolean('beyondRcv'),
      moved: r.stringOrNull('moved'),
      wanted: r.string('wanted', fallback: 'none'),
      verdict: LabVerdict.fromString(r.stringOrNull('verdict')),
      reasons: r.strings('reasons'),
      readWithCare: [
        for (final x in r.objects('readWithCare')) ReadWithCare.fromJson(x),
      ],
      variation: variation == null ? null : VariationSource.fromJson(variation),
      prep: r.strings('prep'),
      retest: r.stringOrNull('retest'),
    );
  }
}

/// A medicine or supplement active on a draw date.
class TakenItem {
  final String id;
  final String kind;
  final String? brand;
  final String product;
  final List<String> substances;

  const TakenItem({
    required this.id,
    this.kind = 'supplement',
    this.brand,
    this.product = '',
    this.substances = const [],
  });

  /// "Thorne Creatine" or "Creatine monohydrate".
  String get label => brand == null ? product : '$brand $product';

  factory TakenItem.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return TakenItem(
      id: r.string('id'),
      kind: r.string('kind', fallback: 'supplement'),
      brand: r.stringOrNull('brand'),
      product: r.string('product'),
      substances: r.strings('substances'),
    );
  }
}

/// The weigh-in nearest a draw.
class WeightAtDraw {
  final DateTime day;
  final double kg;

  const WeightAtDraw({required this.day, required this.kg});

  static WeightAtDraw? maybe(Map<String, dynamic>? json) {
    if (json == null) return null;
    final r = JsonReader(json);
    final day = r.dateTime('day');
    final kg = readDouble(r.raw('kg'));
    if (day == null || kg == null) return null;
    return WeightAtDraw(day: day, kg: kg);
  }
}

class LabReviewWeight {
  final WeightAtDraw? now;
  final WeightAtDraw? then;
  final double? changeKg;

  const LabReviewWeight({this.now, this.then, this.changeKg});

  factory LabReviewWeight.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabReviewWeight(
      now: WeightAtDraw.maybe(r.object('now')),
      then: WeightAtDraw.maybe(r.object('then')),
      changeKg: readDouble(r.raw('changeKg')),
    );
  }
}

/// A trial that ran between the two panels, with its markers' verdicts.
class LabReviewTrial {
  final Trial trial;
  final Map<String, LabVerdict> verdicts;
  final bool sharedCredit;

  const LabReviewTrial({
    required this.trial,
    this.verdicts = const {},
    this.sharedCredit = false,
  });

  factory LabReviewTrial.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabReviewTrial(
      trial: Trial.fromJson(r.object('trial') ?? const {}),
      verdicts: {
        for (final m in r.objects('markers'))
          JsonReader(m).string('metricKey'): LabVerdict.fromString(
            JsonReader(m).stringOrNull('verdict'),
          ),
      },
      sharedCredit: r.boolean('sharedCredit'),
    );
  }
}

/// Answer of GET /labs/reports/{id}/review: one panel against the one before.
class LabReview {
  final String reportId;
  final String personId;
  final DateTime collectedOn;
  final String? previousReportId;
  final DateTime? previousCollectedOn;
  final DrawConditions conditions;
  final DrawConditions? previousConditions;
  final List<ConditionDifference> conditionsDiffer;
  final List<LabReviewMarker> markers;
  final List<TakenItem> takingNow;
  final List<TakenItem> takingThen;
  final List<String> substancesNow;
  final List<String> substancesThen;

  /// Null when the body record is not shared.
  final LabReviewWeight? weight;

  /// Null when the body record is not shared.
  final List<LabReviewTrial>? trials;
  final Map<LabVerdict, int> verdictCounts;

  const LabReview({
    required this.reportId,
    this.personId = '',
    required this.collectedOn,
    this.previousReportId,
    this.previousCollectedOn,
    this.conditions = const DrawConditions(),
    this.previousConditions,
    this.conditionsDiffer = const [],
    this.markers = const [],
    this.takingNow = const [],
    this.takingThen = const [],
    this.substancesNow = const [],
    this.substancesThen = const [],
    this.weight,
    this.trials,
    this.verdictCounts = const {},
  });

  factory LabReview.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final previous = r.object('previousConditions');
    final weight = r.object('weight');
    final counts = r.object('verdictCounts') ?? const {};
    return LabReview(
      reportId: r.string('reportId'),
      personId: r.string('personId'),
      collectedOn:
          r.dateTime('collectedOn') ?? DateTime.fromMillisecondsSinceEpoch(0),
      previousReportId: r.stringOrNull('previousReportId'),
      previousCollectedOn: r.dateTime('previousCollectedOn'),
      conditions: DrawConditions.fromJson(r.object('conditions') ?? const {}),
      previousConditions: previous == null
          ? null
          : DrawConditions.fromJson(previous),
      conditionsDiffer: [
        for (final d in r.objects('conditionsDiffer'))
          ConditionDifference.fromJson(d),
      ],
      markers: [
        for (final m in r.objects('markers')) LabReviewMarker.fromJson(m),
      ],
      takingNow: [
        for (final t in r.objects('takingNow')) TakenItem.fromJson(t),
      ],
      takingThen: [
        for (final t in r.objects('takingThen')) TakenItem.fromJson(t),
      ],
      substancesNow: r.strings('substancesNow'),
      substancesThen: r.strings('substancesThen'),
      weight: weight == null ? null : LabReviewWeight.fromJson(weight),
      trials: r.has('trials')
          ? [for (final t in r.objects('trials')) LabReviewTrial.fromJson(t)]
          : null,
      verdictCounts: {
        for (final v in LabVerdict.values)
          if (JsonReader(counts).has(v.wireValue))
            v: JsonReader(counts).integer(v.wireValue),
      },
    );
  }
}

/// One panel on the lab timeline.
class LabTimelinePanel {
  final String reportId;
  final DateTime collectedOn;
  final String provider;
  final DrawConditions conditions;
  final int markerCount;
  final WeightAtDraw? weight;
  final List<String> substances;

  const LabTimelinePanel({
    required this.reportId,
    required this.collectedOn,
    this.provider = '',
    this.conditions = const DrawConditions(),
    this.markerCount = 0,
    this.weight,
    this.substances = const [],
  });

  String get providerLabel =>
      provider.isEmpty ? '' : provider[0].toUpperCase() + provider.substring(1);

  factory LabTimelinePanel.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabTimelinePanel(
      reportId: r.string('reportId'),
      collectedOn:
          r.dateTime('collectedOn') ?? DateTime.fromMillisecondsSinceEpoch(0),
      provider: r.string('provider'),
      conditions: DrawConditions.fromJson(r.object('conditions') ?? const {}),
      markerCount: r.integer('markerCount'),
      weight: WeightAtDraw.maybe(r.object('weight')),
      substances: r.strings('substances'),
    );
  }
}

/// Answer of GET /labs/timeline. Panels oldest first.
class LabTimeline {
  final String personId;
  final bool weightShared;
  final List<LabTimelinePanel> panels;

  const LabTimeline({
    this.personId = '',
    this.weightShared = false,
    this.panels = const [],
  });

  factory LabTimeline.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabTimeline(
      personId: r.string('personId'),
      weightShared: r.boolean('weightShared'),
      panels: [
        for (final p in r.objects('panels')) LabTimelinePanel.fromJson(p),
      ],
    );
  }
}

/// One tie between two readings in the link catalogue.
class LabLink {
  final String id;
  final String from;
  final String to;

  /// same, opposite, modifies or none.
  final String direction;
  final String explanation;
  final String source;
  final String sourceUrl;
  final String evidence;

  const LabLink({
    required this.id,
    this.from = '',
    this.to = '',
    this.direction = 'none',
    this.explanation = '',
    this.source = '',
    this.sourceUrl = '',
    this.evidence = '',
  });

  factory LabLink.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabLink(
      id: r.string('id'),
      from: r.string('from'),
      to: r.string('to'),
      direction: r.string('direction', fallback: 'none'),
      explanation: r.string('explanation'),
      source: r.string('source'),
      sourceUrl: r.string('sourceUrl'),
      evidence: r.string('evidence'),
    );
  }
}

/// A reading tied to a marker, with its latest value when it is shared.
class RelatedReading {
  final String key;

  /// lab, metric, substance or condition.
  final String kind;
  final String displayName;
  final String? unit;
  final String category;
  final bool shared;
  final double? latestValue;
  final DateTime? latestDate;
  final List<LabLink> links;

  const RelatedReading({
    required this.key,
    this.kind = 'metric',
    this.displayName = '',
    this.unit,
    this.category = '',
    this.shared = false,
    this.latestValue,
    this.latestDate,
    this.links = const [],
  });

  factory RelatedReading.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return RelatedReading(
      key: r.string('key'),
      kind: r.string('kind', fallback: 'metric'),
      displayName: r.string('displayName'),
      unit: r.stringOrNull('unit'),
      category: r.string('category'),
      shared: r.boolean('shared'),
      latestValue: readDouble(r.raw('latestValue')),
      latestDate: r.dateTime('latestDate'),
      links: [for (final l in r.objects('links')) LabLink.fromJson(l)],
    );
  }
}

/// Answer of GET /labs/markers/{key}/related.
class RelatedReadings {
  final String metricKey;
  final String displayName;
  final List<RelatedReading> related;

  const RelatedReadings({
    required this.metricKey,
    this.displayName = '',
    this.related = const [],
  });

  factory RelatedReadings.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return RelatedReadings(
      metricKey: r.string('metricKey'),
      displayName: r.string('displayName'),
      related: [
        for (final x in r.objects('related')) RelatedReading.fromJson(x),
      ],
    );
  }
}
