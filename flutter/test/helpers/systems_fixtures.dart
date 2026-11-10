import 'package:selfkept/providers/systems_scope.dart';

import 'fake_api_client.dart';
import 'pump.dart';

/// Every key of every map turned to PascalCase, for the casing tests.
dynamic pascalize(dynamic value) {
  if (value is Map) {
    return {
      for (final e in value.entries)
        (e.key as String).isEmpty
            ? e.key
            : (e.key as String)[0].toUpperCase() +
                  (e.key as String).substring(1): pascalize(
          e.value,
        ),
    };
  }
  if (value is List) return [for (final v in value) pascalize(v)];
  return value;
}

/// Signs in and tells the app the server has [features].
Future<void> signInWithFeatures(
  TestHarness h, {
  List<String> features = const [
    SystemsFeatures.contextPatterns,
    SystemsFeatures.labsReview,
  ],
}) async {
  h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
  await h.auth.login(email: 'a@b', password: 'x');
  h.api.on('GET', '/system/setup-status', (_) => Fixtures.setupStatus());
  h.api.on(
    'GET',
    '/system/version',
    (_) => {...Fixtures.version(), 'features': features},
  );
  await h.startup.run();
  h.api.on('GET', '/household', (_) => Fixtures.household());
  h.api.on('GET', '/household/shares', (_) => <Map<String, dynamic>>[]);
  h.api.on('GET', '/household/invites', (_) => <Map<String, dynamic>>[]);
  h.api.on('GET', '/household/shared-with-me', (_) => <Map<String, dynamic>>[]);
}

/// Charles shares with Ada.
void sharesFromCharles(TestHarness h, {List<String> categories = const []}) {
  h.api.on(
    'GET',
    '/household/shared-with-me',
    (_) => [
      {'personId': 'p2', 'displayName': 'Charles', 'categories': categories},
    ],
  );
}

/// The query of the last call to [key], for example "GET /systems".
Map<String, dynamic>? lastQuery(FakeApiClient api, String key) {
  final i = api.calls.lastIndexOf(key);
  if (i < 0) return null;
  final body = api.bodies[i];
  return body is Map<String, dynamic> ? body : null;
}

abstract final class SystemsFixtures {
  static Map<String, dynamic> insight({
    String id = 'i9',
    String kind = 'rhr_elevated',
    String severity = 'watch',
    String text = 'Resting heart rate is above your usual range.',
    Map<String, dynamic> evidence = const {},
  }) => {
    'id': id,
    'day': '2026-11-10',
    'kind': kind,
    'severity': severity,
    'metrics': ['resting_hr_bpm'],
    'evidence': evidence,
    'text': text,
    'generated': false,
    'shownAt': null,
    'dismissedAt': null,
    'reaction': null,
    'metricNames': ['Resting heart rate'],
  };

  static Map<String, dynamic> reading({bool detail = false}) => {
    'metric': 'resting_hr_bpm',
    'displayName': 'Resting heart rate',
    'unit': 'bpm',
    'category': 'vitals',
    'latestDay': '2026-11-10',
    'latestValue': 66,
    'usual': {'median28': 57.5, 'p25': 56.5, 'p75': 58.5, 'days': 28},
    'band': 'above',
    'recent': detail
        ? [
            for (var i = 1; i <= 28; i++)
              {
                'day': '2026-10-${i.toString().padLeft(2, '0')}',
                'value': 56 + (i % 3),
              },
          ]
        : null,
  };

  static Map<String, dynamic> lab({bool detail = false}) => {
    'metric': 'lab.ldl',
    'displayName': 'LDL cholesterol',
    'unit': 'mg/dL',
    'value': 160,
    'collectedOn': '2026-10-21',
    'flag': 'high',
    'previousValue': 171,
    'previousOn': '2026-07-20',
    'history': detail
        ? [
            {'collectedOn': '2026-07-20', 'value': 171, 'flag': 'high'},
            {'collectedOn': '2026-10-21', 'value': 160, 'flag': 'high'},
          ]
        : null,
  };

  static Map<String, dynamic> system({
    String key = 'heart',
    String name = 'Heart and fitness',
    String state =
        'One card worth a look. Resting heart rate is above your usual range.',
    bool detail = false,
  }) => {
    'key': key,
    'name': name,
    'state': state,
    'readings': [reading(detail: detail)],
    'labs': [lab(detail: detail)],
    'insights': [insight()],
  };

