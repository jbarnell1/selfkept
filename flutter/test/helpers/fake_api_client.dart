import 'dart:typed_data';

import 'package:selfkept/models/user.dart';
import 'package:selfkept/services/api_client.dart';
import 'package:selfkept/utils/api_error.dart';

/// Hand-written fake. Register handlers per "METHOD /path"; anything else
/// throws a 404 ApiException. Records every call for assertions.
class FakeApiClient implements ApiClient {
  final Map<String, dynamic Function(Object? data)> _handlers = {};
  final List<String> calls = [];
  final List<Object?> bodies = [];

  /// What [restoreSession] answers.
  bool restoreResult = false;
  AuthResponse? storedSession;
  int clearCount = 0;
  void Function()? unauthorizedCallback;

  @override
  String serverUrl = 'http://fake.test';

  void on(String method, String path, dynamic Function(Object? data) handler) {
    _handlers['${method.toUpperCase()} $path'] = handler;
  }

  /// Convenience: a handler that throws.
  void fail(String method, String path, ApiException error) {
    on(method, path, (_) => throw error);
  }

  Future<T> _dispatch<T>(
    String method,
    String path,
    Object? data,
    FromBody<T>? fromJson,
  ) async {
    final key = '$method $path';
    calls.add(key);
    bodies.add(data);
    final handler = _handlers[key];
    if (handler == null) {
      throw ApiException('No fake for $key', statusCode: 404);
    }
    final body = handler(data);
    if (body is Future) {
      final awaited = await body;
      return fromJson != null ? fromJson(awaited) : awaited as T;
    }
    if (fromJson != null) return fromJson(body);
    return body as T;
  }

  @override
  Future<T> get<T>(
    String path, {
    Map<String, dynamic>? queryParameters,
    FromBody<T>? fromJson,
  }) => _dispatch('GET', path, queryParameters, fromJson);

  /// Bytes registered with [onBytes], answered to [getBytes] by path.
  final Map<String, Uint8List Function(Object? query)> _byteHandlers = {};

  void onBytes(String path, Uint8List Function(Object? query) handler) {
    _byteHandlers['GET $path'] = handler;
  }

  @override
  Future<Uint8List> getBytes(
    String path, {
    Map<String, dynamic>? queryParameters,
  }) async {
    final key = 'GET $path';
    calls.add(key);
    bodies.add(queryParameters);
    final handler = _byteHandlers[key];
    if (handler != null) return handler(queryParameters);
    final json = _handlers[key];
    if (json != null) {
      final body = await json(queryParameters);
      if (body is Uint8List) return body;
    }
    throw ApiException('No fake for $key', statusCode: 404);
  }

  @override
  Future<T> post<T>(String path, {Object? data, FromBody<T>? fromJson}) =>
      _dispatch('POST', path, data, fromJson);

  @override
  Future<T> put<T>(String path, {Object? data, FromBody<T>? fromJson}) =>
      _dispatch('PUT', path, data, fromJson);

  @override
  Future<T> patch<T>(String path, {Object? data, FromBody<T>? fromJson}) =>
      _dispatch('PATCH', path, data, fromJson);

  @override
  Future<T> delete<T>(String path, {Object? data, FromBody<T>? fromJson}) =>
      _dispatch('DELETE', path, data, fromJson);

  @override
  Future<void> storeSession(AuthResponse response) async {
    storedSession = response;
  }

  @override
  Future<bool> restoreSession() async => restoreResult;

  @override
  Future<void> clearSession() async {
    clearCount += 1;
    storedSession = null;
  }

  @override
  set onUnauthorized(void Function()? callback) {
    unauthorizedCallback = callback;
  }

  /// Simulates the Dio interceptor giving up on a session.
  void fireUnauthorized() => unauthorizedCallback?.call();
}

/// JSON fixtures shared by tests.
abstract final class Fixtures {
  static Map<String, dynamic> person({bool pascal = false}) => _case({
    'id': 'p1',
    'displayName': 'Ada',
    'sexAtBirth': 'female',
    'birthDate': '1990-05-04',
    'units': 'metric',
    'timeZone': 'Europe/London',
  }, pascal);

