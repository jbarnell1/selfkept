import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/labs_page.dart';
import 'package:selfkept/pages/systems/trials_page.dart';
import 'package:selfkept/pages/you/household_overview_page.dart';
import 'package:selfkept/pages/you/household_section.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';
import '../helpers/systems_fixtures.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    await signInWithFeatures(h);
    h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
    h.api.on('GET', '/labs/reports', (_) => [Fixtures.labReport()]);
    h.api.on(
      'GET',
      '/trials',
      (_) => [
        SystemsFixtures.trial(),
        SystemsFixtures.trial(id: 'tr2', title: 'No coffee', status: 'done'),
      ],
    );
    h.api.on(
      'GET',
      '/insights',
      (_) => [
        SystemsFixtures.insight(
          kind: 'trial_ready',
          severity: 'notice',
          text: 'A panel from Dec 12 has come in since the trial you started.',
          evidence: {'trialId': 'tr1', 'reportId': 'r2'},
        ),
      ],
    );
  });
  tearDown(() => h.dispose());

  Future<void> pump(
    WidgetTester tester,
    Widget page, {
    ThemeData? theme,
  }) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(page, theme: theme));
    await tester.pumpAndSettle();
  }

  group('trials', () {
    testBothThemes('active and ended, with the server word when ready', (
      tester,
      theme,
    ) async {
      await pump(tester, const TrialsPage(), theme: theme);
      expect(find.text(TrialsPage.title), findsOneWidget);
      expect(find.text('Oats instead of a bagel'), findsOneWidget);
      expect(find.text('Oats at breakfast'), findsWidgets);
      expect(
        find.text('Started Aug 16, 2026. Read on Dec 12, 2026.'),
        findsWidgets,
      );
      expect(
        find.text('Should move: lab.ldl, Resting heart rate'),
        findsWidgets,
      );
      expect(
        find.text(
          'A panel from Dec 12 has come in since the trial you started.',
        ),
        findsOneWidget,
      );
      expect(find.text(TrialsPage.reviewLabel), findsOneWidget);
      await scrollTo(tester, find.text(TrialsPage.endedTitle));
      expect(find.text('No coffee'), findsOneWidget);
      expect(find.text('Done'), findsOneWidget);
    });

    testWidgets('loading, then empty', (tester) async {
      final gate = Completer<List<Map<String, dynamic>>>();
      h.api.on('GET', '/trials', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(h.wrap(const TrialsPage()));
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.complete(<Map<String, dynamic>>[]);
      await tester.pumpAndSettle();
      expect(find.text(TrialsPage.emptyLine), findsOneWidget);
    });

    testWidgets('starting a trial posts what was typed and chosen', (
      tester,
    ) async {
      h.api.on('POST', '/trials', (_) => SystemsFixtures.trial(id: 'tr3'));
      await pump(tester, const TrialsPage());
      await tester.tap(find.text(TrialsPage.startLabel));
      await tester.pumpAndSettle();
      expect(find.byType(TrialEditorPage), findsOneWidget);
      await tester.ensureVisible(find.text(TrialEditorPage.saveLabel));
      await tester.pumpAndSettle();
      await tester.tap(find.text(TrialEditorPage.saveLabel));
      await tester.pumpAndSettle();
      expect(find.text(TrialEditorPage.needTitle), findsOneWidget);
      await scrollTo(
        tester,
        find.widgetWithText(TextField, TrialEditorPage.titleLabel),
      );
      await tester.enterText(
        find.widgetWithText(TextField, TrialEditorPage.titleLabel),
        'Walk after dinner',
      );
      FocusManager.instance.primaryFocus?.unfocus();
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(FilterChip, 'Resting heart rate'));
      await tester.ensureVisible(find.text(TrialEditorPage.saveLabel));
      await tester.pumpAndSettle();
      await tester.tap(find.text(TrialEditorPage.saveLabel));
      await tester.pumpAndSettle();
      final body = lastQuery(h.api, 'POST /trials')!;
      expect(body['title'], 'Walk after dinner');
      expect(body['metricKeys'], ['resting_hr_bpm']);
      expect(body.containsKey('readOn'), isTrue);
      expect(find.byType(TrialEditorPage), findsNothing);
      expect(find.text('Started.'), findsOneWidget);
    });

    testWidgets('marking one done sends the status and words', (tester) async {
      h.api.on(
        'PATCH',
        '/trials/tr1',
        (_) => SystemsFixtures.trial(status: 'done'),
      );
      await pump(tester, const TrialsPage());
      await tester.tap(find.byTooltip(TrialsPage.menuTooltip).first);
      await tester.pumpAndSettle();
      await tester.tap(find.text(TrialsPage.doneLabel));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), 'LDL came down');
      await tester.tap(find.text('End it'));
      await tester.pumpAndSettle();
      expect(lastQuery(h.api, 'PATCH /trials/tr1'), {
        'status': 'done',
        'outcome': 'LDL came down',
      });
    });

    testWidgets('someone else: no start, no menus; 404 is not shared', (
      tester,
    ) async {
      h.viewing.view('p2', displayName: 'Charles');
      await pump(tester, const TrialsPage());
      expect(find.text(TrialsPage.startLabel), findsNothing);
      expect(find.byTooltip(TrialsPage.menuTooltip), findsNothing);
      expect(find.text('Oats instead of a bagel'), findsOneWidget);

      h.api.fail(
        'GET',
        '/trials',
        const ApiException('Not found', statusCode: 404),
      );
      h.viewing.view('p3', displayName: 'Mary');
      await tester.pumpAndSettle();
      expect(find.text(NotSharedView.detailFor('Mary')), findsOneWidget);
    });
  });

  group('household overview', () {
    testBothThemes('each member with only what they share', (
      tester,
      theme,
    ) async {
      h.api.on('GET', '/household/overview', (_) => SystemsFixtures.overview());
      await pump(tester, const HouseholdOverviewPage(), theme: theme);
      expect(find.text(HouseholdOverviewPage.title), findsOneWidget);
      expect(find.text('Ada (You)'), findsOneWidget);
      expect(
        find.text('Labs: latest panel Oct 21, 2026, 2 flagged'),
        findsOneWidget,
      );
      expect(
        find.text(
          'Body: weight 64.2 kg on Nov 9, goal: Build muscle, 1 trial running',
        ),
        findsOneWidget,
      );
      expect(find.text('Sleep: 7 h 35 on Nov 10'), findsOneWidget);
      expect(find.text('Charles'), findsOneWidget);
      expect(find.text('Shares: activity'), findsOneWidget);
      expect(find.text('Steps: 8,421 on Nov 10'), findsOneWidget);
      expect(find.textContaining('Labs:'), findsOneWidget);
    });

    testWidgets('loading, not in a household and failed', (tester) async {
      final gate = Completer<Map<String, dynamic>>();
      h.api.on('GET', '/household/overview', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(h.wrap(const HouseholdOverviewPage()));
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.completeError(const ApiException('Not found', statusCode: 404));
      await tester.pumpAndSettle();
      expect(find.text(HouseholdOverviewPage.noHousehold), findsOneWidget);
    });

    testWidgets('the household section opens it', (tester) async {
      h.api.on('GET', '/household/overview', (_) => SystemsFixtures.overview());
      await h.household.load();
      await pump(
        tester,
        const Scaffold(body: SingleChildScrollView(child: HouseholdSection())),
      );
      await tester.tap(find.text(HouseholdOverviewPage.title));
      await tester.pumpAndSettle();
      expect(find.byType(HouseholdOverviewPage), findsOneWidget);
    });
  });

  group('labs page', () {
    testWidgets('each panel offers its review and the timeline', (
      tester,
    ) async {
      await pump(tester, const Scaffold(body: LabsPage()));
      await scrollTo(tester, find.text(LabsPage.reviewLabel));
      expect(find.text(LabsPage.reviewLabel), findsOneWidget);
      expect(find.text(LabsPage.timelineLabel), findsOneWidget);
    });
  });
}