  static Map<String, dynamic> systems({bool withLabs = true}) => {
    'day': '2026-11-10',
    'systems': [
      system(),
      {
        'key': 'sleep',
        'name': 'Sleep',
        'state': 'Sleep is in its usual place.',
        'readings': <Map<String, dynamic>>[],
        'labs': <Map<String, dynamic>>[],
        'insights': <Map<String, dynamic>>[],
      },
    ],
    'labs': withLabs
        ? {
            'latestPanelOn': '2026-10-21',
            'panels': 3,
            'markers': 22,
            'outsideRange': 2,
          }
        : null,
  };

  static Map<String, dynamic> event({
    String id = 'e1',
    String kind = 'illness',
    String label = 'Illness',
    String startedOn = '2026-11-08',
    String? endedOn,
    String? note = 'Streaming cold',
    bool active = true,
  }) => {
    'id': id,
    'kind': kind,
    'label': label,
    'startedOn': startedOn,
    'endedOn': endedOn,
    'source': 'user',
    'note': note,
    'active': active,
  };

  static Map<String, dynamic> context({
    List<Map<String, dynamic>>? active,
    List<Map<String, dynamic>>? events,
  }) => {
    'day': '2026-11-10',
    'from': '2026-10-12',
    'to': '2026-11-10',
    'active':
        active ??
        [
          {
            'kind': 'illness',
            'label': 'Illness',
            'since': '2026-11-08',
            'until': null,
            'source': 'user',
            'reason': 'You noted an illness on Nov 8, 2026',
            'eventId': 'e1',
            'detail': null,
          },
          {
            'kind': 'medication_change',
            'label': 'Medication change',
            'since': '2026-11-01',
            'until': null,
            'source': 'derived',
            'reason': 'You started Creatine on Nov 1, 2026',
            'eventId': null,
            'detail': 'started',
          },
        ],
    'events': events ?? [event()],
  };

  static Map<String, dynamic> pattern() => {
    'tag': 'alcohol',
    'tagLabel': 'Alcohol',
    'metric': 'resting_hr_bpm',
    'metricName': 'Resting heart rate',
    'unit': 'bpm',
    'nightsWith': 9,
    'nightsWithout': 31,
    'medianWith': 62,
    'medianWithout': 58,
    'difference': 4,
    'rangeWith': {'low': 60, 'high': 65},
    'rangeWithout': {'low': 56, 'high': 59},
    'overlap': 0.22,
    'noiseLine': 1.5,
    'weekendNights': 6,
    'contextNights': 0,
    'sentence':
        'On 9 nights after alcohol, against 31 without, your resting heart rate ran 4 bpm higher. A pattern in your own data, not proof.',
  };

  static Map<String, dynamic> patterns({bool empty = false}) => {
    'day': '2026-11-10',
    'from': '2026-07-13',
    'windowDays': 120,
    'minNightsWith': 8,
    'minNightsWithout': 16,
    'note': 'A pattern in your own data, not proof.',
    'patterns': empty ? <Map<String, dynamic>>[] : [pattern()],
    'building': empty
        ? <Map<String, dynamic>>[]
        : [
            {
              'tag': 'late_meal',
              'tagLabel': 'Late meal',
              'nightsWith': 3,
              'nightsWithout': 40,
            },
          ],
  };

  static Map<String, dynamic> sleep({bool empty = false}) => {
    'from': '2026-10-28',
    'to': '2026-11-10',
    'nights': empty
        ? <Map<String, dynamic>>[]
        : [
            {
              'day': '2026-11-08',
              'bedtime': '2026-11-07T23:10:00Z',
              'wakeTime': '2026-11-08T06:20:00Z',
              'inBedMinutes': 430,
              'asleepMinutes': 410,
              'awakeMinutes': null,
              'lightMinutes': null,
              'deepMinutes': null,
              'remMinutes': null,
              'unspecifiedMinutes': 410,
              'hasStages': false,
            },
            {
              'day': '2026-11-09',
              'bedtime': '2026-11-08T23:00:00Z',
              'wakeTime': '2026-11-09T06:30:00Z',
              'inBedMinutes': 450,
              'asleepMinutes': 435,
              'awakeMinutes': 15,
              'lightMinutes': 120,
              'deepMinutes': 120,
              'remMinutes': 195,
              'unspecifiedMinutes': null,
              'hasStages': true,
            },
          ],
  };

