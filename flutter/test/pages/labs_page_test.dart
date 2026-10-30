import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/pages/labs_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/lab_marker_tile.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

GoRouter _router() => GoRouter(
  routes: [
    GoRoute(
      path: '/',
      builder: (context, state) => const Scaffold(body: LabsPage()),
    ),
    GoRoute(
      path: '/labs/add',
      builder: (context, state) => const Scaffold(body: Text('add page')),
    ),
    GoRoute(
      path: '/labs/marker/:key',
      builder: (context, state) =>
          Scaffold(body: Text('marker ${state.pathParameters['key']}')),
    ),
    GoRoute(
      path: '/you',
      builder: (context, state) => const Scaffold(body: Text('you page')),
    ),
  ],
);

void main() {
  late TestHarness h;

  Future<void> signIn({String sex = 'female'}) async {
    final user = Fixtures.user();
    (user['person'] as Map<String, dynamic>)['sexAtBirth'] = sex;
    h.api.on(
      'POST',
      '/auth/login',
      (_) => {...Fixtures.authResponse(), 'user': user},
    );
    await h.auth.login(email: 'a@b', password: 'x');
  }

  setUp(() async {
    h = TestHarness();
    await signIn();
    h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
    h.api.on('GET', '/labs/reports', (_) => [Fixtures.labReport()]);
  });
  tearDown(() => h.dispose());

  Widget page({ThemeData? theme}) =>
      h.wrap(const Scaffold(body: LabsPage()), theme: theme);

  testWidgets('loading shows the spinner', (tester) async {
    await usePhoneSize(tester);
    final completer = Completer<List<Map<String, dynamic>>>();
    h.api.on('GET', '/labs/markers', (_) => completer.future);
    await tester.pumpWidget(page());
    await pumpFrames(tester);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.text('Labs'), findsOneWidget);
    completer.complete(Fixtures.labMarkers());
    await tester.pumpAndSettle();
    expect(find.byType(CircularProgressIndicator), findsNothing);
  });

  testWidgets('error shows a retry that loads the data', (tester) async {
    await usePhoneSize(tester);
    h.api.fail(
      'GET',
      '/labs/markers',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(page());
    await tester.pumpAndSettle();
    expect(find.text('Labs did not load.'), findsOneWidget);
    expect(find.text('The server had a problem.'), findsOneWidget);
    expect(find.text('Add results'), findsNothing);

    h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text('Labs did not load.'), findsNothing);
    expect(find.byType(LabMarkerTile), findsWidgets);
  });

  testWidgets('empty: the sentence, the note and one Add results button', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/labs/markers',
      (_) => [Fixtures.labMarker(latestValue: null, severity: null)],
    );
    h.api.on('GET', '/labs/reports', (_) => <Map<String, dynamic>>[]);
    await tester.pumpWidget(page());
    await tester.pumpAndSettle();
    expect(find.text('Your first panel will land here.'), findsOneWidget);
    expect(
      find.text('Add a photo of your results, or type them in.'),
      findsOneWidget,
    );
    expect(find.text('Add results'), findsOneWidget);
    expect(find.byType(LabMarkerTile), findsNothing);
    expect(find.text('Clinician summary'), findsNothing);
  });

  testBothThemes('data: groups, tiles, clinician words and panels', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(page(theme: theme));
    await tester.pumpAndSettle();

    expect(find.text('Labs'), findsOneWidget);
    expect(find.text('Add results'), findsOneWidget);
    for (final group in ['Hormones', 'Thyroid', 'Heart', 'Metabolic']) {
      expect(find.text(group), findsOneWidget);
    }
    expect(find.text('Kidney'), findsNothing);
    expect(find.text('Testosterone'), findsOneWidget);
    expect(find.text('Estradiol'), findsNothing);
    expect(find.text('TSH'), findsOneWidget);
    expect(find.text('No result yet'), findsOneWidget);
    // A marker with no result is a name in a quiet line, not an empty tile.
    expect(find.byKey(const ValueKey('not-yet-lab.tsh')), findsOneWidget);
    expect(find.widgetWithText(LabMarkerTile, 'TSH'), findsNothing);
    expect(find.text('ApoB'), findsOneWidget);
    expect(find.text('TC/HDL ratio'), findsOneWidget);
    expect(find.text('Worth raising with a clinician'), findsOneWidget);
    expect(find.text('Above range'), findsOneWidget);

    // Groups follow the panel order.
    final hormones = tester.getTopLeft(find.text('Hormones')).dy;
    final thyroid = tester.getTopLeft(find.text('Thyroid')).dy;
    final heart = tester.getTopLeft(find.text('Heart')).dy;
    expect(hormones, lessThan(thyroid));
    expect(thyroid, lessThan(heart));

    await scrollTo(tester, find.text('Panels'));
    expect(find.text('October 24, 2026'), findsOneWidget);
    expect(find.textContaining('Rythm'), findsOneWidget);
    expect(find.textContaining('2 results'), findsOneWidget);
    await scrollTo(tester, find.text('Clinician summary'));
    expect(find.byIcon(Icons.picture_as_pdf), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('two tile columns on a phone, four on a laptop', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(page());
    await tester.pumpAndSettle();
    final apob = tester.getRect(find.widgetWithText(LabMarkerTile, 'ApoB'));
    final crp = tester.getRect(find.widgetWithText(LabMarkerTile, 'hs-CRP'));
    // ApoB and hs-CRP share a row of two on a phone.
    expect(apob.top, crp.top);
    expect(apob.left, lessThan(crp.left));
    expect(LabsPage.columnsFor(390), 2);
    expect(LabsPage.columnsFor(700), 3);
    expect(LabsPage.columnsFor(1100), 4);

    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    await tester.pumpAndSettle();
    final wideApob = tester.getRect(find.widgetWithText(LabMarkerTile, 'ApoB'));
    final wideCrp = tester.getRect(
      find.widgetWithText(LabMarkerTile, 'hs-CRP'),
    );
    expect(wideApob.top, wideCrp.top);
    expect(wideApob.width, lessThan(apob.width + 200));
    expect(tester.takeException(), isNull);
  });

  testWidgets('unspecified sex shows the card and it opens You', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn(sex: 'unspecified');
    await tester.pumpWidget(h.wrap(const SizedBox.shrink(), router: _router()));
    await tester.pumpAndSettle();
    expect(find.text(LabsPage.sexCardText), findsOneWidget);
    await tester.tap(find.text('Open You'));
    await tester.pumpAndSettle();
    expect(find.text('you page'), findsOneWidget);
  });

  testWidgets('a known sex hides the card', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(page());
    await tester.pumpAndSettle();
    expect(find.text(LabsPage.sexCardText), findsNothing);
    expect(find.text('Open You'), findsNothing);
  });

  testWidgets('Add results and a tile navigate', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const SizedBox.shrink(), router: _router()));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Add results'));
    await tester.pumpAndSettle();
    expect(find.text('add page'), findsOneWidget);

    final router = GoRouter.of(tester.element(find.text('add page')));
    router.pop();
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(LabMarkerTile, 'ApoB'));
    await tester.pumpAndSettle();
    expect(find.text('marker lab.apob'), findsOneWidget);
  });

  testWidgets('deleting a panel asks first, and Keep leaves it alone', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(page());
    await tester.pumpAndSettle();
    await scrollTo(tester, find.byTooltip('Delete panel'));

    await tester.tap(find.byTooltip('Delete panel'));
    await tester.pumpAndSettle();
    expect(find.text('Delete this panel?'), findsOneWidget);
    expect(find.text('Its results leave your trends too.'), findsOneWidget);
    await tester.tap(find.text('Keep'));
    await tester.pumpAndSettle();
    expect(h.api.calls, isNot(contains('DELETE /labs/reports/r1')));
    expect(find.text('October 24, 2026'), findsOneWidget);

    h.api.on('DELETE', '/labs/reports/r1', (_) => null);
    h.api.on('GET', '/labs/reports', (_) => <Map<String, dynamic>>[]);
    await tester.tap(find.byTooltip('Delete panel'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Delete'));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /labs/reports/r1'));
    expect(find.text('October 24, 2026'), findsNothing);
    expect(find.text('Panels'), findsNothing);
  });

  testWidgets('the clinician summary failing says so', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(page());
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Clinician summary'));
    await tester.tap(find.text('Clinician summary'));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('GET /labs/summary.pdf'));
    expect(find.text('The summary did not download.'), findsOneWidget);
  });
}
