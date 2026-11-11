import '../utils/json_reader.dart';
import 'enums.dart';
import 'insight.dart';

class OverviewLabs {
  final DateTime? latestCollectedOn;
  final int panels;
  final int outOfRange;

  const OverviewLabs({
    this.latestCollectedOn,
    this.panels = 0,
    this.outOfRange = 0,
  });

  factory OverviewLabs.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return OverviewLabs(
      latestCollectedOn: r.dateTime('latestCollectedOn'),
      panels: r.integer('panels'),
      outOfRange: r.integer('outOfRange'),
    );
  }
}

class OverviewBody {
  final double? weightKg;
  final DateTime? weighedOn;
  final String? mainGoal;
  final int activeTrials;

  const OverviewBody({
    this.weightKg,
    this.weighedOn,
    this.mainGoal,
    this.activeTrials = 0,
  });

  factory OverviewBody.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return OverviewBody(
      weightKg: readDouble(r.raw('weightKg')),
      weighedOn: r.dateTime('weighedOn'),
      mainGoal: r.stringOrNull('mainGoal'),
      activeTrials: r.integer('activeTrials'),
    );
  }
}

/// The latest daily value of one metric.
class OverviewValue {
  final String metric;
  final DateTime? day;
  final double? value;
  final String unit;

  const OverviewValue({
    required this.metric,
    this.day,
    this.value,
    this.unit = '',
  });

  factory OverviewValue.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return OverviewValue(
      metric: r.string('metric'),
      day: r.dateTime('day'),
      value: readDouble(r.raw('value')),
      unit: r.string('unit'),
    );
  }
}

class OverviewInsights {
  final int cards;
  final InsightSeverity? topSeverity;

  const OverviewInsights({this.cards = 0, this.topSeverity});

  factory OverviewInsights.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final top = r.stringOrNull('topSeverity');
    return OverviewInsights(
      cards: r.integer('cards'),
      topSeverity: top == null ? null : InsightSeverity.fromString(top),
    );
  }
}

class OverviewCheckins {
  final DateTime? lastAt;
  final int? mood;

  const OverviewCheckins({this.lastAt, this.mood});

  factory OverviewCheckins.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final mood = r.raw('mood');
    return OverviewCheckins(
      lastAt: r.dateTime('lastAt'),
      mood: mood == null ? null : r.integer('mood'),
    );
  }
}

/// One member as the caller may see them. A summary that is absent was not
/// shared; the page shows nothing for it.
class MemberOverview {
  final String personId;
  final String displayName;
  final bool isSelf;
  final List<ShareCategory> sharedCategories;
  final OverviewLabs? labs;
  final OverviewBody? body;
  final OverviewValue? sleep;
  final OverviewValue? activity;
  final OverviewValue? vitals;
  final OverviewInsights? insights;
  final OverviewCheckins? checkins;

  const MemberOverview({
    required this.personId,
    this.displayName = '',
    this.isSelf = false,
    this.sharedCategories = const [],
    this.labs,
    this.body,
    this.sleep,
    this.activity,
    this.vitals,
    this.insights,
    this.checkins,
  });

  factory MemberOverview.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    T? part<T>(String key, T Function(Map<String, dynamic>) f) {
      final o = r.object(key);
      return o == null ? null : f(o);
    }

    return MemberOverview(
      personId: r.string('personId'),
      displayName: r.string('displayName'),
      isSelf: r.boolean('isSelf'),
      sharedCategories: [
        for (final c in r.strings('sharedCategories'))
          ?ShareCategory.fromString(c),
      ],
      labs: part('labs', OverviewLabs.fromJson),
      body: part('body', OverviewBody.fromJson),
      sleep: part('sleep', OverviewValue.fromJson),
      activity: part('activity', OverviewValue.fromJson),
      vitals: part('vitals', OverviewValue.fromJson),
      insights: part('insights', OverviewInsights.fromJson),
      checkins: part('checkins', OverviewCheckins.fromJson),
    );
  }
}

/// Answer of GET /household/overview: the caller first.
class HouseholdOverview {
  final String householdId;
  final DateTime? day;
  final List<MemberOverview> members;

  const HouseholdOverview({
    this.householdId = '',
    this.day,
    this.members = const [],
  });

  factory HouseholdOverview.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return HouseholdOverview(
      householdId: r.string('householdId'),
      day: r.dateTime('day'),
      members: [
        for (final m in r.objects('members')) MemberOverview.fromJson(m),
      ],
    );
  }
}