  static Map<String, dynamic> workouts({bool empty = false}) => {
    'from': '2026-10-14',
    'to': '2026-11-10',
    'workouts': empty
        ? <Map<String, dynamic>>[]
        : [
            {
              'id': 'w1',
              'type': 'running',
              'group': 'cardio',
              'day': '2026-11-09',
              'startAt': '2026-11-09T09:00:00Z',
              'endAt': '2026-11-09T09:45:00Z',
              'durationMinutes': 45,
              'distanceM': 7200,
              'energyKcal': 480,
              'avgHr': 150,
              'maxHr': 171,
            },
          ],
    'weeks': empty
        ? <Map<String, dynamic>>[]
        : [
            {
              'weekOf': '2026-11-09',
              'count': 2,
              'minutes': 105,
              'strength': 1,
              'cardio': 1,
              'mixed': 0,
            },
          ],
  };

  static Map<String, dynamic> review({
    String weekOf = '2026-11-02',
    String weekEnd = '2026-11-08',
    bool shared = true,
  }) => {
    'weekOf': weekOf,
    'weekEnd': weekEnd,
    'complete': true,
    'targets': shared
        ? {
            'kept': [
              {
                'targetId': 't1',
                'label': 'Sleep',
                'period': 'day',
                'metDays': 6,
                'knownDays': 7,
                'met': true,
              },
            ],
            'missed': <Map<String, dynamic>>[],
            'unknown': <Map<String, dynamic>>[],
          }
        : null,
    'goals': shared
        ? [
            {
              'goalId': 'g1',
              'slot': 'main',
              'kind': 'lose_fat',
              'title': 'Lose fat',
              'metric': 'weight_kg',
              'unit': 'kg',
              'first': 82.4,
              'last': 81.9,
              'change': -0.5,
              'readings': 2,
              'summary': '82.4 kg to 81.9 kg over 2 readings, 0.5 kg down.',
            },
          ]
        : null,
    'sleep': {
      'nights': 7,
      'medianMinutes': 455,
      'previousMedianMinutes': 448,
      'nightsSevenHoursOrMore': 7,
      'summary': '7 nights recorded, a median of 7 h 35 min.',
    },
    'standouts': shared
        ? [
            {
              'metric': 'resting_hr_bpm',
              'displayName': 'Resting heart rate',
              'unit': 'bpm',
              'weekMedian': 63,
              'usualMedian': 57,
              'difference': 6,
              'direction': 'up',
              'summary':
                  'Resting heart rate ran higher than the four weeks before.',
            },
          ]
        : null,
    'insights': shared ? [insight()] : null,
    'patterns': shared ? [pattern()] : null,
  };

  static Map<String, dynamic> conditions({
    String fasting = 'yes',
    int? fastingHours = 8,
    String? drawTime,
    String drawPeriod = 'morning',
    bool defaulted = true,
  }) => {
    'fasting': fasting,
    'fastingHours': fastingHours,
    'drawTime': drawTime,
    'drawPeriod': drawPeriod,
    'hardTraining': null,
    'unwell': null,
    'defaulted': defaulted,
  };

  static Map<String, dynamic> trial({
    String id = 'tr1',
    String title = 'Oats instead of a bagel',
    String status = 'active',
  }) => {
    'id': id,
    'title': title,
    'change': 'Oats at breakfast',
    'startedOn': '2026-08-16',
    'readOn': '2026-12-12',
    'metricKeys': ['lab.ldl', 'resting_hr_bpm'],
    'status': status,
    'createdAt': '2026-08-16T08:00:00Z',
    'updatedAt': '2026-08-16T08:00:00Z',
  };

