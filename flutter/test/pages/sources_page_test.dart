import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/collectors/health_collector.dart';
import 'package:selfkept/pages/sources_page.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/fake_health_collector.dart';
import '../helpers/pump.dart';

void main() {
  group('on web (no collector)', () {
    late TestHarness h;

    setUp(() async {
      h = TestHarness();
      h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
      await h.auth.login(email: 'a@b', password: 'x');
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(configured: false));
    });
    tearDown(() => h.dispose());

    testBothThemes('data: the notice, the list and the switches', (
      tester,
      theme,
    ) async {
      await usePhoneSize(tester);
      h.api.on(
        'GET',
        '/sources',
        (_) => [
          Fixtures.source(),
          Fixtures.source(id: 's2', kind: 'oura', sampleCount: 1),
        ],
      );
      await tester.pumpWidget(h.wrap(const SourcesPage(), theme: theme));
      await tester.pumpAndSettle();

      expect(find.text(SourcesPage.webNotice), findsOneWidget);
      expect(find.text("Ada's phone"), findsWidgets);
      expect(find.textContaining('Apple Health.'), findsOneWidget);
      expect(find.textContaining('Oura.'), findsOneWidget);
      expect(find.text('120 readings'), findsOneWidget);
      expect(find.text('1 reading'), findsOneWidget);
      expect(find.byType(Switch), findsNWidgets(2));
      expect(find.text('Sync now'), findsNothing);
    });

    testWidgets('loading: a spinner until the list arrives', (tester) async {
      await usePhoneSize(tester);
      h.api.on(
        'GET',
        '/sources',
        (_) => Completer<List<Map<String, dynamic>>>().future,
      );
      await tester.pumpWidget(h.wrap(const SourcesPage()));
      await tester.pump();
      await tester.pump();
      expect(find.byType(CircularProgressIndicator), findsWidgets);
      expect(find.byType(Switch), findsNothing);
    });

    testWidgets('error: explains and retries', (tester) async {
      await usePhoneSize(tester);
      h.api.fail(
        'GET',
        '/sources',
        const ApiException('The server had a problem.', statusCode: 500),
      );
      await tester.pumpWidget(h.wrap(const SourcesPage()));
      await tester.pumpAndSettle();
      expect(find.text('Your sources did not load.'), findsOneWidget);
      expect(find.text('The server had a problem.'), findsOneWidget);

      h.api.on('GET', '/sources', (_) => [Fixtures.source()]);
      await scrollTo(tester, find.text('Try again'));
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(find.text("Ada's phone"), findsOneWidget);
    });

    testWidgets('empty: says so', (tester) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/sources', (_) => <Map<String, dynamic>>[]);
      await tester.pumpWidget(h.wrap(const SourcesPage()));
      await tester.pumpAndSettle();
      expect(find.textContaining('No sources yet'), findsOneWidget);
    });

    testWidgets('the enabled switch PATCHes the source', (tester) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/sources', (_) => [Fixtures.source()]);
      h.api.on('PATCH', '/sources/s1', (data) {
        final body = data as Map<String, dynamic>;
        return {...Fixtures.source(), 'enabled': body['enabled']};
      });
      await tester.pumpWidget(h.wrap(const SourcesPage()));
      await tester.pumpAndSettle();
      await tester.tap(find.byType(Switch));
      await tester.pumpAndSettle();
      final patch =
          h.api.bodies[h.api.calls.indexOf('PATCH /sources/s1')] as Map;
      expect(patch, {'enabled': false});
      expect(h.sources.byId('s1')!.enabled, isFalse);
    });

    testWidgets('built-in sources have no switch', (tester) async {
      await usePhoneSize(tester);
      h.api.on(
        'GET',
        '/sources',
        (_) => [
          Fixtures.source(id: 'c1', kind: 'manual'),
          Fixtures.source(id: 'l1', kind: 'lab_scan'),
        ],
      );
      await tester.pumpWidget(h.wrap(const SourcesPage()));
      await tester.pumpAndSettle();
      expect(find.byType(Switch), findsNothing);
    });
  });

  group('on a phone', () {
    late FakeHealthCollector collector;
    late TestHarness h;

    setUp(() async {
      collector = FakeHealthCollector();
      h = TestHarness(collector: collector);
      h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
      await h.auth.login(email: 'a@b', password: 'x');
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(configured: false));
      h.api.on('GET', '/sources', (_) => <Map<String, dynamic>>[]);
      h.api.on(
        'POST',
        '/sources',
        (_) => Fixtures.source(id: 'src-phone', cursor: null, sampleCount: 0),
      );
      h.api.on('POST', '/ingest/batch', (_) => Fixtures.ingestResult());
    });
    tearDown(() async {
      h.dispose();
      await collector.close();
    });

    testWidgets('connect runs the first sync and shows the sync card', (
      tester,
    ) async {
      await usePhoneSize(tester);
      collector.pages.add(CollectResult(samples: hrSamples(2), cursor: 'c1'));
      await tester.pumpWidget(h.wrap(const SourcesPage()));
      await tester.pumpAndSettle();
      expect(find.text('Connect Apple Health'), findsOneWidget);

      await tester.tap(find.text('Connect Apple Health'));
      await tester.pumpAndSettle();

      expect(find.text('Sync now'), findsOneWidget);
      expect(find.text('Last successful sync'), findsOneWidget);
      expect(find.text('Just now'), findsOneWidget);
      expect(find.text('Nothing'), findsOneWidget);
      expect(find.text("Ada's phone"), findsOneWidget);
    });

    testWidgets('pending rows and the last error are shown', (tester) async {
      await usePhoneSize(tester);
      h.api.fail('POST', '/ingest/batch', const ApiException.network());
      collector.pages.add(CollectResult(samples: hrSamples(3), cursor: 'c1'));
      await tester.pumpWidget(h.wrap(const SourcesPage()));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Connect Apple Health'));
      await tester.pumpAndSettle();

      expect(find.text('3 readings'), findsOneWidget);
      expect(find.text('The server could not be reached.'), findsOneWidget);
      expect(find.text('Sync now'), findsOneWidget);
    });
  });

  group('Oura card', () {
    late TestHarness h;
    late List<Uri> opened;

    setUp(() async {
      opened = [];
      h = TestHarness(
        openUrl: (uri) async {
          opened.add(uri);
          return true;
        },
      );
      h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
      await h.auth.login(email: 'a@b', password: 'x');
      h.api.on('GET', '/sources', (_) => <Map<String, dynamic>>[]);
    });
    tearDown(() => h.dispose());

    Widget page({String? ouraResult, ThemeData? theme}) =>
        h.wrap(SourcesPage(ouraResult: ouraResult), theme: theme);

    testBothThemes('not configured, member: the setup sentence and no button', (
      tester,
      theme,
    ) async {
      await usePhoneSize(tester);
      h.api.on(
        'POST',
        '/auth/login',
        (_) => Fixtures.authResponse(role: 'member'),
      );
      await h.auth.login(email: 'a@b', password: 'x');
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(configured: false));
      await tester.pumpWidget(page(theme: theme));
      await tester.pumpAndSettle();
      expect(find.text('Oura ring'), findsOneWidget);
      expect(find.text(SourcesPage.ouraNotConfigured), findsOneWidget);
      expect(find.text('Connect Oura'), findsNothing);
      expect(h.api.calls, isNot(contains('GET /integrations/oura')));
    });

    testBothThemes(
      'not configured, admin: the guided form saves the application',
      (tester, theme) async {
        await usePhoneSize(tester);
        var configured = false;
        h.api.on(
          'GET',
          '/oura',
          (_) => Fixtures.ouraStatus(configured: configured),
        );
        h.api.on('GET', '/integrations/oura', (_) => Fixtures.ouraApp());
        h.api.on('PUT', '/integrations/oura', (_) {
          configured = true;
          return Fixtures.ouraApp(
            configured: true,
            source: 'app',
            clientId: 'app-1',
          );
        });
        await tester.pumpWidget(page(theme: theme));
        await tester.pumpAndSettle();
        expect(find.text("Open Oura's developer page"), findsOneWidget);
        expect(
          find.text('https://box.tailnet.ts.net/api/oura/callback'),
          findsOneWidget,
        );

        await scrollTo(tester, find.widgetWithText(TextField, 'Client ID'));
        await tester.enterText(
          find.widgetWithText(TextField, 'Client ID'),
          ' app-1 ',
        );
        await tester.enterText(
          find.widgetWithText(TextField, 'Client secret'),
          'shh',
        );
        await scrollTo(tester, find.widgetWithText(FilledButton, 'Save'));
        await tester.tap(find.widgetWithText(FilledButton, 'Save'));
        await tester.pumpAndSettle();

        final i = h.api.calls.indexOf('PUT /integrations/oura');
        expect(h.api.bodies[i], {'clientId': 'app-1', 'clientSecret': 'shh'});
        expect(
          find.text('Connect Oura'),
          findsOneWidget,
          reason: 'status reloads',
        );
        expect(find.text('shh'), findsNothing);
      },
    );

    testWidgets('loading: a spinner in the card', (tester) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/oura', (_) => Completer<Map<String, dynamic>>().future);
      await tester.pumpWidget(page());
      await tester.pump();
      await tester.pump();
      expect(find.text('Oura ring'), findsOneWidget);
      expect(find.byType(CircularProgressIndicator), findsWidgets);
    });

    testWidgets('error: a calm line and a way to check again', (tester) async {
      await usePhoneSize(tester);
      h.api.fail(
        'GET',
        '/oura',
        const ApiException('The server had a problem.', statusCode: 500),
      );
      await tester.pumpWidget(page());
      await tester.pumpAndSettle();
      expect(find.text('The server had a problem.'), findsWidgets);
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus());
      await tester.tap(find.text('Check again'));
      await tester.pumpAndSettle();
      expect(find.text('Connect Oura'), findsOneWidget);
    });

    testBothThemes('not connected: one line and Connect Oura', (
      tester,
      theme,
    ) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus());
      await tester.pumpWidget(page(theme: theme));
      await tester.pumpAndSettle();
      expect(
        find.text(
          "Bring in readiness, sleep stages, HRV and temperature from Oura's cloud.",
        ),
        findsOneWidget,
      );
      expect(find.widgetWithText(FilledButton, 'Connect Oura'), findsOneWidget);
    });

    testWidgets('Connect Oura opens the address the server returns', (
      tester,
    ) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus());
      h.api.on(
        'POST',
        '/oura/connect',
        (_) => {
          'authorizeUrl': 'https://cloud.ouraring.com/oauth/authorize?state=x',
        },
      );
      await tester.pumpWidget(page());
      await tester.pumpAndSettle();
      await tester.tap(find.text('Connect Oura'));
      await tester.pumpAndSettle();
      expect(opened, [
        Uri.parse('https://cloud.ouraring.com/oauth/authorize?state=x'),
      ]);
    });

    testBothThemes('connected with an error: lines, amber note, buttons', (
      tester,
      theme,
    ) async {
      await usePhoneSize(tester);
      h.api.on(
        'GET',
        '/oura',
        (_) => Fixtures.ouraStatus(
          connected: true,
          lastError: 'Oura refused the last pull.',
          sampleCount: 1200,
        ),
      );
      await tester.pumpWidget(page(theme: theme));
      await tester.pumpAndSettle();
      expect(find.textContaining('Connected Oct 20, 2026'), findsOneWidget);
      expect(find.textContaining('Last pulled'), findsOneWidget);
      expect(find.text('1200 readings'), findsOneWidget);
      expect(find.text('Oura refused the last pull.'), findsOneWidget);
      expect(find.widgetWithText(OutlinedButton, 'Pull now'), findsOneWidget);
      expect(find.widgetWithText(TextButton, 'Disconnect'), findsOneWidget);
      expect(find.text('Connect Oura'), findsNothing);
    });

    testWidgets('connected and never pulled says so', (tester) async {
      await usePhoneSize(tester);
      h.api.on(
        'GET',
        '/oura',
        (_) => Fixtures.ouraStatus(connected: true, pulled: false),
      );
      await tester.pumpWidget(page());
      await tester.pumpAndSettle();
      expect(find.text('Not pulled yet'), findsOneWidget);
      expect(find.text('0 readings'), findsOneWidget);
    });

    testWidgets('Pull now syncs, then reloads two seconds later', (
      tester,
    ) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(connected: true));
      h.api.on('POST', '/oura/sync', (_) => null);
      await tester.pumpWidget(page());
      await tester.pumpAndSettle();
      final before = h.api.calls.where((c) => c == 'GET /oura').length;

      await tester.tap(find.text('Pull now'));
      await tester.pump();
      expect(h.api.calls, contains('POST /oura/sync'));
      expect(find.text('Pulling'), findsOneWidget);
      expect(h.api.calls.where((c) => c == 'GET /oura'), hasLength(before));

      await tester.pump(const Duration(seconds: 2));
      await tester.pumpAndSettle();
      expect(h.api.calls.where((c) => c == 'GET /oura'), hasLength(before + 1));
      expect(find.text('Pull now'), findsOneWidget);
    });

    testWidgets('Disconnect confirms, then deletes', (tester) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(connected: true));
      h.api.on('DELETE', '/oura', (_) => null);
      await tester.pumpWidget(page());
      await tester.pumpAndSettle();

      await tester.tap(find.widgetWithText(TextButton, 'Disconnect'));
      await tester.pumpAndSettle();
      expect(
        find.text('Disconnect Oura? Readings already brought in stay.'),
        findsOneWidget,
      );
      await tester.tap(find.text('Keep'));
      await tester.pumpAndSettle();
      expect(h.api.calls, isNot(contains('DELETE /oura')));

      await tester.tap(find.widgetWithText(TextButton, 'Disconnect'));
      await tester.pumpAndSettle();
      await tester.tap(
        find.descendant(
          of: find.byType(AlertDialog),
          matching: find.text('Disconnect'),
        ),
      );
      await tester.pumpAndSettle();
      expect(h.api.calls, contains('DELETE /oura'));
    });

    testWidgets('oura=connected shows the first pull message', (tester) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(connected: true));
      await tester.pumpWidget(page(ouraResult: 'connected'));
      await tester.pump();
      await tester.pump();
      expect(
        find.text('Oura connected. The first pull is under way.'),
        findsOneWidget,
      );
    });

    testWidgets('oura=error shows the try again message', (tester) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus());
      await tester.pumpWidget(page(ouraResult: 'error'));
      await tester.pump();
      await tester.pump();
      expect(find.text('Oura did not connect. Try again.'), findsOneWidget);
    });

    testWidgets('no oura value shows no message', (tester) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus());
      await tester.pumpWidget(page());
      await tester.pumpAndSettle();
      expect(find.byType(SnackBar), findsNothing);
    });

    testWidgets('the /sources route passes the oura value through', (
      tester,
    ) async {
      await usePhoneSize(tester);
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus());
      final router = GoRouter(
        initialLocation: '/sources?oura=error',
        routes: [
          GoRoute(
            path: '/sources',
            builder: (context, state) =>
                SourcesPage(ouraResult: state.uri.queryParameters['oura']),
          ),
        ],
      );
      await tester.pumpWidget(h.wrap(const SizedBox.shrink(), router: router));
      await tester.pump();
      await tester.pump();
      expect(find.text('Oura did not connect. Try again.'), findsOneWidget);
    });
  });
}
