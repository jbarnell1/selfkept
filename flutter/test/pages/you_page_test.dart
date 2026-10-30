import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/brand/brand.dart';
import 'package:selfkept/pages/you/you_page.dart';
import 'package:selfkept/utils/api_error.dart';

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

  void stubHousehold() {
    h.api.on('GET', '/me/profile', (_) => Fixtures.person());
    h.api.on('GET', '/household', (_) => Fixtures.household());
    h.api.on(
      'GET',
      '/household/shares',
      (_) => [
        {
          'memberPersonId': 'p2',
          'categories': ['sleep'],
        },
      ],
    );
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => [
        {
          'personId': 'p2',
          'displayName': 'Charles',
          'categories': ['labs', 'body'],
        },
      ],
    );
    h.api.on('GET', '/household/invites', (_) => <Map<String, dynamic>>[]);
  }

  testBothThemes('data: profile, household, sharing switches and about', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    stubHousehold();
    await tester.pumpWidget(h.wrap(const YouPage(), theme: theme));
    await tester.pumpAndSettle();

    expect(find.text('Ada'), findsWidgets);
    expect(find.text('The Lovelaces'), findsOneWidget);
    expect(find.text('2 members'), findsOneWidget);
    expect(find.text('Create invite code'), findsOneWidget);

    await scrollTo(tester, find.text('What Charles can see'));
    expect(find.text('What Charles can see'), findsOneWidget);
    expect(find.byType(SwitchListTile), findsNWidgets(8));

    await scrollTo(tester, find.text(Brand.productName));
    expect(find.textContaining('AGPL-3.0'), findsOneWidget);
    expect(find.text('Support'), findsOneWidget);
  });

  testWidgets('loading: spinners show before the data arrives', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/me/profile',
      (_) => Completer<Map<String, dynamic>>().future,
    );
    h.api.on(
      'GET',
      '/household',
      (_) => Completer<Map<String, dynamic>>().future,
    );
    await tester.pumpWidget(h.wrap(const YouPage()));
    await tester.pump();
    await tester.pump();
    // The profile card already has the signed-in person; the household card waits.
    expect(find.byType(CircularProgressIndicator), findsWidgets);
    expect(find.text('The Lovelaces'), findsNothing);
  });

  testWidgets('error: a failed household load explains and offers a retry', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/me/profile', (_) => Fixtures.person());
    h.api.fail(
      'GET',
      '/household',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    h.api.on('GET', '/household/shares', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => <Map<String, dynamic>>[],
    );
    h.api.on('GET', '/household/invites', (_) => <Map<String, dynamic>>[]);
    await tester.pumpWidget(h.wrap(const YouPage()));
    await tester.pumpAndSettle();
    expect(find.text('The household did not load.'), findsOneWidget);
    expect(find.text('Try again'), findsOneWidget);

    stubHousehold();
    await scrollTo(tester, find.text('Try again'));
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text('The Lovelaces'), findsOneWidget);
  });

  testWidgets('flipping a sharing switch PUTs the full category set', (
    tester,
  ) async {
    await usePhoneSize(tester);
    stubHousehold();
    h.api.on('PUT', '/household/shares/p2', (_) => null);
    await tester.pumpWidget(h.wrap(const YouPage()));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.widgetWithText(SwitchListTile, 'Labs'));
    await tester.tap(find.widgetWithText(SwitchListTile, 'Labs'));
    await tester.pumpAndSettle();
    final put =
        h.api.bodies[h.api.calls.indexOf('PUT /household/shares/p2')] as Map;
    expect(put['categories'], ['sleep', 'labs']);
  });

  testWidgets('Recompute insights posts and confirms in a toast', (
    tester,
  ) async {
    await usePhoneSize(tester);
    stubHousehold();
    h.api.on('POST', '/insights/recompute', (_) => null);
    await tester.pumpWidget(h.wrap(Scaffold(body: const YouPage())));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Recompute insights'));
    await tester.tap(find.text('Recompute insights'));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('POST /insights/recompute'));
    expect(find.textContaining('Recomputing.'), findsOneWidget);
  });

  testWidgets('the server card lists the features the server reports', (
    tester,
  ) async {
    await usePhoneSize(tester);
    stubHousehold();
    h.api.on(
      'GET',
      '/system/version',
      (_) => {
        ...Fixtures.version(),
        'features': ['labs', 'insights.model'],
      },
    );
    h.api.on('GET', '/system/setup-status', (_) => Fixtures.setupStatus());
    await h.startup.run();
    await tester.pumpWidget(h.wrap(const YouPage()));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Features'));
    expect(find.text('insights.model'), findsOneWidget);
    expect(find.text('labs'), findsOneWidget);
  });

  testWidgets('creating an invite shows the code once with a copy button', (
    tester,
  ) async {
    await usePhoneSize(tester);
    stubHousehold();
    h.api.on(
      'POST',
      '/household/invites',
      (_) => {
        'id': 'i1',
        'code': 'KEEP-2468',
        'expiresAt': '2999-01-01T00:00:00Z',
      },
    );
    await tester.pumpWidget(h.wrap(const YouPage()));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Create invite code'));
    await tester.tap(find.text('Create invite code'));
    await tester.pumpAndSettle();
    expect(find.text('KEEP-2468'), findsOneWidget);
    expect(find.byTooltip('Copy code'), findsOneWidget);
  });

  testWidgets('Assistants sits between Sharing and Shared with me', (
    tester,
  ) async {
    await usePhoneSize(tester);
    stubHousehold();
    final router = GoRouter(
      routes: [
        GoRoute(
          path: '/',
          builder: (context, state) => const Scaffold(body: YouPage()),
        ),
        GoRoute(
          path: '/connections',
          builder: (context, state) =>
              const Scaffold(body: Text('connections page')),
        ),
      ],
    );
    await tester.pumpWidget(h.wrap(const SizedBox.shrink(), router: router));
    await tester.pumpAndSettle();

    await scrollTo(tester, find.text('Manage'));
    expect(
      find.text(
        'Connect Open WebUI or another assistant to ask questions about your data.',
      ),
      findsOneWidget,
    );
    final sharing = tester.getTopLeft(find.text('Sharing')).dy;
    final assistants = tester.getTopLeft(find.text('Assistants')).dy;
    final shared = tester.getTopLeft(find.text('Shared with me')).dy;
    expect(sharing < assistants && assistants < shared, isTrue);

    await tester.tap(find.text('Manage'));
    await tester.pumpAndSettle();
    expect(find.text('connections page'), findsOneWidget);
  });

  testBothThemes(
    'account: connect the sign-in provider, then it shows as connected',
    (tester, theme) async {
      await usePhoneSize(tester);
      stubHousehold();
      h.api.on(
        'GET',
        '/auth/sso/link',
        (_) => {'enabled': true, 'name': 'Pocket ID', 'linked': false},
      );
      await tester.pumpWidget(h.wrap(const YouPage(), theme: theme));
      await tester.pumpAndSettle();
      await scrollTo(tester, find.text('Connect Pocket ID'));
      expect(find.text('Connect Pocket ID'), findsOneWidget);

      h.api.on(
        'GET',
        '/auth/sso/link',
        (_) => {
          'enabled': true,
          'name': 'Pocket ID',
          'linked': true,
          'email': 'ada@id.example.test',
        },
      );
      await tester.pumpWidget(
        h.wrap(
          const YouPage(key: ValueKey('back'), ssoResult: 'linked'),
          theme: theme,
        ),
      );
      await tester.pumpAndSettle();
      await scrollTo(tester, find.text('Disconnect Pocket ID'));
      expect(find.text('Pocket ID (ada@id.example.test)'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('account: no provider on the server, no row', (tester) async {
    await usePhoneSize(tester);
    stubHousehold();
    h.api.on(
      'GET',
      '/auth/sso/link',
      (_) => {'enabled': false, 'linked': false},
    );
    await tester.pumpWidget(h.wrap(const YouPage()));
    await tester.pumpAndSettle();
    expect(find.textContaining('Pocket ID'), findsNothing);
    expect(find.text('Connect Pocket ID'), findsNothing);
  });
}