  /// A finished setup by default; pass [setupDone] false for a new account.
  static Map<String, dynamic> user({
    bool pascal = false,
    String role = 'admin',
    List<String> setupPlan = const [],
    bool setupDone = true,
  }) => _case({
    'id': 'u1',
    'email': 'ada@example.test',
    'displayName': 'Ada',
    'role': role,
    'householdId': 'h1',
    'person': person(pascal: pascal),
    'setupPlan': setupPlan,
    'setupCompletedAt': setupDone ? '2026-10-29T08:00:00Z' : null,
  }, pascal);

  static Map<String, dynamic> authResponse({
    bool pascal = false,
    String role = 'admin',
    List<String> setupPlan = const [],
    bool setupDone = true,
  }) => _case({
    'accessToken': 'access-1',
    'accessTokenExpiresAt': '2030-01-01T00:00:00Z',
    'refreshToken': 'refresh-1',
    'user': user(
      pascal: pascal,
      role: role,
      setupPlan: setupPlan,
      setupDone: setupDone,
    ),
  }, pascal);

  static Map<String, dynamic> ouraApp({
    bool pascal = false,
    bool configured = false,
    String source = 'none',
    String? clientId,
    String? redirectUri = 'https://box.tailnet.ts.net/api/oura/callback',
  }) => _case({
    'configured': configured,
    'source': source,
    'clientId': clientId,
    'redirectUri': redirectUri,
    'developerUrl': 'https://cloud.ouraring.com/oauth/applications',
  }, pascal);

  static Map<String, dynamic> version({
    bool pascal = false,
    String v = '0.1.0',
  }) => _case({
    'version': v,
    'minClientVersion': '0.1.0',
    'brand': _case({
      'productName': 'Anything',
      'shortName': 'Any',
      'tagline': 'A line.',
      'supportUrl': 'https://example.test',
      'privacyUrl': 'https://example.test/privacy',
    }, pascal),
    'features': ['labs', 'mcp'],
  }, pascal);

  static Map<String, dynamic> setupStatus({
    bool pascal = false,
    bool needsFirstUser = false,
  }) => _case({
    'needsFirstUser': needsFirstUser,
    'registrationOpen': needsFirstUser,
  }, pascal);

  static Map<String, dynamic> household({bool pascal = false}) => _case({
    'id': 'h1',
    'name': 'The Lovelaces',
    'members': [
      _case({
        'personId': 'p1',
        'userId': 'u1',
        'displayName': 'Ada',
        'role': 'admin',
        'joinedAt': '2026-01-01T10:00:00Z',
      }, pascal),
      _case({
        'personId': 'p2',
        'userId': 'u2',
        'displayName': 'Charles',
        'role': 'member',
        'joinedAt': '2026-02-01T10:00:00Z',
      }, pascal),
    ],
  }, pascal);

  static Map<String, dynamic> source({
    bool pascal = false,
    String id = 's1',
    String kind = 'healthkit',
    String? cursor = 'anchor-1',
    int sampleCount = 120,
  }) => _case({
    'id': id,
    'kind': kind,
    'deviceName': "Ada's phone",
    'platform': 'ios',
    'enabled': true,
    'lastSyncAt': '2026-10-28T07:00:00Z',
    'lastSampleAt': '2026-10-28T06:58:00Z',
    'sampleCount': sampleCount,
    'cursor': cursor,
  }, pascal);

  static Map<String, dynamic> sample({bool pascal = false}) => _case({
    'id': 'smp1',
    'sourceUid': 'hk-1',
    'metric': 'heart_rate_bpm',
    'start': '2026-10-29T06:00:00Z',
    'end': null,
    'value': 58,
    'unit': 'bpm',
    'sourceId': 's1',
    'meta': {'motion': 'sedentary'},
  }, pascal);

  static Map<String, dynamic> workout({bool pascal = false}) => _case({
    'id': 'w1',
    'sourceUid': 'hk-w1',
    'type': 'running',
    'start': '2026-10-29T06:00:00Z',
    'end': '2026-10-29T06:50:00Z',
    'distanceM': 10020,
    'energyKcal': 612,
    'avgHr': 148,
    'maxHr': 171,
    'routeGeoJson': null,
    'meta': <String, dynamic>{},
  }, pascal);

