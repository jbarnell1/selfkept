import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/trend_detail_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/trend_chart.dart';

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
  });
  tearDown(() => h.dispose());

  Widget page() => const TrendDetailPage(metric: 'sleep_stage');

  testBothThemes('data: headline, toggle and chart, from a direct link', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(page(), theme: theme));
    await tester.pumpAndSettle();

    expect(find.text('Sleep'), findsOneWidget, reason: 'the bar title');
    expect(find.text('7 h 40'), findsOneWidget);
    expect(find.text('Latest is below your range.'), findsOneWidget);
    expect(find.text('28 days'), findsOneWidget);
    expect(find.text('90 days'), findsOneWidget);
    expect(find.byType(TrendChart), findsOneWidget);
    expect(find.textContaining('Your usual range is'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('the 90 day toggle fetches the wider window', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(page()));
    await tester.pumpAndSettle();
    await tester.tap(find.text('90 days'));
    await tester.pumpAndSettle();
    final queries = [
      for (var i = 0; i < h.api.calls.length; i++)
        if (h.api.calls[i] == 'GET /trends/sleep_stage')
          (h.api.bodies[i] as Map)['days'],
    ];
    expect(queries, [28, 90]);
    expect(h.trends.series!.points, hasLength(90));
  });

  testWidgets('tapping the chart shows the day in the caption', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(page()));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.byType(TrendChart));
    final rect = tester.getRect(find.byType(TrendChart));
    // The plot's right edge is the last day in the series.
    await tester.tapAt(Offset(rect.right - 12, rect.top + 100));
    await tester.pumpAndSettle();
    expect(find.textContaining('Oct 29'), findsWidgets);
    expect(find.textContaining('low 6 h 50'), findsOneWidget);
  });

  testWidgets('learning: a series without a band says so', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/trends',
      (_) => {
        'metrics': [Fixtures.trendMetric(band: null, baselineDays: 9)],
      },
    );
    h.api.on(
      'GET',
      '/trends/sleep_stage',
      (_) => Fixtures.trendSeries(withBand: false),
    );
    await tester.pumpWidget(h.wrap(page()));
    await tester.pumpAndSettle();
    expect(find.text('Still learning your normal.'), findsOneWidget);
    expect(find.text(TrendChart.learningText), findsOneWidget);
  });

  testWidgets('a series that fails offers a retry', (tester) async {
    await usePhoneSize(tester);
    h.api.fail(
      'GET',
      '/trends/sleep_stage',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(h.wrap(page()));
    await tester.pumpAndSettle();
    expect(find.text('This metric did not load.'), findsOneWidget);

    h.api.on('GET', '/trends/sleep_stage', (_) => Fixtures.trendSeries());
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.byType(TrendChart), findsOneWidget);
  });

  testWidgets('renders at laptop width without overflow', (tester) async {
    tester.view.physicalSize = const Size(1280, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(h.wrap(page()));
    await tester.pumpAndSettle();
    expect(find.byType(TrendChart), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
