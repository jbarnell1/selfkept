import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/weekly_review.dart';
import 'package:selfkept/pages/systems/patterns_page.dart';
import 'package:selfkept/pages/systems/readback_pages.dart';
import 'package:selfkept/pages/systems/system_detail_page.dart';
import 'package:selfkept/pages/systems/weekly_review_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/insight_card.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/pump.dart';
import '../helpers/systems_fixtures.dart';

const _notFound = ApiException('Not found', statusCode: 404);

Future<void> use400Wide(WidgetTester tester) async {
  tester.view.physicalSize = const Size(400 * 3, 844 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
}

/// True when the text laid out in a single line: its height is one line of
/// its own style, not two or three.
bool isOneLine(WidgetTester tester, Finder text) {
  final paragraph = tester.renderObject<RenderParagraph>(text);
  final lineHeight = paragraph.getFullHeightForCaret(
    const TextPosition(offset: 0),
  );
  return paragraph.size.height < lineHeight * 1.5;
}

const _down = ApiException('Down', statusCode: 500);

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    await signInWithFeatures(h);
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

  group('system detail', () {
    testBothThemes('readings, lab results and cards', (tester, theme) async {
      h.api.on(
        'GET',
        '/systems/heart',
        (_) => SystemsFixtures.system(detail: true),
      );
      await pump(
        tester,
        const SystemDetailPage(systemKey: 'heart'),
        theme: theme,
      );
      expect(find.text('Heart and fitness'), findsOneWidget);
      expect(find.text(SystemDetailPage.readingsTitle), findsOneWidget);
      expect(find.text('Above your range'), findsOneWidget);
      expect(find.text('Your usual 57 to 59 bpm'), findsOneWidget);
      expect(find.text('66 bpm'), findsOneWidget);
      await scrollTo(tester, find.text('LDL cholesterol'));
      expect(find.text('Oct 21, 2026 · High'), findsOneWidget);
      expect(find.text('Before: 171 mg/dL on Jul 20, 2026'), findsOneWidget);
      expect(find.text('160 mg/dL'), findsOneWidget);
      await scrollTo(tester, find.byType(InsightCard));
      expect(find.byType(InsightCard), findsOneWidget);
    });

    testWidgets('a long unit stays on one line at 400 px', (tester) async {
      final body = SystemsFixtures.system(detail: true);
      final reading = (body['readings'] as List).first as Map<String, dynamic>;
      reading['metric'] = 'respiratory_rate_bpm';
      reading['displayName'] = 'Respiratory rate';
      reading['unit'] = 'breaths/min';
      reading['latestValue'] = 15;
      h.api.on('GET', '/systems/heart', (_) => body);
      await use400Wide(tester);
      await tester.pumpWidget(
        h.wrap(const SystemDetailPage(systemKey: 'heart')),
      );
      await tester.pumpAndSettle();
      final value = find.byKey(
        const ValueKey('reading-value-respiratory_rate_bpm'),
      );
      expect(find.text('15 breaths/min'), findsOneWidget);
      expect(isOneLine(tester, value), isTrue);
      expect(tester.takeException(), isNull);
    });

    testWidgets('loading, then an error with a retry', (tester) async {
      final gate = Completer<Map<String, dynamic>>();
      h.api.on('GET', '/systems/sleep', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(
        h.wrap(const SystemDetailPage(systemKey: 'sleep')),
      );
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.completeError(_down);
      await tester.pumpAndSettle();
      expect(find.text(SystemDetailPage.loadErrorTitle), findsOneWidget);
    });

    testWidgets('someone else: not shared, and nothing opens onward', (
      tester,
    ) async {
      h.api.fail('GET', '/systems/cycle', _notFound);
      h.viewing.view('p2', displayName: 'Charles');
      await pump(tester, const SystemDetailPage(systemKey: 'cycle'));
      expect(lastQuery(h.api, 'GET /systems/cycle'), {'personId': 'p2'});
      expect(find.text(NotSharedView.detailFor('Charles')), findsOneWidget);
    });
  });

  group('patterns', () {
    testBothThemes('the server sentence, counts and the building tags', (
      tester,
      theme,
    ) async {
      h.api.on('GET', '/patterns', (_) => SystemsFixtures.patterns());
      await pump(tester, const PatternsPage(), theme: theme);
      expect(find.text(PatternsPage.title), findsOneWidget);
      expect(
        find.text('A pattern in your own data, not proof.'),
        findsOneWidget,
      );
      expect(find.text('Alcohol and resting heart rate'), findsOneWidget);
      expect(find.textContaining('ran 4 bpm higher'), findsOneWidget);
      expect(find.text('9 nights with, 31 without'), findsOneWidget);
      expect(find.text(PatternsPage.buildingTitle), findsOneWidget);
      expect(
        find.text(
          'A pattern needs at least 8 nights with the tag and 16 without, in the last 120 days.',
        ),
        findsOneWidget,
      );
      expect(find.text('Late meal'), findsOneWidget);
      expect(find.text('3 nights with, 40 without'), findsOneWidget);
    });

    testWidgets('a metric named with an acronym keeps its capitals', (
      tester,
    ) async {
      final body = SystemsFixtures.patterns();
      final first = (body['patterns'] as List).first as Map<String, dynamic>;
      first['metric'] = 'hrv_rmssd_ms';
      first['metricName'] = 'HRV (RMSSD)';
      h.api.on('GET', '/patterns', (_) => body);
      await pump(tester, const PatternsPage());
      expect(find.text('Alcohol and HRV (RMSSD)'), findsOneWidget);
    });

    testWidgets('empty', (tester) async {
      h.api.on(
        'GET',
        '/patterns',
        (_) => SystemsFixtures.patterns(empty: true),
      );
      await pump(tester, const PatternsPage());
      expect(find.text(PatternsPage.emptyLine), findsOneWidget);
    });

    testWidgets('failed', (tester) async {
      h.api.fail('GET', '/patterns', _down);
      await pump(tester, const PatternsPage());
      expect(find.text(PatternsPage.loadErrorTitle), findsOneWidget);
    });
  });

  group('sleep and workouts', () {
    testBothThemes('nights newest first, with stages where recorded', (
      tester,
      theme,
    ) async {
      h.api.on('GET', '/sleep', (_) => SystemsFixtures.sleep());
      await pump(tester, const SleepNightsPage(), theme: theme);
      expect(find.text(SleepNightsPage.title), findsOneWidget);
      expect(find.text('7 h 15'), findsOneWidget);
      expect(
        find.text('Deep 2 h · REM 3 h 15 · Light 2 h · Awake 15 min'),
        findsOneWidget,
      );
      expect(find.text(SleepNightsPage.noStagesText), findsOneWidget);
      final first = tester.getTopLeft(find.text('7 h 15')).dy;
      final second = tester.getTopLeft(find.text('6 h 50')).dy;
      expect(first, lessThan(second));
    });

    testWidgets('no nights: says the window', (tester) async {
      h.api.on('GET', '/sleep', (_) => SystemsFixtures.sleep(empty: true));
      await pump(tester, const SleepNightsPage());
      expect(
        find.text('No nights recorded from Oct 28, 2026 to Nov 10, 2026.'),
        findsOneWidget,
      );
    });

    testBothThemes('workouts with the weekly totals', (tester, theme) async {
      h.api.on('GET', '/workouts', (_) => SystemsFixtures.workouts());
      await pump(tester, const WorkoutsPage(), theme: theme);
      expect(find.text('Week of Nov 9'), findsOneWidget);
      expect(
        find.text('2 workouts, 1 h 45. 1 strength, 1 cardio.'),
        findsOneWidget,
      );
      expect(find.text('Running'), findsOneWidget);
      expect(
        find.text('45 min · 7.2 km · 480 kcal · average 150 bpm, top 171 bpm'),
        findsOneWidget,
      );
    });

    testWidgets('the week label stays on one line at 400 px', (tester) async {
      h.api.on('GET', '/workouts', (_) => SystemsFixtures.workouts());
      await use400Wide(tester);
      await tester.pumpWidget(h.wrap(const WorkoutsPage()));
      await tester.pumpAndSettle();
      expect(find.text('Week of Nov 9'), findsOneWidget);
      expect(
        isOneLine(tester, find.byKey(const ValueKey('week-label-0'))),
        isTrue,
      );
      expect(tester.takeException(), isNull);
    });

    testWidgets('workouts not shared for someone else', (tester) async {
      h.api.fail('GET', '/workouts', _notFound);
      h.viewing.view('p2', displayName: 'Charles');
      await pump(tester, const WorkoutsPage());
      expect(find.text(NotSharedView.title), findsOneWidget);
    });
  });

  group('weekly review', () {
    test('a week target says which days added to it', () {
      expect(
        WeeklyReviewPage.targetLine(
          const ReviewTarget(
            targetId: 't1',
            period: 'week',
            metDays: 2,
            knownDays: 7,
          ),
        ),
        'Logged on 2 of 7 days',
      );
      expect(
        WeeklyReviewPage.targetLine(
          const ReviewTarget(targetId: 't2', metDays: 6, knownDays: 7),
        ),
        '6 of 7 days',
      );
    });

    testBothThemes('each section in the server words', (tester, theme) async {
      h.api.on('GET', '/review/weekly', (_) => SystemsFixtures.review());
      await pump(
        tester,
        WeeklyReviewPage(today: () => DateTime(2026, 11, 10)),
        theme: theme,
      );
      expect(find.text('Nov 2 to Nov 8, 2026'), findsOneWidget);
      expect(find.text('Sleep'), findsWidgets);
      expect(find.text('6 of 7 days'), findsOneWidget);
      expect(
        find.text('82.4 kg to 81.9 kg over 2 readings, 0.5 kg down.'),
        findsOneWidget,
      );
      await scrollTo(tester, find.textContaining('7 nights recorded'));
      await scrollTo(tester, find.textContaining('four weeks before'));
      await scrollTo(tester, find.textContaining('ran 4 bpm higher'));
      expect(find.text(WeeklyReviewPage.notSharedLine), findsNothing);
    });

    testWidgets('a null section is not shared', (tester) async {
      h.api.on(
        'GET',
        '/review/weekly',
        (_) => SystemsFixtures.review(shared: false),
      );
      await pump(tester, WeeklyReviewPage(today: () => DateTime(2026, 11, 10)));
      expect(find.text(WeeklyReviewPage.notSharedLine), findsWidgets);
      expect(find.textContaining('7 nights recorded'), findsOneWidget);
    });

    testWidgets('steps back a week; forward stops at today', (tester) async {
      h.api.on('GET', '/review/weekly', (q) {
        final weekOf = (q as Map?)?['weekOf'];
        return weekOf == '2026-10-26'
            ? SystemsFixtures.review(
                weekOf: '2026-10-26',
                weekEnd: '2026-11-01',
              )
            : SystemsFixtures.review();
      });
      await pump(tester, WeeklyReviewPage(today: () => DateTime(2026, 11, 8)));
      expect(
        tester
            .widget<IconButton>(
              find.ancestor(
                of: find.byIcon(Icons.chevron_right),
                matching: find.byType(IconButton),
              ),
            )
            .onPressed,
        isNull,
      );
      await tester.tap(find.byTooltip(WeeklyReviewPage.previousTooltip));
      await tester.pumpAndSettle();
      expect(lastQuery(h.api, 'GET /review/weekly'), {'weekOf': '2026-10-26'});
      expect(find.text('Oct 26 to Nov 1, 2026'), findsOneWidget);
      expect(
        tester
            .widget<IconButton>(
              find.ancestor(
                of: find.byIcon(Icons.chevron_right),
                matching: find.byType(IconButton),
              ),
            )
            .onPressed,
        isNotNull,
      );
    });

    testWidgets('nothing shared: the not shared view', (tester) async {
      h.api.fail('GET', '/review/weekly', _notFound);
      h.viewing.view('p2', displayName: 'Charles');
      await pump(tester, WeeklyReviewPage(today: () => DateTime(2026, 11, 10)));
      expect(find.text(NotSharedView.detailFor('Charles')), findsOneWidget);
    });

    testWidgets('the Today card shows on a Monday and opens the review', (
      tester,
    ) async {
      h.api.on('GET', '/review/weekly', (_) => SystemsFixtures.review());
      await pump(
        tester,
        Scaffold(
          body: WeeklyReviewTodayCard(today: () => DateTime(2026, 11, 9)),
        ),
      );
      expect(find.text(WeeklyReviewTodayCard.title), findsOneWidget);
      await tester.tap(find.text(WeeklyReviewTodayCard.title));
      await tester.pumpAndSettle();
      expect(find.byType(WeeklyReviewPage), findsOneWidget);
    });

    testWidgets('the Today card stays away midweek and on an older server', (
      tester,
    ) async {
      await pump(
        tester,
        Scaffold(
          body: WeeklyReviewTodayCard(today: () => DateTime(2026, 11, 11)),
        ),
      );
      expect(find.text(WeeklyReviewTodayCard.title), findsNothing);

      h.api.on(
        'GET',
        '/system/version',
        (_) => {'version': '0.1.0', 'minClientVersion': '0.1.0'},
      );
      await h.startup.run();
      await pump(
        tester,
        Scaffold(
          body: WeeklyReviewTodayCard(today: () => DateTime(2026, 11, 9)),
        ),
      );
      expect(find.text(WeeklyReviewTodayCard.title), findsNothing);
    });
  });
}
