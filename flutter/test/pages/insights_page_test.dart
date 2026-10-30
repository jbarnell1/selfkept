import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/insights_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/insight_card.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
  });
  tearDown(() => h.dispose());

  List<Map<String, dynamic>> archive({bool withDismissed = false}) => [
    Fixtures.insight(id: 'i1', day: '2026-01-05'),
    Fixtures.insight(
      id: 'i2',
      day: '2026-01-05',
      kind: 'sleep_good_week',
      severity: 'notice',
      text: 'A better week of sleep than your usual.',
    ),
    Fixtures.insight(
      id: 'i3',
      day: '2026-01-03',
      kind: 'spo2_low',
      severity: 'clinician',
      text: 'Overnight oxygen has dipped on two nights.',
    ),
    if (withDismissed)
      Fixtures.insight(
        id: 'i4',
        day: '2026-01-02',
        kind: 'activity_dip',
        severity: 'notice',
        text: 'A quieter week than your norm.',
        dismissedAt: '2026-01-02T09:00:00Z',
      ),
  ];

  testBothThemes('data: cards grouped by day, newest first', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/insights', (_) => archive());
    await tester.pumpWidget(
      h.wrap(const Scaffold(body: InsightsPage()), theme: theme),
    );
    await tester.pumpAndSettle();

    expect(find.text('Insights'), findsOneWidget);
    expect(find.byType(InsightCard), findsNWidgets(3));
    final jan5 = find.textContaining('January 5');
    final jan3 = find.textContaining('January 3');
    expect(jan5, findsOneWidget);
    expect(jan3, findsOneWidget);
    expect(tester.getTopLeft(jan5).dy, lessThan(tester.getTopLeft(jan3).dy));
    expect(tester.takeException(), isNull);
  });

  testWidgets('the severity filter narrows the list', (tester) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/insights', (_) => archive());
    await tester.pumpWidget(h.wrap(const Scaffold(body: InsightsPage())));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(ChoiceChip, 'Clinician'));
    await tester.pumpAndSettle();
    expect(find.byType(InsightCard), findsOneWidget);
    expect(find.textContaining('Overnight oxygen'), findsOneWidget);
    expect(find.textContaining('January 5'), findsNothing);

    await tester.tap(find.widgetWithText(ChoiceChip, 'Watch'));
    await tester.pumpAndSettle();
    expect(find.byType(InsightCard), findsOneWidget);
    expect(find.textContaining('HRV has sat below'), findsOneWidget);

    await tester.tap(find.widgetWithText(ChoiceChip, 'All'));
    await tester.pumpAndSettle();
    expect(find.byType(InsightCard), findsNWidgets(3));
    // Filtering never refetches.
    expect(h.api.calls.where((c) => c == 'GET /insights').length, 1);
  });

  testWidgets('including dismissed reloads and shows the dismissed card', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/insights', (q) {
      final include = (q as Map)['includeDismissed'] == true;
      return archive(withDismissed: include);
    });
    await tester.pumpWidget(h.wrap(const Scaffold(body: InsightsPage())));
    await tester.pumpAndSettle();
    expect(find.byType(InsightCard), findsNWidgets(3));

    await tester.tap(find.widgetWithText(FilterChip, 'Include dismissed'));
    await tester.pumpAndSettle();
    expect(h.api.calls.where((c) => c == 'GET /insights').length, 2);
    await scrollTo(tester, find.textContaining('A quieter week'));
    expect(find.text('Dismissed'), findsOneWidget);
  });

  testWidgets('a reaction posts from the archive', (tester) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/insights', (_) => archive());
    h.api.on(
      'POST',
      '/insights/i1/reaction',
      (_) => Fixtures.insight(id: 'i1', day: '2026-01-05', reaction: 'wrong'),
    );
    await tester.pumpWidget(h.wrap(const Scaffold(body: InsightsPage())));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Not right').first);
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('POST /insights/i1/reaction'));
    expect(h.insights.known('i1')!.reaction?.name, 'wrong');
  });

  testWidgets('empty: the calm sentence', (tester) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/insights', (_) => <Map<String, dynamic>>[]);
    await tester.pumpWidget(h.wrap(const Scaffold(body: InsightsPage())));
    await tester.pumpAndSettle();
    expect(find.text(InsightsPage.emptySentence), findsOneWidget);
    expect(find.byType(InsightCard), findsNothing);
  });

  testWidgets('empty after filtering says so differently', (tester) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/insights', (_) => [archive().last]);
    await tester.pumpWidget(h.wrap(const Scaffold(body: InsightsPage())));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ChoiceChip, 'Notice'));
    await tester.pumpAndSettle();
    expect(find.text(InsightsPage.emptyFilteredSentence), findsOneWidget);
  });

  testWidgets('loading then error with a retry', (tester) async {
    await usePhoneSize(tester);
    final completer = Completer<List<Map<String, dynamic>>>();
    h.api.on('GET', '/insights', (_) => completer.future);
    await tester.pumpWidget(h.wrap(const Scaffold(body: InsightsPage())));
    await pumpFrames(tester);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    completer.completeError(
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpAndSettle();
    expect(find.text('Insights did not load.'), findsOneWidget);

    h.api.on('GET', '/insights', (_) => archive());
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.byType(InsightCard), findsNWidgets(3));
  });

  testWidgets('the clinician summary button sits at the bottom', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/insights', (_) => archive());
    await tester.pumpWidget(h.wrap(const Scaffold(body: InsightsPage())));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Clinician summary'));
    expect(find.text('Clinician summary'), findsOneWidget);
  });
}
