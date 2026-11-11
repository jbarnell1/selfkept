/// JSON fixtures for goals, targets, the day log, medicines, strength and
/// Progress, in the server's camelCase or, with [pascal], PascalCase.
abstract final class BodyFixtures {
  static Map<String, dynamic> _case(Map<String, dynamic> map, bool pascal) {
    if (!pascal) return map;
    return {
      for (final e in map.entries)
        e.key[0].toUpperCase() + e.key.substring(1): _deep(e.value),
    };
  }

  static dynamic _deep(dynamic value) {
    if (value is Map<String, dynamic>) return _case(value, true);
    if (value is List) return [for (final v in value) _deep(v)];
    return value;
  }

  static Map<String, dynamic> goal({
    bool pascal = false,
    String id = 'g1',
    String slot = 'main',
    String kind = 'lose_fat',
    String title = 'Lose fat',
    String? metricKey = 'weight_kg',
    double? startValue = 86,
    double? targetValue = 80,
    double? pacePerWeek = -0.5,
    String status = 'active',
  }) => _case({
    'id': id,
    'slot': slot,
    'kind': kind,
    'title': title,
    'metricKey': metricKey,
    'startValue': startValue,
    'targetValue': targetValue,
    'pacePerWeek': pacePerWeek,
    'startedOn': '2026-09-01',
    'targetDate': '2027-03-01',
    'status': status,
    'endedAt': status == 'active' ? null : '2026-11-09T10:00:00Z',
  }, pascal);

  static Map<String, dynamic> target({
    bool pascal = false,
    String id = 't1',
    String? goalId = 'g1',
    String label = 'Protein',
    String kind = 'metric',
    String? metricKey = 'protein_g',
    String? tag,
    String comparator = 'at_least',
    double value = 130,
    String period = 'day',
    bool active = true,
    int sortOrder = 0,
    String unit = 'g',
  }) => _case({
    'id': id,
    'goalId': goalId,
    'label': label,
    'kind': kind,
    'metricKey': metricKey,
    'tag': tag,
    'comparator': comparator,
    'value': value,
    'period': period,
    'active': active,
    'sortOrder': sortOrder,
    'unit': unit,
  }, pascal);

  static Map<String, dynamic> targetTally({
    bool pascal = false,
    Map<String, dynamic>? target,
    double? current = 86,
    bool? met,
    List<bool?> week = const [true, false, null, true, true, null, null],
    int metDays = 3,
    int knownDays = 4,
  }) => _case({
    'target': target ?? BodyFixtures.target(),
    'current': current,
    'met': met,
    'week': week,
    'metDays': metDays,
    'knownDays': knownDays,
  }, pascal);

  static Map<String, dynamic> tally({
    bool pascal = false,
    List<Map<String, dynamic>>? goals,
    List<Map<String, dynamic>>? targets,
  }) => _case({
    'day': '2026-11-10',
    'goals': goals ?? [goal()],
    'targets': targets ?? [targetTally()],
  }, pascal);

  static Map<String, dynamic> journalEntry({
    bool pascal = false,
    String id = 'j1',
    String kind = 'food',
    String? text = 'Oats and berries',
    List<String> tags = const ['high_protein'],
    String? portion = 'usual',
  }) => _case({
    'id': id,
    'at': '2026-11-10T08:10:00Z',
    'day': '2026-11-10',
    'kind': kind,
    'text': text,
    'tags': tags,
    'portion': portion,
    'source': text == null ? 'chip' : 'typed',
    'messageId': null,
  }, pascal);

  static List<Map<String, dynamic>> tagGroups({bool pascal = false}) => [
    _case({
      'kind': 'food',
      'tags': [
        {'tag': 'high_protein', 'label': 'High protein'},
        {'tag': 'vegetables', 'label': 'Vegetables'},
      ],
    }, pascal),
    _case({
      'kind': 'drink',
      'tags': [
        {'tag': 'late_caffeine', 'label': 'Late caffeine'},
        {'tag': 'alcohol', 'label': 'Alcohol'},
      ],
    }, pascal),
  ];

  static Map<String, dynamic> medication({
    bool pascal = false,
    String id = 'm1',
    String kind = 'supplement',
    String? brand = 'Acme',
    String product = 'Daily Multi',
    bool active = true,
    String? stoppedOn,
    List<Map<String, dynamic>>? ingredients,
    List<String> substances = const ['vitamin_d', 'zinc'],
  }) => _case({
    'id': id,
    'kind': kind,
    'brand': brand,
    'product': product,
    'dose': '1 tablet',
    'scheduleNote': 'with breakfast',
    'generic': false,
    'schedule': 'daily',
    'ingredients':
        ingredients ??
        [
          {'name': 'Vitamin D3', 'amount': 25, 'unit': 'mcg'},
          {'name': 'Zinc', 'amount': 10, 'unit': 'mg'},
        ],
    'substances': substances,
    'startedOn': '2026-09-01',
    'stoppedOn': stoppedOn,
    'active': active,
  }, pascal);