  static Map<String, dynamic> labReview({String id = 'r2'}) => {
    'reportId': id,
    'personId': 'p1',
    'collectedOn': '2026-12-12',
    'previousReportId': 'r1',
    'previousCollectedOn': '2026-08-14',
    'conditions': conditions(
      fasting: 'no',
      fastingHours: null,
      drawTime: '15:40',
      drawPeriod: 'afternoon',
      defaulted: false,
    ),
    'previousConditions': conditions(),
    'conditionsDiffer': [
      {'condition': 'fasting', 'now': 'no', 'then': 'yes'},
      {'condition': 'drawPeriod', 'now': 'afternoon', 'then': 'morning'},
    ],
    'markers': [
      {
        'metricKey': 'lab.ggt',
        'displayName': 'GGT',
        'unit': 'U/L',
        'value': 28,
        'previousValue': 40,
        'change': -12,
        'changePercent': -30,
        'rcvPercent': 24.7,
        'beyondRcv': true,
        'moved': 'down',
        'wanted': 'down',
        'verdict': 'working',
        'reasons': <String>[],
        'readWithCare': <Map<String, dynamic>>[],
        'variation': {
          'cviPercent': 8.9,
          'source': 'Carobene 2017, Clin Chem 63(6):1141-1150 (EuBIVAS)',
          'sourceUrl': 'https://pubmed.ncbi.nlm.nih.gov/28428356/',
          'caveat': null,
        },
        'prep': <String>[],
        'retest': null,
      },
      {
        'metricKey': 'lab.triglycerides',
        'displayName': 'Triglycerides',
        'unit': 'mg/dL',
        'value': 168,
        'previousValue': 120,
        'change': 48,
        'changePercent': 40,
        'rcvPercent': null,
        'beyondRcv': null,
        'moved': 'up',
        'wanted': 'down',
        'verdict': 'cannot_tell',
        'reasons': ['draw_conditions_differ', 'no_variation_figure'],
        'readWithCare': [
          {
            'linkId': 'not_fasting_triglycerides',
            'from': 'condition:not_fasting',
            'explanation': 'After a usual meal triglycerides read higher.',
            'source': 'Nordestgaard 2016',
          },
        ],
        'variation': null,
        'prep': [
          'Draw the same way each time, fasting or not, so this compares with the last panel.',
        ],
        'retest': null,
      },
    ],
    'takingNow': [
      {
        'id': 'm1',
        'kind': 'supplement',
        'brand': null,
        'product': 'Creatine monohydrate',
        'substances': ['creatine'],
      },
    ],
    'takingThen': <Map<String, dynamic>>[],
    'substancesNow': ['creatine'],
    'substancesThen': <String>[],
    'weight': {
      'now': {'day': '2026-12-13', 'kg': 80.5},
      'then': {'day': '2026-08-12', 'kg': 84},
      'changeKg': -3.5,
    },
    'trials': [
      {
        'trial': trial(),
        'markers': [
          {'metricKey': 'lab.ggt', 'verdict': 'working'},
        ],
        'sharedCredit': true,
      },
    ],
    'verdictCounts': {
      'working': 1,
      'right_direction': 0,
      'no_clear_change': 0,
      'not_working': 0,
      'cannot_tell': 1,
    },
  };

  static Map<String, dynamic> timeline({bool weightShared = true}) => {
    'personId': 'p1',
    'weightShared': weightShared,
    'panels': [
      {
        'reportId': 'r1',
        'collectedOn': '2026-08-14',
        'provider': 'rythm',
        'conditions': conditions(),
        'markerCount': 30,
        'weight': weightShared ? {'day': '2026-08-12', 'kg': 84} : null,
        'substances': ['creatine'],
      },
    ],
  };

  static Map<String, dynamic> related() => {
    'metricKey': 'lab.ferritin',
    'displayName': 'Ferritin',
    'related': [
      {
        'key': 'lab.hscrp',
        'kind': 'lab',
        'displayName': 'hs-CRP',
        'unit': 'mg/L',
        'category': 'labs',
        'shared': true,
        'latestValue': 4.2,
        'latestDate': '2026-10-24',
        'links': [
          {
            'id': 'ferritin_inflammation',
            'from': 'lab.hscrp',
            'to': 'lab.ferritin',
            'direction': 'same',
            'explanation': 'Ferritin rises with inflammation.',
            'source': 'WHO 2020',
            'sourceUrl': 'https://example.test/who',
            'evidence': 'guideline',
          },
        ],
      },
      {
        'key': 'resting_hr_bpm',
        'kind': 'metric',
        'displayName': 'Resting heart rate',
        'unit': 'bpm',
        'category': 'vitals',
        'shared': false,
        'links': <Map<String, dynamic>>[],
      },
    ],
  };

  static Map<String, dynamic> overview() => {
    'householdId': 'h1',
    'day': '2026-11-10',
    'members': [
      {
        'personId': 'p1',
        'displayName': 'Ada',
        'isSelf': true,
        'sharedCategories': [
          'activity',
          'sleep',
          'vitals',
          'body',
          'labs',
          'cycle',
          'checkins',
          'insights',
        ],
        'labs': {
          'latestCollectedOn': '2026-10-21',
          'panels': 3,
          'outOfRange': 2,
        },
        'body': {
          'weightKg': 64.2,
          'weighedOn': '2026-11-09',
          'mainGoal': 'Build muscle',
          'activeTrials': 1,
        },
        'sleep': {
          'metric': 'sleep_stage',
          'day': '2026-11-10',
          'value': 455,
          'unit': 'min',
        },
      },
      {
        'personId': 'p2',
        'displayName': 'Charles',
        'isSelf': false,
        'sharedCategories': ['activity'],
        'activity': {
          'metric': 'steps',
          'day': '2026-11-10',
          'value': 8421,
          'unit': 'count',
        },
      },
    ],
  };
}