  static Map<String, dynamic> checkin({
    bool pascal = false,
    String id = 'c1',
    int mood = 5,
    String? note = 'slept well',
  }) => _case({
    'id': id,
    'personId': 'p1',
    'at': '2026-10-29T08:00:00Z',
    'mood': mood,
    'note': note,
    'tags': ['morning'],
  }, pascal);

  static Map<String, dynamic> ingestResult({
    bool pascal = false,
    int accepted = 2,
    String? cursor = 'anchor-2',
  }) => _case({
    'accepted': accepted,
    'duplicates': 0,
    'rejected': <Map<String, dynamic>>[],
    'cursor': cursor,
  }, pascal);

  static Map<String, dynamic> insight({
    bool pascal = false,
    String id = 'i1',
    String day = '2026-10-29',
    String kind = 'hrv_low_run',
    String severity = 'watch',
    List<String> metrics = const ['hrv_sdnn_ms'],
    String text =
        'HRV has sat below your usual range for three nights. That often follows poor sleep, a hard training block or the start of an illness.',
    bool generated = false,
    String? dismissedAt,
    String? reaction,
  }) => _case({
    'id': id,
    'day': day,
    'kind': kind,
    'severity': severity,
    'metrics': metrics,
    'evidence': {'nights': 3, 'p10': 31.5},
    'text': text,
    'generated': generated,
    'shownAt': '2026-10-29T06:00:00Z',
    'dismissedAt': dismissedAt,
    'reaction': reaction,
  }, pascal);

  static Map<String, dynamic> today({
    bool pascal = false,
    String sentence =
        'You slept 7 h 40, a little under your usual. HRV is inside your range and resting heart rate is in its usual place.',
    bool learning = false,
    List<Map<String, dynamic>>? cards,
    num? sleepMinutes = 460,
    String? hrv = 'inside',
    String? restingHr = 'inside',
    String? sleep = 'below',
  }) => _case({
    'day': '2026-10-29',
    'sentence': sentence,
    'generated': false,
    'sleepMinutes': sleepMinutes,
    'readiness': _case({
      'hrv': hrv,
      'restingHr': restingHr,
      'sleep': sleep,
    }, pascal),
    'cards':
        cards ??
        [
          insight(pascal: pascal),
          insight(
            pascal: pascal,
            id: 'i2',
            kind: 'sleep_good_week',
            severity: 'notice',
            metrics: const ['sleep_stage'],
            text: 'A better week of sleep than your usual.',
          ),
        ],
    'lastCheckin': checkin(pascal: pascal),
    'learning': learning,
  }, pascal);

  static Map<String, dynamic> trendMetric({
    bool pascal = false,
    String metric = 'sleep_stage',
    String unit = 'min',
    String displayName = 'Sleep',
    num? latestValue = 460,
    String? band = 'below',
    int baselineDays = 28,
    bool withRecent = true,
  }) => _case({
    'metric': metric,
    'unit': unit,
    'displayName': displayName,
    'latestValue': latestValue,
    'latestDay': '2026-10-29',
    'band': band,
    'baselineDays': baselineDays,
    'recent': [
      if (withRecent)
        for (var i = 27; i >= 0; i--)
          _case({
            'day': _isoDay(DateTime(2026, 10, 29).subtract(Duration(days: i))),
            'value': (latestValue ?? 0) + (i % 4) * 3 - (i == 0 ? 0 : 1),
          }, pascal),
    ],
    'range': band == null
        ? null
        : _case({
            'median28': (latestValue ?? 0) + 5,
            'p25': (latestValue ?? 0) - 2,
            'p75': (latestValue ?? 0) + 12,
            'p10': (latestValue ?? 0) - 8,
            'p90': (latestValue ?? 0) + 20,
          }, pascal),
  }, pascal);

  static String _isoDay(DateTime d) =>
      '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

