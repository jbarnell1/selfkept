import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/assistant.dart';
import 'package:selfkept/pages/today_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/assistant/assistant_today_cards.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/body_fixtures.dart';
import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  final evening = DateTime(2026, 11, 10, 20);
  final morning = DateTime(2026, 11, 10, 8);

  setUp(() async {
    h = TestHarness();
    await signInWithAssistant(h);
    h.api.on('GET', '/assistant/brief', (_) => AssistantFixtures.brief());
    h.api.on(
      'GET',
      '/assistant/checkin/today',
      (_) => AssistantFixtures.checkinToday(),
    );
  });
  tearDown(() => h.dispose());

  Future<void> pumpCards(
    WidgetTester tester, {
    DateTime? now,
    ThemeData? theme,
  }) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(
        Scaffold(
          body: SingleChildScrollView(
            child: AssistantTodayCards(now: () => now ?? evening),
          ),
        ),
        theme: theme,
      ),
    );
    await tester.pumpAndSettle();
  }

  testBothThemes('evening: the brief lines and "Close out today"', (
    tester,
    theme,
  ) async {
    await pumpCards(tester, theme: theme);
    expect(find.text(MorningBriefCard.title), findsOneWidget);
    expect(
      find.text('You slept 6 h 5 min last night, below your usual range.'),
      findsOneWidget,
    );
    expect(find.text('Yesterday you kept 2 of 3 targets.'), findsOneWidget);
    expect(find.text(CheckinCard.startTitle), findsOneWidget);
    expect(
      find.text(
        CheckinCard.startLine(
          CheckinTopic.listFrom(['eating', 'drinks', 'mood']),
        ),
      ),
      findsOneWidget,
    );
  });

  testWidgets('morning, not started: the brief only', (tester) async {
    await pumpCards(tester, now: morning);
    expect(find.text(MorningBriefCard.title), findsOneWidget);
    expect(find.text(CheckinCard.startTitle), findsNothing);
  });

  testWidgets('partial: "Pick up where you left off" at any hour', (
    tester,
  ) async {
    h.api.on(
      'GET',
      '/assistant/checkin/today',
      (_) => AssistantFixtures.checkinToday(
        status: 'partial',
        conversationId: 'ck1',
        done: ['eating'],
      ),
    );
    await pumpCards(tester, now: morning);
    expect(find.text(CheckinCard.resumeTitle), findsOneWidget);
    expect(
      find.text('Covered: eating. Still to go: drinks, mood.'),
      findsOneWidget,
    );
    expect(find.text(CheckinCard.resumeLabel), findsOneWidget);
  });

  testWidgets('done: one quiet line in the evening, nothing in the morning', (
    tester,
  ) async {
    h.api.on(
      'GET',
      '/assistant/checkin/today',
      (_) => AssistantFixtures.checkinToday(status: 'done'),
    );
    await pumpCards(tester);
    expect(find.text(CheckinCard.doneLine), findsOneWidget);
    expect(find.text(CheckinCard.startTitle), findsNothing);
  });

  testWidgets('nothing missing and not started: no card', (tester) async {
    h.api.on(
      'GET',
      '/assistant/checkin/today',
      (_) => AssistantFixtures.checkinToday(missing: []),
    );
    await pumpCards(tester);
    expect(find.byType(CheckinCard), findsNothing);
  });

  testWidgets('a brief with no items shows nothing', (tester) async {
    h.api.on(
      'GET',
      '/assistant/brief',
      (_) => AssistantFixtures.brief(items: []),
    );
    await pumpCards(tester, now: morning);
    expect(find.byType(MorningBriefCard), findsNothing);
  });

  testWidgets('loading: nothing yet', (tester) async {
    final gate = Completer<Map<String, dynamic>>();
    h.api.on('GET', '/assistant/brief', (_) => gate.future);
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(Scaffold(body: AssistantTodayCards(now: () => evening))),
    );
    await tester.pump();
    expect(find.byType(MorningBriefCard), findsNothing);
    expect(find.byType(CheckinCard), findsNothing);
    gate.complete(AssistantFixtures.brief());
    await tester.pumpAndSettle();
    expect(find.byType(MorningBriefCard), findsOneWidget);
  });

  testWidgets('errors: one quiet line each', (tester) async {
    h.api.fail('GET', '/assistant/brief', const ApiException.network());
    h.api.fail('GET', '/assistant/checkin/today', const ApiException.network());
    await pumpCards(tester);
    expect(find.text(MorningBriefCard.errorLine), findsOneWidget);
    expect(find.text(CheckinCard.errorLine), findsOneWidget);
  });

  testWidgets('a server without the assistant: nothing, and no calls', (
    tester,
  ) async {
    h.dispose();
    h = TestHarness();
    await signInWithAssistant(h, feature: false);
    await pumpCards(tester);
    expect(find.byType(MorningBriefCard), findsNothing);
    expect(find.byType(CheckinCard), findsNothing);
    expect(h.api.calls.where((c) => c.contains('/assistant/')), isEmpty);
  });

  testWidgets('Today shows the cards above the sentence', (tester) async {
    h.api.on('GET', '/sources', (_) => [Fixtures.source()]);
    h.api.on('GET', '/today', (_) => Fixtures.today());
    h.api.on('GET', '/targets/tally', (_) => BodyFixtures.tally());
    h.api.on('GET', '/journal', (_) => <Map<String, dynamic>>[]);
    h.api.on('GET', '/journal/tags', (_) => BodyFixtures.tagGroups());
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const TodayPage()));
    await tester.pumpAndSettle();
    expect(find.byType(AssistantTodayCards), findsOneWidget);
    expect(find.text(MorningBriefCard.title), findsOneWidget);
  });
}
