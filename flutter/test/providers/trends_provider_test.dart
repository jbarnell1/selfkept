import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/trends', (_) => Fixtures.trends());
    h.api.on('GET', '/trends/sleep_stage', (q) {
      final days = (q as Map)['days'] as int;
      return Fixtures.trendSeries(days: days);
    });
    h.api.on(
      'GET',
      '/trends/steps',
      (_) => Fixtures.trendSeries(metric: 'steps', unit: 'count'),
    );
  });
  tearDown(() => h.dispose());

  test(
    'loadMetrics sorts by priority and fetches no series until one is opened',
    () async {
      await h.trends.loadMetrics();
      expect(h.trends.hasLoadedMetrics, isTrue);
      expect(h.trends.metrics.map((m) => m.metric), [
        'sleep_stage',
        'resting_hr_bpm',
        'steps',
      ]);
      expect(h.trends.selected, isNull);
      expect(h.api.calls, isNot(contains('GET /trends/sleep_stage')));

      await h.trends.select('sleep_stage');
      expect(h.trends.selectedMetric?.label, 'Sleep');
      expect(h.trends.days, 28);
      expect(h.trends.series!.points, hasLength(28));
      final query =
          h.api.bodies[h.api.calls.indexOf('GET /trends/sleep_stage')] as Map;
      expect(query['days'], 28);
    },
  );

  test('ordered puts metrics outside their range first', () async {
    h.api.on(
      'GET',
      '/trends',
      (_) => {
        'metrics': [
          Fixtures.trendMetric(band: 'inside'),
          Fixtures.trendMetric(
            metric: 'steps',
            displayName: 'Steps',
            band: 'above',
          ),
          Fixtures.trendMetric(
            metric: 'resting_hr_bpm',
            displayName: 'Resting heart rate',
            band: null,
          ),
        ],
      },
    );
    await h.trends.loadMetrics();
    expect(h.trends.ordered.map((m) => m.metric), [
      'steps',
      'sleep_stage',
      'resting_hr_bpm',
    ]);
    expect(h.trends.metricFor('steps')!.recent, hasLength(28));
  });

  test('select switches the metric and caches each series', () async {
    await h.trends.loadMetrics();
    await h.trends.select('steps');
    expect(h.trends.selected, 'steps');
    expect(h.trends.series!.metric, 'steps');
    await h.trends.select('sleep_stage');
    expect(h.trends.series!.metric, 'sleep_stage');
    expect(h.api.calls.where((c) => c == 'GET /trends/sleep_stage').length, 1);
  });

  test('setDays fetches the 90 day window once and keeps 28 cached', () async {
    await h.trends.loadMetrics();
    await h.trends.select('sleep_stage');
    await h.trends.setDays(90);
    expect(h.trends.days, 90);
    expect(h.trends.series!.points, hasLength(90));
    final queries = [
      for (var i = 0; i < h.api.calls.length; i++)
        if (h.api.calls[i] == 'GET /trends/sleep_stage')
          (h.api.bodies[i] as Map)['days'],
    ];
    expect(queries, [28, 90]);
    await h.trends.setDays(28);
    expect(h.trends.series!.points, hasLength(28));
    expect(h.api.calls.where((c) => c == 'GET /trends/sleep_stage').length, 2);
    await h.trends.setDays(7);
    expect(h.trends.days, 28);
  });

  test('an empty metric list leaves nothing selected', () async {
    h.api.on('GET', '/trends', (_) => {'metrics': <Map<String, dynamic>>[]});
    await h.trends.loadMetrics();
    expect(h.trends.hasLoadedMetrics, isTrue);
    expect(h.trends.metrics, isEmpty);
    expect(h.trends.selected, isNull);
    expect(h.trends.series, isNull);
  });

  test(
    'a failed metric list and a failed series each keep a message',
    () async {
      h.api.fail(
        'GET',
        '/trends',
        const ApiException('The server had a problem.', statusCode: 500),
      );
      await h.trends.loadMetrics();
      expect(h.trends.hasLoadedMetrics, isFalse);
      expect(h.trends.error, 'The server had a problem.');

      h.api.on('GET', '/trends', (_) => Fixtures.trends());
      h.api.fail(
        'GET',
        '/trends/sleep_stage',
        const ApiException('The server could not be reached.', isNetwork: true),
      );
      await h.trends.loadMetrics();
      expect(h.trends.hasLoadedMetrics, isTrue);
      await h.trends.select('sleep_stage');
      expect(h.trends.series, isNull);
      expect(h.trends.seriesError, 'The server could not be reached.');

      h.api.on('GET', '/trends/sleep_stage', (_) => Fixtures.trendSeries());
      await h.trends.retrySeries();
      expect(h.trends.series, isNotNull);
      expect(h.trends.seriesError, isNull);
    },
  );

  test('refresh drops the cache and fetches the open chart again', () async {
    await h.trends.loadMetrics();
    await h.trends.select('sleep_stage');
    await h.trends.refresh();
    expect(h.api.calls.where((c) => c == 'GET /trends').length, 2);
    expect(h.api.calls.where((c) => c == 'GET /trends/sleep_stage').length, 2);
  });
}
