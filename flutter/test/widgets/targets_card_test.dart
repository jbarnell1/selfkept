import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/target.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/targets_card.dart';

import '../helpers/body_fixtures.dart';
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

  Future<void> pumpCard(WidgetTester tester, {ThemeData? theme}) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(
        const Scaffold(body: SingleChildScrollView(child: TargetsCard())),
        theme: theme,
      ),
    );
    await h.tally.load();
    await tester.pumpAndSettle();
  }

  testBothThemes('data: each target with where it stands and the week', (
    tester,
    theme,
  ) async {
    h.api.on(
      'GET',
      '/targets/tally',
      (_) => BodyFixtures.tally(
        targets: [
          BodyFixtures.targetTally(met: true, current: 140),
          BodyFixtures.targetTally(
            target: BodyFixtures.target(
              id: 't2',
              label: 'No late caffeine',
              kind: 'journal',
              metricKey: null,
              tag: 'late_caffeine',
              comparator: 'at_most',
              value: 0,
              period: 'week',
              unit: 'entries',
            ),
            current: null,
            met: null,
            week: const [null, null, null, null, null, null, null],
            metDays: 0,
            knownDays: 0,
          ),
          BodyFixtures.targetTally(
            target: BodyFixtures.target(
              id: 't3',
              label: 'Sleep',
              metricKey: 'sleep_stage',
              value: 420,
              unit: 'min',
            ),
            current: 400,
            met: false,
          ),
        ],
      ),
    );
    await pumpCard(tester, theme: theme);

    expect(find.text(TargetsCard.title), findsOneWidget);
    expect(find.text('Protein'), findsOneWidget);
    expect(find.text('140 / 130 g'), findsOneWidget);
    expect(find.text(TargetsCard.metText), findsOneWidget);
    expect(find.text('At least 130 g a day'), findsOneWidget);
    expect(find.text('3 of 4 days met'), findsNWidgets(2));
    expect(find.byType(WeekDots), findsNWidgets(3));

    // Nothing written down is unknown, never a miss.
    expect(find.text('No late caffeine'), findsOneWidget);
    expect(find.text(TargetsCard.nothingLoggedText), findsOneWidget);
    expect(find.text('No days counted yet'), findsOneWidget);

    expect(find.text('6 h 40 / 7 h'), findsOneWidget);
    expect(find.text(TargetsCard.notYetText), findsOneWidget);
  });

  test('status words never call an unknown day a miss', () {
    TargetTally tally({bool? met, double? current, String kind = 'metric'}) =>
        TargetTally.fromJson(
          BodyFixtures.targetTally(
            met: met,
            current: current,
            target: BodyFixtures.target(
              kind: kind,
              metricKey: kind == 'metric' ? 'steps' : null,
            ),
          ),
        );
    expect(TargetsCard.statusFor(tally(current: 4000)), TargetsCard.openText);
    expect(TargetsCard.statusFor(tally()), TargetsCard.noReadingText);
    expect(
      TargetsCard.statusFor(tally(kind: 'journal')),
      TargetsCard.nothingLoggedText,
    );
    expect(TargetsCard.statusFor(tally(met: true)), TargetsCard.metText);
  });

  testWidgets('loading: one quiet line', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/targets/tally',
      (_) => Completer<Map<String, dynamic>>().future,
    );
    await tester.pumpWidget(h.wrap(const Scaffold(body: TargetsCard())));
    unawaited(h.tally.load());
    await pumpFrames(tester);
    expect(find.text(TargetsCard.loadingLine), findsOneWidget);
  });

  testWidgets('error: says so and loads again', (tester) async {
    h.api.fail(
      'GET',
      '/targets/tally',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await pumpCard(tester);
    expect(find.text(TargetsCard.errorLine), findsOneWidget);
    expect(find.text('The server had a problem.'), findsOneWidget);

    h.api.on('GET', '/targets/tally', (_) => BodyFixtures.tally());
    await tester.tap(find.byTooltip(TargetsCard.retryTooltip));
    await tester.pumpAndSettle();
    expect(find.text('Protein'), findsOneWidget);
  });

  testWidgets('empty: no goal yet, with a way to set one', (tester) async {
    h.api.on(
      'GET',
      '/targets/tally',
      (_) => BodyFixtures.tally(goals: const [], targets: const []),
    );
    await pumpCard(tester);
    expect(find.text(TargetsCard.emptyLine), findsOneWidget);
    expect(find.text(TargetsCard.setGoalLabel), findsOneWidget);
  });

  testWidgets('a goal without targets asks for one', (tester) async {
    h.api.on(
      'GET',
      '/targets/tally',
      (_) => BodyFixtures.tally(targets: const []),
    );
    await pumpCard(tester);
    expect(find.text(TargetsCard.noTargetsLine), findsOneWidget);
    expect(find.text(TargetsCard.addTargetLabel), findsOneWidget);
  });
}