  /// `{ metrics: [...] }` in a deliberately unsorted order.
  static Map<String, dynamic> trends({bool pascal = false}) => _case({
    'metrics': [
      trendMetric(
        pascal: pascal,
        metric: 'steps',
        unit: 'count',
        displayName: 'Steps',
        latestValue: 8421,
        band: 'inside',
      ),
      trendMetric(pascal: pascal),
      trendMetric(
        pascal: pascal,
        metric: 'resting_hr_bpm',
        unit: 'bpm',
        displayName: 'Resting heart rate',
        latestValue: 58,
        band: 'inside',
      ),
    ],
  }, pascal);

  static Map<String, dynamic> trendSeries({
    bool pascal = false,
    String metric = 'sleep_stage',
    String unit = 'min',
    int days = 28,
    bool withBand = true,
  }) {
    final end = DateTime(2026, 10, 29);
    final points = <Map<String, dynamic>>[];
    for (var i = days - 1; i >= 0; i--) {
      final day = end.subtract(Duration(days: i));
      final value = 430 + (i % 5) * 12 + (i == 3 ? -120 : 0);
      points.add(
        _case({
          'day':
              '${day.year}-${day.month.toString().padLeft(2, '0')}-${day.day.toString().padLeft(2, '0')}',
          'value': value,
          'min': value - 20,
          'max': value + 25,
        }, pascal),
      );
    }
    return _case({
      'metric': metric,
      'unit': unit,
      'points': points,
      'band': withBand
          ? _case({
              'median28': 452,
              'p25': 436,
              'p75': 470,
              'p10': 420,
              'p90': 485,
            }, pascal)
          : null,
      'baselineDays': withBand ? 28 : 9,
    }, pascal);
  }

  static Map<String, dynamic> labPoint({
    bool pascal = false,
    String date = '2026-10-24',
    num value = 92,
    String flag = 'none',
    bool derived = false,
    String reportId = 'r1',
  }) => _case({
    'date': date,
    'value': value,
    'flag': flag,
    'derived': derived,
    'reportId': reportId,
  }, pascal);

  static Map<String, dynamic> labMarker({
    bool pascal = false,
    String metricKey = 'lab.apob',
    String displayName = 'ApoB',
    String unit = 'mg/dL',
    String group = 'heart',
    bool applicable = true,
    String goodDirection = 'down',
    num? latestValue = 92,
    String? latestDate = '2026-10-24',
    String? latestFlag = 'high',
    num? previousValue = 78,
    String? previousDate = '2026-09-20',
    Map<String, dynamic>? populationRange = const {'low': 40, 'high': 90},
    Map<String, dynamic>? optimalRange = const {'high': 80},
    String? tier = 'watch',
    String? position = 'above',
    String? severity = 'watch',
    List<Map<String, dynamic>>? lastThree,
  }) => _case({
    'metricKey': metricKey,
    'displayName': displayName,
    'unit': unit,
    'group': group,
    'sexApplicability': 'both',
    'applicable': applicable,
    'goodDirection': goodDirection,
    'latestValue': latestValue,
    'latestDate': latestDate,
    'latestFlag': latestFlag,
    'previousValue': previousValue,
    'previousDate': previousDate,
    'populationRange': populationRange,
    'optimalRange': optimalRange,
    'tier': tier,
    'position': position,
    'severity': severity,
    'lastThree':
        lastThree ??
        (latestValue == null
            ? <Map<String, dynamic>>[]
            : [
                labPoint(pascal: pascal, date: '2026-08-20', value: 85),
                labPoint(
                  pascal: pascal,
                  date: '2026-09-20',
                  value: previousValue ?? 80,
                ),
                labPoint(
                  pascal: pascal,
                  date: latestDate ?? '2026-10-24',
                  value: latestValue,
                  flag: 'high',
                ),
              ]),
  }, pascal);

