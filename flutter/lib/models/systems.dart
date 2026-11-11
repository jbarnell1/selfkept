import '../utils/json_reader.dart';
import 'band_position.dart';
import 'insight.dart';
import 'trend.dart';

/// The person's own 28-day usual for a reading.
class SystemUsual {
  final double median28;
  final double p25;
  final double p75;
  final int days;

  const SystemUsual({
    required this.median28,
    required this.p25,
    required this.p75,
    this.days = 0,
  });

  /// The same range in the shape the sparkline draws.
  TrendBand get band =>
      TrendBand(median28: median28, p25: p25, p75: p75, p10: p25, p90: p75);

  static SystemUsual? maybe(Map<String, dynamic>? json) {
    if (json == null) return null;
    final r = JsonReader(json);
    return SystemUsual(
      median28: readDouble(r.raw('median28')) ?? 0,
      p25: readDouble(r.raw('p25')) ?? 0,
      p75: readDouble(r.raw('p75')) ?? 0,
      days: r.integer('days'),
    );
  }
}

/// One wearable or body reading inside a system.
class SystemReading {
  final String metric;
  final String displayName;
  final String unit;
  final String category;
  final DateTime? latestDay;
  final double? latestValue;
  final SystemUsual? usual;
  final BandPosition? band;

  /// The last 28 days, on the detail only.
  final List<TrendPoint> recent;

  const SystemReading({
    required this.metric,
    this.displayName = '',
    this.unit = '',
    this.category = '',
    this.latestDay,
    this.latestValue,
    this.usual,
    this.band,
    this.recent = const [],
  });

  bool get isOutside =>
      band == BandPosition.below || band == BandPosition.above;

  factory SystemReading.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SystemReading(
      metric: r.string('metric'),
      displayName: r.string('displayName'),
      unit: r.string('unit'),
      category: r.string('category'),
      latestDay: r.dateTime('latestDay'),
      latestValue: readDouble(r.raw('latestValue')),
      usual: SystemUsual.maybe(r.object('usual')),
      band: BandPosition.fromString(r.stringOrNull('band')),
      recent: [for (final p in r.objects('recent')) TrendPoint.fromJson(p)],
    );
  }
}

/// One past value of a lab marker.
class SystemLabPoint {
  final DateTime collectedOn;
  final double value;
  final String flag;

  const SystemLabPoint({
    required this.collectedOn,
    required this.value,
    this.flag = 'none',
  });

  factory SystemLabPoint.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SystemLabPoint(
      collectedOn:
          r.dateTime('collectedOn') ?? DateTime.fromMillisecondsSinceEpoch(0),
      value: readDouble(r.raw('value')) ?? 0,
      flag: r.string('flag', fallback: 'none'),
    );
  }
}

/// One lab marker inside a system.
class SystemLab {
  final String metric;
  final String displayName;
  final String unit;
  final double value;
  final DateTime collectedOn;

  /// low, high, normal or none.
  final String flag;
  final double? previousValue;
  final DateTime? previousOn;

  /// Up to six, oldest first, on the detail only.
  final List<SystemLabPoint> history;

  const SystemLab({
    required this.metric,
    this.displayName = '',
    this.unit = '',
    required this.value,
    required this.collectedOn,
    this.flag = 'none',
    this.previousValue,
    this.previousOn,
    this.history = const [],
  });

  String get displayUnit => unit == 'ratio' ? '' : unit;
  bool get isFlagged => flag == 'high' || flag == 'low';

  factory SystemLab.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SystemLab(
      metric: r.string('metric'),
      displayName: r.string('displayName'),
      unit: r.string('unit'),
      value: readDouble(r.raw('value')) ?? 0,
      collectedOn:
          r.dateTime('collectedOn') ?? DateTime.fromMillisecondsSinceEpoch(0),
      flag: r.string('flag', fallback: 'none'),
      previousValue: readDouble(r.raw('previousValue')),
      previousOn: r.dateTime('previousOn'),
      history: [
        for (final p in r.objects('history')) SystemLabPoint.fromJson(p),
      ],
    );
  }
}

/// One body system: heart and fitness, sleep, metabolism, hormones, body or
/// cycle, with a sentence on where it stands.
class BodySystem {
  final String key;
  final String name;
  final String state;
  final List<SystemReading> readings;
  final List<SystemLab> labs;
  final List<Insight> insights;

  const BodySystem({
    required this.key,
    this.name = '',
    this.state = '',
    this.readings = const [],
    this.labs = const [],
    this.insights = const [],
  });

  factory BodySystem.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return BodySystem(
      key: r.string('key').toLowerCase(),
      name: r.string('name'),
      state: r.string('state'),
      readings: [
        for (final x in r.objects('readings')) SystemReading.fromJson(x),
      ],
      labs: [for (final x in r.objects('labs')) SystemLab.fromJson(x)],
      insights: [for (final x in r.objects('insights')) Insight.fromJson(x)],
    );
  }
}

/// The Labs line at the top of Systems.
class LabsEntry {
  final DateTime? latestPanelOn;
  final int panels;
  final int markers;
  final int outsideRange;

  const LabsEntry({
    this.latestPanelOn,
    this.panels = 0,
    this.markers = 0,
    this.outsideRange = 0,
  });

  factory LabsEntry.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return LabsEntry(
      latestPanelOn: r.dateTime('latestPanelOn'),
      panels: r.integer('panels'),
      markers: r.integer('markers'),
      outsideRange: r.integer('outsideRange'),
    );
  }
}

