import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/progress_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/body_chart.dart';
import 'package:selfkept/widgets/body_entry_sheets.dart';
import 'package:selfkept/widgets/person_switcher.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/body_fixtures.dart';
import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/progress', (_) => BodyFixtures.progress());
    h.api.on('GET', '/household', (_) => Fixtures.household());
    h.api.on('GET', '/household/shares', (_) => <Map<String, dynamic>>[]);
    h.api.on('GET', '/household/invites', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => <Map<String, dynamic>>[],
    );
  });
  tearDown(() => h.dispose());

  Future<void> pumpPage(WidgetTester tester, {ThemeData? theme}) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(const Scaffold(body: ProgressPage()), theme: theme),
    );
    await tester.pumpAndSettle();
  }

  testBothThemes('data: goal, targets, body, chart and strength', (
    tester,
    theme,
  ) async {
    await pumpPage(tester, theme: theme);
    expect(find.text(ProgressPage.title), findsOneWidget);
    expect(find.text('Main goal'), findsOneWidget);
    expect(find.text('Lose fat'), findsOneWidget);
    expect(find.text('On track'), findsOneWidget);
    expect(find.textContaining('aiming for 80.0 kg'), findsOneWidget);
    expect(find.byType(LinearProgressIndicator), findsOneWidget);
    expect(find.text('0.4 kg down a week'), findsOneWidget);
    expect(find.text('Around March 2027'), findsOneWidget);
    expect(find.text('This week\'s targets'), findsOneWidget);

    await scrollTo(tester, find.text('Where things stand'));
    expect(find.byType(BodyChart), findsWidgets);
    expect(find.text('84.2 kg'), findsOneWidget);
    expect(find.text('90.0 cm'), findsWidgets);
    expect(find.text(ProgressPage.setAsideNote), findsOneWidget);

    await scrollTo(tester, find.text('Squat'));
    expect(find.text('116.7 kg'), findsOneWidget);
    expect(find.textContaining('Up 11.1%'), findsOneWidget);
    expect(find.text('100.0 kg x 5, effort 8.5'), findsOneWidget);
  });

  testWidgets('imperial: weights read in pounds', (tester) async {
    h.api.on('POST', '/auth/login', (_) {
      final json = Fixtures.authResponse();
      ((json['user'] as Map)['person'] as Map)['units'] = 'imperial';
      return json;
    });
    await h.auth.login(email: 'a@b', password: 'x');
    await pumpPage(tester);
    expect(find.text('0.9 lb down a week'), findsOneWidget);
    await scrollTo(tester, find.text('Where things stand'));
    expect(find.text('185.6 lb'), findsOneWidget);
  });

  testWidgets('loading: one spinner', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/progress',
      (_) => Completer<Map<String, dynamic>>().future,
    );
    await tester.pumpWidget(h.wrap(const Scaffold(body: ProgressPage())));
    await pumpFrames(tester);
    expect(find.byType(LoadingView), findsOneWidget);
    expect(find.text('Main goal'), findsNothing);
  });

  testWidgets('error: says so and tries again', (tester) async {
    h.api.fail(
      'GET',
      '/progress',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await pumpPage(tester);
    expect(find.text(ProgressPage.loadErrorTitle), findsOneWidget);
    h.api.on('GET', '/progress', (_) => BodyFixtures.progress());
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text('Lose fat'), findsOneWidget);
  });

  testWidgets('empty: suggestions to start from and no body yet', (
    tester,
  ) async {
    h.api.on(
      'GET',
      '/progress',
      (_) => BodyFixtures.progress(
        goals: const [],
        suggestions: [BodyFixtures.suggestion()],
        strength: const [],
        body: false,
      ),
    );
    h.api.on('POST', '/goals', (_) => BodyFixtures.goal(id: 'g9'));
    h.api.on('POST', '/targets', (_) => BodyFixtures.target());
    await pumpPage(tester);
    expect(find.text(ProgressPage.noGoalLine), findsOneWidget);
    expect(find.text('Starting from 84.0 kg'), findsOneWidget);
    expect(find.text('Protein: at least 135 g a day'), findsOneWidget);
    await scrollTo(tester, find.text(ProgressPage.noStrengthLine));
    expect(find.text(ProgressPage.noBodyLine), findsOneWidget);

    await scrollTo(tester, find.text(ProgressPage.startLabel));
    await tester.tap(find.text(ProgressPage.startLabel));
    await tester.pumpAndSettle();
    expect(h.api.calls, containsAllInOrder(['POST /goals', 'POST /targets']));
    expect(find.text('Goal started.'), findsOneWidget);
  });

  testWidgets('adding a weight saves it in kg and Undo deletes it', (
    tester,
  ) async {
    h.api.on('POST', '/body/measurements', (_) => BodyFixtures.measurement());
    h.api.on('DELETE', '/body/measurements/weight_kg/w1', (_) => null);
    await pumpPage(tester);
    await scrollTo(tester, find.text(ProgressPage.addReadingLabel));
    await tester.tap(find.text(ProgressPage.addReadingLabel));
    await tester.pumpAndSettle();
    expect(find.text(MeasurementSheet.title), findsWidgets);
    await tester.enterText(find.byType(TextField), '82,5');
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /body/measurements')]
            as Map<String, dynamic>;
    expect(body['metricKey'], 'weight_kg');
    expect(body['value'], 82.5);
    expect(find.text(MeasurementSheet.savedMessage), findsOneWidget);
    await tester.tap(find.text('Undo'));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /body/measurements/weight_kg/w1'));
  });

  testWidgets('a waist reading goes in as waist_cm', (tester) async {
    h.api.on(
      'POST',
      '/body/measurements',
      (_) => BodyFixtures.measurement(metricKey: 'waist_cm', value: 88),
    );
    await pumpPage(tester);
    await scrollTo(tester, find.text(ProgressPage.addReadingLabel));
    await tester.tap(find.text(ProgressPage.addReadingLabel));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Waist').last);
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), '88');
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /body/measurements')]
            as Map<String, dynamic>;
    expect(body['metricKey'], 'waist_cm');
    expect(body['value'], 88);
  });

  testWidgets('logging a set sends lift, weight, reps and RPE, with Undo', (
    tester,
  ) async {
    h.api.on('POST', '/strength', (_) => BodyFixtures.strengthSet(id: 's9'));
    h.api.on('DELETE', '/strength/s9', (_) => null);
    await pumpPage(tester);
    await scrollTo(tester, find.text(ProgressPage.logSetLabel));
    await tester.tap(find.text(ProgressPage.logSetLabel));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ChoiceChip, 'Deadlift'));
    await tester.enterText(
      find.widgetWithText(TextField, StrengthSetSheet.weightLabel),
      '140',
    );
    await tester.enterText(
      find.widgetWithText(TextField, StrengthSetSheet.repsLabel),
      '3',
    );
    await tester.enterText(
      find.widgetWithText(TextField, StrengthSetSheet.rpeLabel),
      '8.5',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /strength')]
            as Map<String, dynamic>;
    expect(body['lift'], 'deadlift');
    expect(body['weightKg'], 140);
    expect(body['reps'], 3);
    expect(body['rpe'], 8.5);
    expect(find.text(StrengthSetSheet.savedMessage), findsOneWidget);
    await tester.tap(find.text('Undo'));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /strength/s9'));
  });

  testWidgets('a set can be deleted from the lift card', (tester) async {
    h.api.on('DELETE', '/strength/s1', (_) => null);
    await pumpPage(tester);
    await scrollTo(tester, find.byTooltip(ProgressPage.deleteSetTooltip).first);
    await tester.tap(find.byTooltip(ProgressPage.deleteSetTooltip).first);
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /strength/s1'));
  });

  testWidgets('someone else: their record, read only', (tester) async {
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => [
        {
          'personId': 'p2',
          'displayName': 'Charles',
          'categories': ['body', 'activity'],
        },
      ],
    );
    await pumpPage(tester);
    expect(find.text(PersonSwitcher.selfLabel), findsOneWidget);
    await tester.tap(find.widgetWithText(ChoiceChip, 'Charles'));
    await tester.pumpAndSettle();
    final query =
        h.api.bodies[h.api.calls.lastIndexOf('GET /progress')]
            as Map<String, dynamic>;
    expect(query['personId'], 'p2');
    expect(find.text(PersonSwitcher.readOnlyLine('Charles')), findsOneWidget);
    expect(find.text(ProgressPage.manageLabel), findsNothing);
    expect(find.text(ProgressPage.addReadingLabel), findsNothing);
    expect(find.text(ProgressPage.logSetLabel), findsNothing);
    expect(find.byTooltip(ProgressPage.deleteSetTooltip), findsNothing);
  });

  testWidgets('not shared: the 404 shows the not shared state', (tester) async {
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => [
        {
          'personId': 'p2',
          'displayName': 'Charles',
          'categories': ['labs'],
        },
      ],
    );
    await pumpPage(tester);
    h.api.fail(
      'GET',
      '/progress',
      const ApiException('Not found', statusCode: 404),
    );
    await tester.tap(find.widgetWithText(ChoiceChip, 'Charles'));
    await tester.pumpAndSettle();
    expect(find.text(NotSharedView.title), findsOneWidget);
    expect(find.text(NotSharedView.detailFor('Charles')), findsOneWidget);

    h.api.on('GET', '/progress', (_) => BodyFixtures.progress());
    await tester.tap(find.text(NotSharedView.backLabel));
    await tester.pumpAndSettle();
    expect(find.text('Lose fat'), findsOneWidget);
  });
}
