import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/pages/trend_detail_page.dart';
import 'package:selfkept/pages/trends_page.dart';
import 'package:selfkept/theme/tokens.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/trend_chart.dart';
import 'package:selfkept/widgets/trend_sparkline.dart';

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

  GoRouter router() => GoRouter(
    initialLocation: '/trends',
    routes: [
      GoRoute(
        path: '/trends',
        builder: (_, _) => const Scaffold(body: TrendsPage()),
        routes: [
          GoRoute(
            path: ':metric',
            builder: (_, state) =>
                TrendDetailPage(metric: state.pathParameters['metric']!),
          ),
        ],
      ),
    ],
  );

  /// Steps above its range, so it should jump ahead of resting heart rate.
  Map<String, dynamic> withStepsOutside() => {
    'metrics': [
      Fixtures.trendMetric(),
      Fixtures.trendMetric(
        metric: 'resting_hr_bpm',
        unit: 'bpm',
        displayName: 'Resting heart rate',
        latestValue: 58,
        band: 'inside',
      ),
      Fixtures.trendMetric(
        metric: 'steps',
        unit: 'count',
        displayName: 'Steps',
        latestValue: 14210,
        band: 'above',
      ),
    ],
  };

  testBothThemes('data: one row per metric, outside first, in amber', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/trends', (_) => withStepsOutside());
    await tester.pumpWidget(
      h.wrap(const Scaffold(body: TrendsPage()), theme: theme),
    );
    await tester.pumpAndSettle();

    expect(find.text('Trends'), findsOneWidget);
    expect(find.byType(TrendRow), findsNWidgets(3));
    expect(find.byType(TrendSparkline), findsNWidgets(3));
    double y(String text) => tester.getTopLeft(find.text(text)).dy;
    expect(y('Sleep'), lessThan(y('Steps')));
    expect(y('Steps'), lessThan(y('Resting heart rate')));

    expect(find.text('7 h 40'), findsOneWidget);
    expect(find.text('Below your range'), findsOneWidget);
    expect(find.text('Above your range'), findsOneWidget);
    expect(find.text(TrendsPage.withinText), findsOneWidget);
    // No chart on the list; it opens on its own screen.
    expect(find.byType(TrendChart), findsNothing);
    expect(h.api.calls, isNot(contains('GET /trends/sleep_stage')));

    final outside = tester.widget<Text>(find.text('Above your range'));
    final inside = tester.widget<Text>(find.text(TrendsPage.withinText));
    expect(outside.style?.color, theme.colorScheme.error);
    expect(inside.style?.color, theme.colorScheme.onSurfaceVariant);
    final sparks = tester
        .widgetList<TrendSparkline>(find.byType(TrendSparkline))
        .toList();
    expect(sparks.where((s) => s.outside), hasLength(2));
    expect(sparks.first.points, hasLength(28));
    expect(sparks.first.range, isNotNull);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a row opens its chart, and back returns to the list', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const SizedBox(), router: router()));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Sleep'));
    await tester.pumpAndSettle();
    expect(find.byType(TrendDetailPage), findsOneWidget);
    expect(find.byType(TrendChart), findsOneWidget);
    expect(find.text('Latest is below your range.'), findsOneWidget);
    expect(h.api.calls, contains('GET /trends/sleep_stage'));

    await tester.tap(find.byType(BackButton));
    await tester.pumpAndSettle();
    expect(find.byType(TrendDetailPage), findsNothing);
    expect(find.byType(TrendRow), findsNWidgets(3));
  });

  testWidgets('learning: a metric without a band says so', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/trends',
      (_) => {
        'metrics': [Fixtures.trendMetric(band: null, baselineDays: 9)],
      },
    );
    await tester.pumpWidget(h.wrap(const Scaffold(body: TrendsPage())));
    await tester.pumpAndSettle();
    expect(find.text(TrendsPage.learningText), findsOneWidget);
    final spark = tester.widget<TrendSparkline>(find.byType(TrendSparkline));
    expect(spark.range, isNull);
    expect(spark.outside, isFalse);
  });

  testWidgets('empty: a calm sentence when no metric has a week yet', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/trends', (_) => {'metrics': <Map<String, dynamic>>[]});
    await tester.pumpWidget(h.wrap(const Scaffold(body: TrendsPage())));
    await tester.pumpAndSettle();
    expect(find.text(TrendsPage.emptySentence), findsOneWidget);
    expect(find.byType(TrendRow), findsNothing);
  });

  testBothThemes('loading then error with a retry', (tester, theme) async {
    await usePhoneSize(tester);
    final completer = Completer<Map<String, dynamic>>();
    h.api.on('GET', '/trends', (_) => completer.future);
    await tester.pumpWidget(
      h.wrap(const Scaffold(body: TrendsPage()), theme: theme),
    );
    await pumpFrames(tester);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    completer.completeError(
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpAndSettle();
    expect(find.text('Trends did not load.'), findsOneWidget);

    h.api.on('GET', '/trends', (_) => Fixtures.trends());
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.byType(TrendRow), findsNWidgets(3));
  });

  testWidgets('rows are at least a thumb tall, and laptop width fits', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const Scaffold(body: TrendsPage())));
    await tester.pumpAndSettle();
    for (final row in tester.widgetList<TrendRow>(find.byType(TrendRow))) {
      expect(
        tester.getSize(find.byWidget(row)).height,
        greaterThanOrEqualTo(48),
      );
    }

    tester.view.physicalSize = const Size(1280, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(Space.x4, 16);
  });
}