  /// A small panel in panel order: a clean marker, one with no result, a
  /// watch, a clinician, a ratio, one that does not apply.
  static List<Map<String, dynamic>> labMarkers({bool pascal = false}) => [
    labMarker(
      pascal: pascal,
      metricKey: 'lab.testosterone',
      displayName: 'Testosterone',
      unit: 'ng/dL',
      group: 'hormones',
      goodDirection: 'range',
      latestValue: 520,
      previousValue: 500,
      populationRange: const {'low': 264, 'high': 916},
      optimalRange: null,
      tier: null,
      position: 'inside',
      severity: null,
    ),
    labMarker(
      pascal: pascal,
      metricKey: 'lab.estradiol',
      displayName: 'Estradiol',
      unit: 'pg/mL',
      group: 'hormones',
      applicable: false,
      latestValue: null,
      latestDate: null,
      previousValue: null,
      populationRange: null,
      optimalRange: null,
      tier: null,
      position: null,
      severity: null,
    ),
    labMarker(
      pascal: pascal,
      metricKey: 'lab.tsh',
      displayName: 'TSH',
      unit: 'uIU/mL',
      group: 'thyroid',
      latestValue: null,
      latestDate: null,
      latestFlag: null,
      previousValue: null,
      previousDate: null,
      populationRange: const {'low': 0.4, 'high': 4.5},
      optimalRange: null,
      tier: null,
      position: null,
      severity: null,
    ),
    labMarker(pascal: pascal),
    labMarker(
      pascal: pascal,
      metricKey: 'lab.hscrp',
      displayName: 'hs-CRP',
      unit: 'mg/L',
      latestValue: 4.2,
      previousValue: 1.1,
      populationRange: const {'high': 3},
      optimalRange: null,
      tier: 'clinician',
      position: 'above',
      severity: 'clinician',
    ),
    labMarker(
      pascal: pascal,
      metricKey: 'lab.tc_hdl_ratio',
      displayName: 'TC/HDL ratio',
      unit: 'ratio',
      latestValue: 3.4,
      previousValue: 3.4,
      populationRange: const {'high': 5},
      optimalRange: null,
      tier: null,
      position: 'inside',
      severity: null,
    ),
    labMarker(
      pascal: pascal,
      metricKey: 'lab.glucose',
      displayName: 'Glucose',
      unit: 'mg/dL',
      group: 'metabolic',
      latestValue: 88,
      previousValue: 91,
      populationRange: const {'low': 70, 'high': 99},
      optimalRange: null,
      tier: null,
      position: 'inside',
      severity: null,
    ),
  ];

  static Map<String, dynamic> labResult({
    bool pascal = false,
    String id = 'x1',
    String metricKey = 'lab.apob',
    String displayName = 'ApoB',
    num value = 92,
    String unit = 'mg/dL',
    String flag = 'high',
    bool derived = false,
  }) => _case({
    'id': id,
    'metricKey': metricKey,
    'displayName': displayName,
    'value': value,
    'unit': unit,
    'flag': flag,
    'rawText': 'ApoB 92 mg/dL',
    'derived': derived,
    'referenceLow': 40,
    'referenceHigh': 90,
  }, pascal);

  static Map<String, dynamic> labReport({
    bool pascal = false,
    String id = 'r1',
    String provider = 'rythm',
    String collectedOn = '2026-10-24',
    int results = 2,
  }) => _case({
    'id': id,
    'personId': 'p1',
    'provider': provider,
    'collectedOn': collectedOn,
    'receivedOn': '2026-10-26',
    'hasImage': true,
    'notes': 'Fasted',
    'createdAt': '2026-10-26T09:00:00Z',
    'confirmedAt': '2026-10-26T09:00:00Z',
    'results': [
      for (var i = 0; i < results; i++)
        labResult(pascal: pascal, id: 'x$i', metricKey: 'lab.k$i'),
    ],
  }, pascal);

  static Map<String, dynamic> labMarkerDetail({
    bool pascal = false,
    Map<String, dynamic>? marker,
    String blurb = 'ApoB counts the particles that carry cholesterol.',
    String whatMovesIt = 'Saturated fat, body weight and some medicines.',
    String? sourceUrl = 'https://example.test/apob',
    List<Map<String, dynamic>>? history,
  }) => _case({
    'marker':
        marker ??
        labMarker(
          pascal: pascal,
          populationRange: const {'low': 66, 'high': 133},
          optimalRange: const {'high': 90},
        ),
    'blurb': blurb,
    'whatMovesIt': whatMovesIt,
    'sourceUrl': sourceUrl,
    'history':
        history ??
        [
          labPoint(pascal: pascal, date: '2026-10-24', value: 92, flag: 'high'),
          labPoint(pascal: pascal, date: '2026-09-20', value: 78),
          labPoint(pascal: pascal, date: '2026-08-20', value: 70, flag: 'low'),
        ],
  }, pascal);