  static Map<String, dynamic> strengthSet({
    bool pascal = false,
    String id = 's1',
    String day = '2026-11-08',
    double weightKg = 100,
    int reps = 5,
    double? rpe = 8.5,
  }) => _case({
    'id': id,
    'day': day,
    'lift': 'squat',
    'liftName': 'Squat',
    'weightKg': weightKg,
    'reps': reps,
    'rpe': rpe,
    'estimatedMaxKg': 116.7,
  }, pascal);

  static Map<String, dynamic> strengthLift({bool pascal = false}) => _case({
    'lift': 'squat',
    'liftName': 'Squat',
    'sets': [
      strengthSet(id: 's0', day: '2026-10-01', weightKg: 90),
      strengthSet(),
    ],
    'bestEstimatedMaxKg': 116.7,
    'changePct': 11.1,
  }, pascal);

  static Map<String, dynamic> measurement({
    bool pascal = false,
    String id = 'w1',
    String metricKey = 'weight_kg',
    double value = 82.5,
  }) => _case({
    'id': id,
    'metricKey': metricKey,
    'value': value,
    'unit': metricKey == 'weight_kg' ? 'kg' : 'cm',
    'at': '2026-11-10T07:00:00Z',
  }, pascal);

  static Map<String, dynamic> goalProgress({
    bool pascal = false,
    Map<String, dynamic>? goal,
    String paceStatus = 'on_track',
    String summary =
        '84.2 kg now, aiming for 80.0 kg. Moving 0.4 kg down a week over '
        'the last four weeks.',
  }) => _case({
    'goal': goal ?? BodyFixtures.goal(),
    'unit': 'kg',
    'current': 84.2,
    'fraction': 0.3,
    'actualPacePerWeek': -0.4,
    'paceStatus': paceStatus,
    'projectedDate': '2027-03-15',
    'summary': summary,
  }, pascal);

  static Map<String, dynamic> suggestion({bool pascal = false}) => _case({
    'kind': 'lose_fat',
    'title': 'Lose fat',
    'why':
        'A review written for lean, trained people suggests losing about '
        '0.5 to 1 % of body weight a week.',
    'metricKey': 'weight_kg',
    'startValue': 84,
    'targets': [
      {
        'label': 'Protein',
        'kind': 'metric',
        'metricKey': 'protein_g',
        'tag': null,
        'comparator': 'at_least',
        'value': 135,
        'period': 'day',
        'unit': 'g',
      },
    ],
  }, pascal);

  static Map<String, dynamic> progress({
    bool pascal = false,
    List<Map<String, dynamic>>? goals,
    List<Map<String, dynamic>>? suggestions,
    List<Map<String, dynamic>>? strength,
    bool body = true,
  }) => _case({
    'day': '2026-11-10',
    'goals': goals ?? [goalProgress()],
    'targets': [targetTally()],
    'body': body
        ? {
            'weightKg': 84.0,
            'trendWeightKg': 84.2,
            'weeklyRateKg': -0.4,
            'weeklyRatePct': -0.48,
            'bodyFatPct': 24.0,
            'fatMassKg': 20.2,
            'leanMassKg': 64.0,
            'bmi': 26.0,
            'leanMassIndex': 19.8,
            'waistCm': 90.0,
            'waistToHeight': 0.5,
            'leanShareOfLoss': null,
            'heightCm': 180.0,
            'readingsSetAside': 1,
            'lastWeighedOn': '2026-11-10',
          }
        : {'readingsSetAside': 0},
    'series': body
        ? [
            {
              'key': 'weight_kg',
              'displayName': 'Weight',
              'unit': 'kg',
              'points': [
                {'day': '2026-10-01', 'value': 86.0},
                {'day': '2026-10-15', 'value': 85.1},
                {'day': '2026-11-10', 'value': 84.0},
              ],
              'trend': [
                {'day': '2026-10-01', 'value': 86.0},
                {'day': '2026-10-15', 'value': 85.4},
                {'day': '2026-11-10', 'value': 84.2},
              ],
              'latest': 84.0,
              'change': -1.8,
            },
            {
              'key': 'waist_cm',
              'displayName': 'Waist',
              'unit': 'cm',
              'points': [
                {'day': '2026-10-01', 'value': 92.0},
                {'day': '2026-11-10', 'value': 90.0},
              ],
              'trend': null,
              'latest': 90.0,
              'change': -2.0,
            },
          ]
        : [],
    'strength': strength ?? [strengthLift()],
    'suggestions': suggestions ?? [],
  }, pascal);
}