/// Answer of GET /systems.
class SystemsOverview {
  final DateTime? day;
  final List<BodySystem> systems;

  /// Null when labs are not shared.
  final LabsEntry? labs;

  const SystemsOverview({this.day, this.systems = const [], this.labs});

  factory SystemsOverview.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final labs = r.object('labs');
    return SystemsOverview(
      day: r.dateTime('day'),
      systems: [for (final s in r.objects('systems')) BodySystem.fromJson(s)],
      labs: labs == null ? null : LabsEntry.fromJson(labs),
    );
  }
}

/// One night read back from the sleep record.
class SleepNight {
  /// The day the night ended.
  final DateTime day;
  final DateTime? bedtime;
  final DateTime? wakeTime;
  final double inBedMinutes;
  final double asleepMinutes;
  final double? awakeMinutes;
  final double? lightMinutes;
  final double? deepMinutes;
  final double? remMinutes;
  final double? unspecifiedMinutes;
  final bool hasStages;

  const SleepNight({
    required this.day,
    this.bedtime,
    this.wakeTime,
    this.inBedMinutes = 0,
    this.asleepMinutes = 0,
    this.awakeMinutes,
    this.lightMinutes,
    this.deepMinutes,
    this.remMinutes,
    this.unspecifiedMinutes,
    this.hasStages = false,
  });

  factory SleepNight.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SleepNight(
      day: r.dateTime('day') ?? DateTime.fromMillisecondsSinceEpoch(0),
      bedtime: r.dateTime('bedtime'),
      wakeTime: r.dateTime('wakeTime'),
      inBedMinutes: readDouble(r.raw('inBedMinutes')) ?? 0,
      asleepMinutes: readDouble(r.raw('asleepMinutes')) ?? 0,
      awakeMinutes: readDouble(r.raw('awakeMinutes')),
      lightMinutes: readDouble(r.raw('lightMinutes')),
      deepMinutes: readDouble(r.raw('deepMinutes')),
      remMinutes: readDouble(r.raw('remMinutes')),
      unspecifiedMinutes: readDouble(r.raw('unspecifiedMinutes')),
      hasStages: r.boolean('hasStages'),
    );
  }
}

/// Answer of GET /sleep. Nights oldest first.
class SleepNights {
  final DateTime? from;
  final DateTime? to;
  final List<SleepNight> nights;

  const SleepNights({this.from, this.to, this.nights = const []});

  factory SleepNights.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SleepNights(
      from: r.dateTime('from'),
      to: r.dateTime('to'),
      nights: [for (final n in r.objects('nights')) SleepNight.fromJson(n)],
    );
  }
}

/// One workout read back.
class WorkoutRead {
  final String id;
  final String type;

  /// strength, cardio or mixed.
  final String group;
  final DateTime day;
  final DateTime? startAt;
  final DateTime? endAt;
  final double durationMinutes;
  final double? distanceM;
  final double? energyKcal;
  final double? avgHr;
  final double? maxHr;

  const WorkoutRead({
    required this.id,
    this.type = '',
    this.group = 'mixed',
    required this.day,
    this.startAt,
    this.endAt,
    this.durationMinutes = 0,
    this.distanceM,
    this.energyKcal,
    this.avgHr,
    this.maxHr,
  });

  /// "Running", "Traditional strength training".
  String get typeLabel {
    final words = type.replaceAll('_', ' ').trim();
    if (words.isEmpty) return 'Workout';
    return words[0].toUpperCase() + words.substring(1);
  }

  factory WorkoutRead.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return WorkoutRead(
      id: r.string('id'),
      type: r.string('type'),
      group: r.string('group', fallback: 'mixed'),
      day: r.dateTime('day') ?? DateTime.fromMillisecondsSinceEpoch(0),
      startAt: r.dateTime('startAt'),
      endAt: r.dateTime('endAt'),
      durationMinutes: readDouble(r.raw('durationMinutes')) ?? 0,
      distanceM: readDouble(r.raw('distanceM')),
      energyKcal: readDouble(r.raw('energyKcal')),
      avgHr: readDouble(r.raw('avgHr')),
      maxHr: readDouble(r.raw('maxHr')),
    );
  }
}

/// One week's workout totals.
class WorkoutWeek {
  final DateTime weekOf;
  final int count;
  final double minutes;
  final int strength;
  final int cardio;
  final int mixed;

  const WorkoutWeek({
    required this.weekOf,
    this.count = 0,
    this.minutes = 0,
    this.strength = 0,
    this.cardio = 0,
    this.mixed = 0,
  });

  factory WorkoutWeek.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return WorkoutWeek(
      weekOf: r.dateTime('weekOf') ?? DateTime.fromMillisecondsSinceEpoch(0),
      count: r.integer('count'),
      minutes: readDouble(r.raw('minutes')) ?? 0,
      strength: r.integer('strength'),
      cardio: r.integer('cardio'),
      mixed: r.integer('mixed'),
    );
  }
}

/// Answer of GET /workouts. Both lists oldest first.
class Workouts {
  final DateTime? from;
  final DateTime? to;
  final List<WorkoutRead> workouts;
  final List<WorkoutWeek> weeks;

  const Workouts({
    this.from,
    this.to,
    this.workouts = const [],
    this.weeks = const [],
  });

  factory Workouts.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Workouts(
      from: r.dateTime('from'),
      to: r.dateTime('to'),
      workouts: [
        for (final w in r.objects('workouts')) WorkoutRead.fromJson(w),
      ],
      weeks: [for (final w in r.objects('weeks')) WorkoutWeek.fromJson(w)],
    );
  }
}
