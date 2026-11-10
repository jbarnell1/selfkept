import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/pages/systems/context_section.dart';
import 'package:selfkept/pages/systems/system_detail_page.dart';
import 'package:selfkept/pages/systems_page.dart';
import 'package:selfkept/pages/trends_page.dart';
import 'package:selfkept/providers/systems_scope.dart';
import 'package:selfkept/router/systems_routes.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/person_switcher.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';
import '../helpers/systems_fixtures.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    await signInWithFeatures(h);
    h.api.on('GET', '/systems', (_) => SystemsFixtures.systems());
    h.api.on('GET', '/context', (_) => SystemsFixtures.context());
  });
  tearDown(() => h.dispose());

  Future<void> pumpPage(WidgetTester tester, {ThemeData? theme}) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(const Scaffold(body: SystemsPage()), theme: theme),
    );
    await tester.pumpAndSettle();
  }

  GoRouter routerAt(String location) => GoRouter(
    initialLocation: location,
    routes: [
      GoRoute(
        path: '/systems',
        builder: (_, _) => const Scaffold(body: SystemsPage()),
      ),
      ...systemsRoutes(),
      GoRoute(
        path: '/labs',
        builder: (_, _) => const Scaffold(body: Text('labs page')),
      ),
      GoRoute(
        path: '/trends/:metric',
        builder: (_, state) =>
            Scaffold(body: Text('trend ${state.pathParameters['metric']}')),
      ),
      GoRoute(
        path: '/labs/marker/:key',
        builder: (_, state) =>
            Scaffold(body: Text('marker ${state.pathParameters['key']}')),
      ),
    ],
  );

  testBothThemes('data: labs, what is going on, systems and the way on', (
    tester,
    theme,
  ) async {
    await pumpPage(tester, theme: theme);
    expect(find.text('Systems'), findsOneWidget);
    expect(find.text('Labs'), findsOneWidget);
    expect(
      find.text(
        '3 panels. The latest, Oct 21, 2026: 2 of 22 flagged high or low.',
      ),
      findsOneWidget,
    );
    expect(find.text(ContextSection.title), findsOneWidget);
    expect(find.text('You noted an illness on Nov 8, 2026'), findsOneWidget);
    expect(find.text('Streaming cold'), findsOneWidget);
    expect(find.text('Since Nov 8, 2026'), findsOneWidget);
    expect(find.text('From your record'), findsOneWidget);
    expect(find.text('Heart and fitness'), findsOneWidget);
    expect(
      find.text(
        'One card worth a look. Resting heart rate is above your usual range.',
      ),
      findsOneWidget,
    );
    await scrollTo(tester, find.text(SystemsPage.timelineTitle));
    expect(find.text(SystemsPage.reviewTitle), findsOneWidget);
    expect(find.text(SystemsPage.patternsTitle), findsOneWidget);
    expect(find.text(SystemsPage.trialsTitle), findsOneWidget);
    expect(find.text(SystemsPage.trendsTitle), findsOneWidget);
  });

  testWidgets('every trend opens the trends list', (tester) async {
    h.api.on('GET', '/trends', (_) => Fixtures.trends());
    await pumpPage(tester);
    await scrollTo(tester, find.text(SystemsPage.trendsTitle));
    await tester.tap(find.text(SystemsPage.trendsTitle));
    await tester.pumpAndSettle();
    expect(find.byType(TrendsPage), findsOneWidget);
    expect(find.text('Steps'), findsOneWidget);
  });

  testWidgets('loading, then the list', (tester) async {
    final gate = Completer<Map<String, dynamic>>();
    h.api.on('GET', '/systems', (_) => gate.future);
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const Scaffold(body: SystemsPage())));
    await pumpFrames(tester);
    expect(find.byType(LoadingView), findsWidgets);
    gate.complete(SystemsFixtures.systems());
    await tester.pumpAndSettle();
    expect(find.text('Heart and fitness'), findsOneWidget);
  });

  testWidgets('a failed load says so and retries', (tester) async {
    h.api.fail('GET', '/systems', const ApiException('Down', statusCode: 500));
    await pumpPage(tester);
    expect(find.text(SystemsPage.loadErrorTitle), findsOneWidget);
    h.api.on('GET', '/systems', (_) => SystemsFixtures.systems());
    await scrollTo(tester, find.text('Try again'));
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text('Heart and fitness'), findsOneWidget);
  });

  testWidgets('empty: a quiet line', (tester) async {
    h.api.on(
      'GET',
      '/systems',
      (_) => {'day': '2026-11-10', 'systems': <Object>[], 'labs': null},
    );
    await pumpPage(tester);
    expect(find.text(SystemsPage.emptyLine), findsOneWidget);
  });

  testWidgets('an older server keeps Labs and the trends, with a note', (
    tester,
  ) async {
    h.api.on(
      'GET',
      '/system/version',
      (_) => {
        ...Fixtures.version(),
        'features': ['labs.reports'],
      },
    );
    await h.startup.run();
    h.api.on('GET', '/trends', (_) => Fixtures.trends());
    await pumpPage(tester);
    expect(find.text(SystemsPage.labsLine), findsOneWidget);
    expect(find.text(SystemsPage.updateNote), findsOneWidget);
    expect(find.byType(TrendsPage), findsOneWidget);
    expect(find.text(ContextSection.title), findsNothing);
    expect(h.api.calls, isNot(contains('GET /systems')));
    expect(h.api.calls, isNot(contains('GET /context')));
  });

  testWidgets('someone else: read only, and their 404 is not shared', (
    tester,
  ) async {
    sharesFromCharles(h, categories: ['vitals']);
    await pumpPage(tester);
    h.api.fail(
      'GET',
      '/context',
      const ApiException('Not found', statusCode: 404),
    );
    await tester.tap(find.widgetWithText(ChoiceChip, 'Charles'));
    await tester.pumpAndSettle();
    expect(lastQuery(h.api, 'GET /systems'), {'personId': 'p2'});
    expect(find.text(PersonSwitcher.readOnlyLine('Charles')), findsOneWidget);
    expect(find.text(ContextSection.notSharedLine), findsOneWidget);
    expect(find.text(ContextSection.addLabel), findsNothing);
    expect(find.byTooltip(ContextSection.menuTooltip), findsNothing);

    h.api.fail(
      'GET',
      '/systems',
      const ApiException('Not found', statusCode: 404),
    );
    await tester.tap(find.widgetWithText(ChoiceChip, 'You'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ChoiceChip, 'Charles'));
    await tester.pumpAndSettle();
    expect(find.text(NotSharedView.title), findsOneWidget);
    expect(find.text(NotSharedView.detailFor('Charles')), findsOneWidget);
  });

  testWidgets('adding context saves at once, and Undo takes it back out', (
    tester,
  ) async {
    h.api.on('POST', '/context', (_) => SystemsFixtures.event(id: 'e2'));
    h.api.on('DELETE', '/context/e2', (_) => null);
    await pumpPage(tester);
    await tester.tap(find.text(ContextSection.addLabel));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ChoiceChip, 'Travel'));
    await tester.enterText(find.byType(TextField), 'Lisbon');
    await tester.tap(find.text(ContextEditorSheet.saveLabel));
    await tester.pumpAndSettle();
    expect(lastQuery(h.api, 'POST /context'), {
      'kind': 'travel',
      'startedOn': '2026-11-10',
      'note': 'Lisbon',
    });
    expect(find.text(ContextSection.savedMessage), findsOneWidget);
    await tester.tap(find.text(ContextSection.undoLabel));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /context/e2'));
    expect(find.text(ContextSection.undoneMessage), findsOneWidget);
  });

  testWidgets('ending an event uses the server day, and Undo reopens it', (
    tester,
  ) async {
    h.api.on('PATCH', '/context/e1', (_) => SystemsFixtures.event());
    await pumpPage(tester);
    await tester.tap(find.byTooltip(ContextSection.menuTooltip).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text(ContextSection.endLabel));
    await tester.pumpAndSettle();
    expect(lastQuery(h.api, 'PATCH /context/e1'), {'endedOn': '2026-11-10'});
    await tester.tap(find.text(ContextSection.undoLabel));
    await tester.pumpAndSettle();
    expect(lastQuery(h.api, 'PATCH /context/e1'), {'reopen': true});
  });

  testWidgets('removing an event, then Undo puts it back', (tester) async {
    h.api.on('DELETE', '/context/e1', (_) => null);
    h.api.on('POST', '/context', (_) => SystemsFixtures.event(id: 'e3'));
    await pumpPage(tester);
    await tester.tap(find.byTooltip(ContextSection.menuTooltip).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text(ContextSection.removeLabel));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /context/e1'));
    expect(find.text(ContextSection.removedMessage), findsOneWidget);
    await tester.tap(find.text(ContextSection.undoLabel));
    await tester.pumpAndSettle();
    expect(lastQuery(h.api, 'POST /context'), {
      'kind': 'illness',
      'startedOn': '2026-11-08',
      'note': 'Streaming cold',
    });
  });

  testWidgets('a system opens its detail, and a reading its trend', (
    tester,
  ) async {
    h.api.on(
      'GET',
      '/systems/heart',
      (_) => SystemsFixtures.system(detail: true),
    );
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(const SizedBox.shrink(), router: routerAt('/systems')),
    );
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Heart and fitness'));
    await tester.tap(find.text('Heart and fitness'));
    await tester.pumpAndSettle();
    expect(find.byType(SystemDetailPage), findsOneWidget);
    await tester.tap(find.text('Resting heart rate').first);
    await tester.pumpAndSettle();
    expect(find.text('trend resting_hr_bpm'), findsOneWidget);
  });

  testWidgets('the Labs entry opens Labs', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(const SizedBox.shrink(), router: routerAt('/systems')),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Labs'));
    await tester.pumpAndSettle();
    expect(find.text('labs page'), findsOneWidget);
  });

  test('the feature names match the server', () {
    expect(SystemsFeatures.contextPatterns, 'context.patterns');
    expect(SystemsFeatures.labsReview, 'labs.review');
  });
}