  static Map<String, dynamic> assistant({
    bool pascal = false,
    String clientId = 'https://webui.example.test/client.json',
    String clientName = 'Open WebUI',
    String kind = 'cimd',
    String? lastUsedAt = '2026-10-29T08:00:00Z',
  }) => _case({
    'clientId': clientId,
    'clientName': clientName,
    'clientUri': 'https://webui.example.test',
    'kind': kind,
    'connectedAt': '2026-10-01T09:00:00Z',
    'lastUsedAt': ?lastUsedAt,
  }, pascal);

  static Map<String, dynamic> token({
    bool pascal = false,
    String id = 't1',
    String name = 'Kitchen tablet',
    String prefix = 'pat_Ab12Cd',
    String scope = 'health:read memory:write',
  }) => _case({
    'id': id,
    'name': name,
    'prefix': prefix,
    'scope': scope,
    'createdAt': '2026-10-02T09:00:00Z',
  }, pascal);

  static Map<String, dynamic> connections({
    bool pascal = false,
    List<Map<String, dynamic>>? assistants,
    List<Map<String, dynamic>>? tokens,
    String mcpUrl = 'https://box.tailnet.ts.net/mcp',
  }) => _case({
    'assistants': assistants ?? [assistant(pascal: pascal)],
    'tokens': tokens ?? [token(pascal: pascal)],
    'mcpUrl': mcpUrl,
  }, pascal);

  static Map<String, dynamic> createdToken({
    bool pascal = false,
    String secret = 'pat_Ab12CdEfGh34IjKl56MnOp78QrSt90',
  }) => _case({'token': token(pascal: pascal), 'secret': secret}, pascal);

  static Map<String, dynamic> memoryItem({
    bool pascal = false,
    String id = 'm1',
    String type = 'goal',
    String key = 'goal.sleep',
    String value = 'seven and a half hours',
    String? displayText = 'Aiming for seven and a half hours of sleep',
    String? source = 'Open WebUI',
  }) => _case({
    'id': id,
    'type': type,
    'key': key,
    'value': value,
    'displayText': ?displayText,
    'source': ?source,
    'createdAt': '2026-10-10T09:00:00Z',
    'updatedAt': '2026-10-11T09:00:00Z',
  }, pascal);

  static Map<String, dynamic> consentRequest({
    bool pascal = false,
    String requestId = 'abc',
    String clientName = 'Open WebUI',
  }) => _case({
    'requestId': requestId,
    'clientName': clientName,
    'clientUri': 'https://webui.example.test',
    'logoUri': 'https://webui.example.test/logo.png',
    'redirectHost': 'webui.example.test',
    'scopes': [
      _case({
        'name': 'health:read',
        'description': 'Read your health data',
      }, pascal),
      _case({
        'name': 'memory:write',
        'description': 'Remember notes for you',
      }, pascal),
    ],
    'expiresAt': '2026-10-30T12:00:00Z',
  }, pascal);

  static Map<String, dynamic> ouraStatus({
    bool pascal = false,
    bool configured = true,
    bool connected = false,
    String? lastError,
    int sampleCount = 0,
    bool pulled = true,
  }) => _case({
    'configured': configured,
    'connected': connected,
    if (connected) 'connectedAt': '2026-10-20T09:00:00Z',
    if (connected && pulled) 'lastPullAt': '2026-10-29T09:00:00Z',
    'lastError': ?lastError,
    'sampleCount': sampleCount,
  }, pascal);

  static Map<String, dynamic> _case(Map<String, dynamic> map, bool pascal) {
    if (!pascal) return map;
    return {
      for (final e in map.entries)
        e.key[0].toUpperCase() + e.key.substring(1): e.value,
    };
  }
}
