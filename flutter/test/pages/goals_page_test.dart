import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/goal.dart';
import 'package:selfkept/pages/progress_page.dart';
import 'package:selfkept/pages/you/goal_forms.dart';
import 'package:selfkept/pages/you/goals_page.dart';
import 'package:selfkept/utils/api_error.dart';
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
    h.api.on('GET', '/goals', (_) => [BodyFixtures.goal()]);
    h.api.on(
      'GET',
      '/targets',
      (_) => [
        BodyFixtures.target(),
        BodyFixtures.target(
          id: 't2',
          label: 'No late caffeine',
          kind: 'journal',
          metricKey: null,
          tag: 'late_caffeine',
          comparator: 'at_most',
          value: 0,
          period: 'week',
          unit: 'entries',
          sortOrder: 1,
          active: false,
        ),
      ],
    );
    h.api.on(
      'GET',
      '/progress',
      (_) => BodyFixtures.progress(suggestions: [BodyFixtures.suggestion()]),
    );
    h.api.on('GET', '/journal/tags', (_) => BodyFixtures.tagGroups());
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
    await tester.pumpWidget(h.wrap(const GoalsPage(), theme: theme));
    await tester.pumpAndSettle();
  }

  testBothThemes('data: the main goal, an empty second slot and targets', (
    tester,
    theme,
  ) async {
    await pumpPage(tester, theme: theme);
    expect(find.text(GoalsPage.title), findsOneWidget);
    expect(find.text('Lose fat'), findsWidgets);
    expect(find.text('86.0 kg'), findsOneWidget);
    expect(find.text('80.0 kg'), findsOneWidget);
    expect(find.text('0.5 kg down a week'), findsOneWidget);
    expect(find.text(GoalsPage.emptySlot(GoalSlot.secondary)), findsOneWidget);
    expect(find.text(GoalsPage.ideasTitle), findsOneWidget);

    await scrollTo(tester, find.text('No late caffeine'));
    expect(find.text('Protein'), findsOneWidget);
    expect(find.textContaining('None in a week'), findsOneWidget);
    expect(find.textContaining(GoalsPage.pausedText), findsOneWidget);
  });

  testWidgets('loading: one spinner', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/goals',
      (_) => Completer<List<Map<String, dynamic>>>().future,
    );
    await tester.pumpWidget(h.wrap(const GoalsPage()));
    await pumpFrames(tester);
    expect(find.byType(LoadingView), findsOneWidget);
  });

  testWidgets('error: says so and tries again', (tester) async {
    h.api.fail(
      'GET',
      '/goals',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await pumpPage(tester);
    expect(find.text(GoalsPage.loadErrorTitle), findsOneWidget);
    h.api.on('GET', '/goals', (_) => [BodyFixtures.goal()]);
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text('Lose fat'), findsWidgets);
  });

  testWidgets('empty: both slots open, and targets explained', (tester) async {
    h.api.on('GET', '/goals', (_) => <Map<String, dynamic>>[]);
    h.api.on('GET', '/targets', (_) => <Map<String, dynamic>>[]);
    await pumpPage(tester);
    expect(find.text(GoalsPage.setLabel(GoalSlot.main)), findsOneWidget);
    expect(find.text(GoalsPage.setLabel(GoalSlot.secondary)), findsOneWidget);
    await scrollTo(tester, find.text(GoalsPage.noTargetsLine));
    expect(find.text(GoalsPage.noTargetsLine), findsOneWidget);
  });

  testWidgets('a goal set by hand is saved in kg with its pace pointing down', (
    tester,
  ) async {
    h.api.on('GET', '/goals', (_) => <Map<String, dynamic>>[]);
    h.api.on('POST', '/goals', (_) => BodyFixtures.goal());
    await pumpPage(tester);
    await tester.tap(find.text(GoalsPage.setLabel(GoalSlot.main)));
    await tester.pumpAndSettle();
    expect(find.text(GoalEditor.newTitle), findsOneWidget);
    await tester.enterText(
      find.widgetWithText(TextField, GoalEditor.startLabel),
      '86',
    );
    await tester.enterText(
      find.widgetWithText(TextField, GoalEditor.targetLabel),
      '80',
    );
    await tester.enterText(
      find.widgetWithText(TextField, GoalEditor.paceLabel),
      '0.5',
    );
    await tester.ensureVisible(find.widgetWithText(FilledButton, 'Save'));
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /goals')]
            as Map<String, dynamic>;
    expect(body['slot'], 'main');
    expect(body['kind'], 'lose_fat');
    expect(body['startValue'], 86);
    expect(body['targetValue'], 80);
    expect(body['pacePerWeek'], -0.5);
    expect(find.text(GoalEditor.newTitle), findsNothing);
  });

  testWidgets('a goal of your own needs a title', (tester) async {
    await pumpPage(tester);
    await scrollTo(tester, find.text(GoalsPage.setLabel(GoalSlot.secondary)));
    await tester.tap(find.text(GoalsPage.setLabel(GoalSlot.secondary)));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(DropdownButtonFormField<GoalKind>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Your goal').last);
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.widgetWithText(FilledButton, 'Save'));
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    expect(find.text(GoalEditor.titleRequired), findsOneWidget);
    expect(h.api.calls, isNot(contains('POST /goals')));
  });

  testWidgets('ending a goal asks done or dropped', (tester) async {
    h.api.on('POST', '/goals/g1/end', (_) => BodyFixtures.goal(status: 'done'));
    await pumpPage(tester);
    await tester.tap(find.text(GoalsPage.endLabel));
    await tester.pumpAndSettle();
    expect(find.text(GoalsPage.endTitle), findsOneWidget);
    await tester.tap(find.text(GoalsPage.doneLabel));
    await tester.pumpAndSettle();
    expect(h.api.bodies[h.api.calls.indexOf('POST /goals/g1/end')], {
      'status': 'done',
    });
    expect(find.text('Marked as done.'), findsOneWidget);
  });

  testWidgets('a suggestion starts in the free slot', (tester) async {
    h.api.on('POST', '/goals', (_) => BodyFixtures.goal(id: 'g9'));
    h.api.on('POST', '/targets', (_) => BodyFixtures.target());
    await pumpPage(tester);
    await scrollTo(tester, find.text(ProgressPage.startLabel));
    await tester.tap(find.text(ProgressPage.startLabel));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /goals')]
            as Map<String, dynamic>;
    expect(body['slot'], 'secondary');
  });

  testWidgets('adding a sleep target types hours and sends minutes', (
    tester,
  ) async {
    h.api.on('POST', '/targets', (_) => BodyFixtures.target(id: 't9'));
    await pumpPage(tester);
    await scrollTo(tester, find.text(GoalsPage.addTargetLabel));
    await tester.tap(find.text(GoalsPage.addTargetLabel));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextField, TargetEditor.labelLabel),
      'Sleep',
    );
    await tester.tap(find.text('Steps'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Sleep').last);
    await tester.pumpAndSettle();
    await tester.enterText(find.widgetWithText(TextField, 'Number'), '7.5');
    await tester.ensureVisible(find.widgetWithText(FilledButton, 'Save'));
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /targets')]
            as Map<String, dynamic>;
    expect(body['label'], 'Sleep');
    expect(body['kind'], 'metric');
    expect(body['metricKey'], 'sleep_stage');
    expect(body['value'], 450);
    expect(body['period'], 'day');
    expect(body['comparator'], 'at_least');
    expect(body['goalId'], 'g1');
  });

  testWidgets('a day log target picks a tag', (tester) async {
    h.api.on('POST', '/targets', (_) => BodyFixtures.target(id: 't9'));
    await pumpPage(tester);
    await scrollTo(tester, find.text(GoalsPage.addTargetLabel));
    await tester.tap(find.text(GoalsPage.addTargetLabel));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextField, TargetEditor.labelLabel),
      'No alcohol',
    );
    await tester.tap(find.text('A journal tag'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Day log tag'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Alcohol').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('At most'));
    await tester.enterText(find.widgetWithText(TextField, 'Number'), '1');
    await tester.tap(find.text('Each week'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.widgetWithText(FilledButton, 'Save'));
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /targets')]
            as Map<String, dynamic>;
    expect(body['kind'], 'journal');
    expect(body['tag'], 'alcohol');
    expect(body['metricKey'], isNull);
    expect(body['comparator'], 'at_most');
    expect(body['period'], 'week');
  });

  testWidgets('targets move and are removed', (tester) async {
    h.api.on('PUT', '/targets/t1', (_) => BodyFixtures.target());
    h.api.on('PUT', '/targets/t2', (_) => BodyFixtures.target(id: 't2'));
    h.api.on('DELETE', '/targets/t2', (_) => null);
    await pumpPage(tester);
    await scrollTo(tester, find.byTooltip(GoalsPage.moveDownTooltip).first);
    await tester.tap(find.byTooltip(GoalsPage.moveDownTooltip).first);
    await tester.pumpAndSettle();
    expect(h.api.calls, containsAll(['PUT /targets/t1', 'PUT /targets/t2']));

    await scrollTo(tester, find.byTooltip(GoalsPage.removeTargetTooltip).last);
    await tester.tap(find.byTooltip(GoalsPage.removeTargetTooltip).last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Remove'));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /targets/t2'));
  });

  testWidgets('someone else: their goals, no edit controls', (tester) async {
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => [
        {
          'personId': 'p2',
          'displayName': 'Charles',
          'categories': ['body'],
        },
      ],
    );
    await pumpPage(tester);
    await tester.tap(find.widgetWithText(ChoiceChip, 'Charles'));
    await tester.pumpAndSettle();
    final query =
        h.api.bodies[h.api.calls.lastIndexOf('GET /goals')]
            as Map<String, dynamic>;
    expect(query['personId'], 'p2');
    expect(find.text(GoalsPage.editLabel), findsNothing);
    expect(find.text(GoalsPage.endLabel), findsNothing);
    expect(find.text(GoalsPage.addTargetLabel), findsNothing);
    expect(find.text(GoalsPage.ideasTitle), findsNothing);
    expect(find.byTooltip(GoalsPage.removeTargetTooltip), findsNothing);
  });
}
