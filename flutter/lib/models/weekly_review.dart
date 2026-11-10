import '../utils/json_reader.dart';
import 'context.dart';
import 'insight.dart';

/// One target over the week.
class ReviewTarget {
  final String targetId;
  final String label;

  /// day or week.
  final String period;
  final int metDays;
  final int knownDays;
  final bool? met;

  const ReviewTarget({
    required this.targetId,
    this.label = '',
    this.period = 'day',
    this.metDays = 0,
    this.knownDays = 0,
    this.met,
  });

  factory ReviewTarget.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final met = r.raw('met');
    return ReviewTarget(
      targetId: r.string('targetId'),
      label: r.string('label'),
      period: r.string('period', fallback: 'day'),
      metDays: r.integer('metDays'),
      knownDays: r.integer('knownDays'),
      met: met == null ? null : r.boolean('met'),
    );
  }
}

class ReviewTargets {
  final List<ReviewTarget> kept;
  final List<ReviewTarget> missed;
  final List<ReviewTarget> unknown;

  const ReviewTargets({
    this.kept = const [],
    this.missed = const [],
    this.unknown = const [],
  });

  bool get isEmpty => kept.isEmpty && missed.isEmpty && unknown.isEmpty;

  factory ReviewTargets.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ReviewTargets(
      kept: [for (final t in r.objects('kept')) ReviewTarget.fromJson(t)],
      missed: [for (final t in r.objects('missed')) ReviewTarget.fromJson(t)],
      unknown: [for (final t in r.objects('unknown')) ReviewTarget.fromJson(t)],
    );
  }
}

/// One goal over the week, with the server's summary.
class ReviewGoal {
  final String goalId;
  final String slot;
  final String kind;
  final String title;
  final String summary;

  const ReviewGoal({
    required this.goalId,
    this.slot = '',
    this.kind = '',
    this.title = '',
    this.summary = '',
  });

  factory ReviewGoal.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ReviewGoal(
      goalId: r.string('goalId'),
      slot: r.string('slot'),
      kind: r.string('kind'),
      title: r.string('title'),
      summary: r.string('summary'),
    );
  }
}

class ReviewSleep {
  final int nights;
  final double? medianMinutes;
  final String summary;

  const ReviewSleep({this.nights = 0, this.medianMinutes, this.summary = ''});

  factory ReviewSleep.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ReviewSleep(
      nights: r.integer('nights'),
      medianMinutes: readDouble(r.raw('medianMinutes')),
      summary: r.string('summary'),
    );
  }
}

/// A reading that ran away from its usual this week.
class ReviewStandout {
  final String metric;
  final String displayName;

  /// up or down.
  final String direction;
  final String summary;

  const ReviewStandout({
    required this.metric,
    this.displayName = '',
    this.direction = '',
    this.summary = '',
  });

  factory ReviewStandout.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ReviewStandout(
      metric: r.string('metric'),
      displayName: r.string('displayName'),
      direction: r.string('direction'),
      summary: r.string('summary'),
    );
  }
}

/// Answer of GET /review/weekly. A null section is not shared.
class WeeklyReview {
  final DateTime weekOf;
  final DateTime weekEnd;
  final bool complete;
  final ReviewTargets? targets;
  final List<ReviewGoal>? goals;
  final ReviewSleep? sleep;
  final List<ReviewStandout>? standouts;
  final List<Insight>? insights;
  final List<PersonalPattern>? patterns;

  const WeeklyReview({
    required this.weekOf,
    required this.weekEnd,
    this.complete = false,
    this.targets,
    this.goals,
    this.sleep,
    this.standouts,
    this.insights,
    this.patterns,
  });

  factory WeeklyReview.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final targets = r.object('targets');
    final sleep = r.object('sleep');
    final epoch = DateTime.fromMillisecondsSinceEpoch(0);
    List<T>? listOrNull<T>(String key, T Function(Map<String, dynamic>) f) =>
        r.raw(key) is List ? [for (final x in r.objects(key)) f(x)] : null;
    return WeeklyReview(
      weekOf: r.dateTime('weekOf') ?? epoch,
      weekEnd: r.dateTime('weekEnd') ?? epoch,
      complete: r.boolean('complete'),
      targets: targets == null ? null : ReviewTargets.fromJson(targets),
      goals: listOrNull('goals', ReviewGoal.fromJson),
      sleep: sleep == null ? null : ReviewSleep.fromJson(sleep),
      standouts: listOrNull('standouts', ReviewStandout.fromJson),
      insights: listOrNull('insights', Insight.fromJson),
      patterns: listOrNull('patterns', PersonalPattern.fromJson),
    );
  }
}
