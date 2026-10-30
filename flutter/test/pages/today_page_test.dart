import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/today_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/check_in_entry.dart';
import 'package:selfkept/widgets/insight_card.dart';
import 'package:selfkept/widgets/readiness_row.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/sources', (_) => [Fixtures.source()]);
    h.api.on('GET', '/today', (_) => Fixtures.today());
  });
  tearDown(() => h.dispose());

  testBothThemes(
    'data: greeting, sentence, readiness, two cards, entry and Sources',
    (tester, theme) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/checkins', (_) => [Fixtures.checkin()]);
      await tester.pumpWidget(h.wrap(const TodayPage(), theme: theme));
      await tester.pumpAndSettle();

      expect(find.textContaining(', Ada.'), findsOneWidget);
      expect(find.textContaining('You slept 7 h 40'), findsOneWidget);
      expect(find.byType(ReadinessRow), findsOneWidget);
      expect(find.text('7 h 40'), findsOneWidget);
      expect(
        find.textContaining('below your range', findRichText: true),
        findsOneWidget,
      );
      expect(
        find.textContaining('in your range', findRichText: true),
        findsNWidgets(2),
      );
      expect(find.byType(InsightCard), findsNWidgets(2));
      expect(find.text('Watch'), findsOneWidget);
      expect(find.text('Notice'), findsOneWidget);
      expect(find.text(TodayPage.learningMessage), findsNothing);
      expect(find.byType(CheckInEntry), findsOneWidget);

      await scrollTo(tester, find.text('Sources'));
      expect(find.textContaining('Last check-in: Good'), findsOneWidget);
      expect(find.textContaining('"slept well"'), findsOneWidget);
      expect(find.textContaining('Last sync'), findsOneWidget);
    },
  );

  testWidgets('loading: a spinner where the sentence will be', (tester) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/today', (_) => Completer<Map<String, dynamic>>().future);
    h.api.on(
      'GET',
      '/checkins',
      (_) => Completer<List<Map<String, dynamic>>>().future,
    );
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await pumpFrames(tester);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.byType(ReadinessRow), findsNothing);
    expect(find.byType(InsightCard), findsNothing);
    expect(find.byType(CheckInEntry), findsOneWidget);
    expect(find.textContaining('Last check-in'), findsNothing);
  });

  testWidgets('learning: the sentence plus a line that does not repeat it', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/today',
      (_) => Fixtures.today(
        sentence:
            'Still learning your normal. A couple more weeks of data and the comparisons start.',
        learning: true,
        cards: const [],
        sleepMinutes: null,
        hrv: null,
        restingHr: null,
        sleep: null,
      ),
    );
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await tester.pumpAndSettle();
    expect(find.text(TodayPage.learningMessage), findsOneWidget);
    expect(find.textContaining('A couple more weeks'), findsOneWidget);
    expect(
      find.textContaining('Still learning your normal'),
      findsOneWidget,
      reason: 'said once, by the sentence',
    );
    expect(
      find.text(ReadinessRow.noBandText, findRichText: true),
      findsNWidgets(3),
    );
    expect(find.byType(InsightCard), findsNothing);
  });

  testWidgets(
    'the first frame, before loading starts, is loading and never the error',
    (tester) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
      h.api.on('GET', '/today', (_) => Fixtures.today());
      await tester.pumpWidget(h.wrap(const TodayPage()));
      expect(find.text(TodayPage.loadErrorTitle), findsNothing);
      await tester.pumpAndSettle();
      expect(find.text(TodayPage.loadErrorTitle), findsNothing);
    },
  );

  testWidgets('error: one line, a retry, and the check-in still works', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    h.api.fail(
      'GET',
      '/today',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await tester.pumpAndSettle();
    expect(find.text(TodayPage.loadErrorTitle), findsOneWidget);
    expect(find.text('The server had a problem.'), findsOneWidget);
    expect(find.byType(CheckInEntry), findsOneWidget);

    h.api.on('GET', '/today', (_) => Fixtures.today());
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.textContaining('You slept 7 h 40'), findsOneWidget);
    expect(find.text(TodayPage.loadErrorTitle), findsNothing);
  });

  testWidgets('a reaction on a Today card posts and marks the card', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'POST',
      '/insights/i1/reaction',
      (_) => Fixtures.insight(id: 'i1', reaction: 'helpful'),
    );
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Helpful').first);
    await tester.tap(find.text('Helpful').first);
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('POST /insights/i1/reaction'));
    expect(h.today.cards.first.reaction?.name, 'helpful');
  });

  testWidgets('dismissing a Today card removes it', (tester) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'POST',
      '/insights/i1/dismiss',
      (_) => Fixtures.insight(id: 'i1', dismissedAt: '2026-10-29T12:00:00Z'),
    );
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await tester.pumpAndSettle();
    expect(find.byType(InsightCard), findsNWidgets(2));
    await tester.tap(find.byTooltip('Dismiss').first);
    await tester.pumpAndSettle();
    expect(find.byType(InsightCard), findsOneWidget);
    expect(find.text('Watch'), findsNothing);
  });

  testWidgets('empty: says there are no check-ins yet', (tester) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('No check-ins yet.'));
    expect(find.text('No check-ins yet.'), findsOneWidget);
  });

  testWidgets('keeping a check-in posts, says Kept. and reloads Today', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'POST',
      '/checkins',
      (data) => Fixtures.checkin(
        id: 'c-new',
        mood: (data as Map)['mood'] as int,
        note: data['note'] as String?,
      ),
    );
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await tester.pumpAndSettle();
    final todayCalls = h.api.calls.where((c) => c == 'GET /today').length;

    await scrollTo(tester, find.text('Keep'));
    await tester.tap(find.bySemanticsLabel('Bright'));
    await tester.enterText(find.byType(TextField), 'long walk');
    await tester.tap(find.text('Keep'));
    await tester.pumpAndSettle();

    final body = h.api.bodies[h.api.calls.indexOf('POST /checkins')] as Map;
    expect(body, {'mood': 6, 'note': 'long walk'});
    expect(find.text(TodayPage.keptMessage), findsOneWidget);
    expect(h.checkins.last!.id, 'c-new');
    expect(h.api.calls.where((c) => c == 'GET /today').length, todayCalls + 1);
    // The entry starts fresh.
    expect(
      tester.widget<TextField>(find.byType(TextField)).controller!.text,
      '',
    );
  });

  testWidgets('Keep without a mood asks for one', (tester) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    await tester.pumpWidget(h.wrap(Scaffold(body: const TodayPage())));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Keep'));
    await tester.tap(find.text('Keep'));
    await tester.pump();
    expect(find.text(TodayPage.chooseMoodMessage), findsOneWidget);
    expect(h.api.calls, isNot(contains('POST /checkins')));
  });

  testWidgets('a refused check-in is rolled back and explained', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    h.api.fail(
      'POST',
      '/checkins',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(h.wrap(Scaffold(body: const TodayPage())));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Keep'));
    await tester.tap(find.bySemanticsLabel('Okay'));
    await tester.tap(find.text('Keep'));
    await tester.pumpAndSettle();
    expect(find.text('The server had a problem.'), findsOneWidget);
    expect(h.checkins.recent, isEmpty);
    expect(find.text('No check-ins yet.'), findsOneWidget);
  });

  testWidgets('lays out at laptop width with the larger sentence', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1280, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/today',
      (_) => Fixtures.today(
        sentence: 'You slept 7 h 40 min last night, about your usual.',
      ),
    );
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await tester.pumpAndSettle();
    final sentence = tester.widget<Text>(
      find.textContaining('You slept 7 h 40'),
    );
    expect(sentence.style?.fontSize, 28);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a long sentence is shown whole, never cut off', (tester) async {
    await usePhoneSize(tester);
    const long =
        'You slept 6 h 24 min last night, a little short of your usual. HRV is below your usual range and resting heart rate is a touch above it. One thing is worth a look; it is below.';
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    h.api.on('GET', '/today', (_) => Fixtures.today(sentence: long));
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await tester.pumpAndSettle();
    final text = tester.widget<Text>(find.text(long));
    expect(text.maxLines, isNull);
    expect(text.overflow, isNot(TextOverflow.ellipsis));
  });
}
