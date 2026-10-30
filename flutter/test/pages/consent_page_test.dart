import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/pages/consent_page.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;
  late List<Uri> opened;

  setUp(() async {
    opened = [];
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
  });
  tearDown(() => h.dispose());

  /// A router so "Go to Today" and the return to /connections can be seen.
  Widget app({String? requestId = 'abc', ThemeData? theme}) {
    final router = GoRouter(
      routes: [
        GoRoute(
          path: '/',
          builder: (context, state) => ConsentPage(
            requestId: requestId,
            opener: (uri) async {
              opened.add(uri);
              return true;
            },
          ),
        ),
        GoRoute(
          path: '/connections',
          builder: (context, state) =>
              const Scaffold(body: Text('connections page')),
        ),
        GoRoute(
          path: '/today',
          builder: (context, state) => const Scaffold(body: Text('today')),
        ),
      ],
    );
    return h.wrap(const SizedBox.shrink(), theme: theme, router: router);
  }

  void answerWithRequest() {
    h.api.on('GET', '/oauth/request/abc', (_) => Fixtures.consentRequest());
  }

  testBothThemes('data: client, redirect host, scopes and who', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    answerWithRequest();
    await tester.pumpWidget(app(theme: theme));
    await tester.pumpAndSettle();

    expect(find.text('Open WebUI wants to connect'), findsOneWidget);
    expect(
      find.text('It will return you to webui.example.test.'),
      findsOneWidget,
    );
    expect(find.text('Read your health data'), findsOneWidget);
    expect(find.text('Remember notes for you'), findsOneWidget);
    expect(find.byIcon(Icons.check), findsNWidgets(2));
    expect(find.text('As Ada'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Allow'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Deny'), findsOneWidget);
  });

  testWidgets('loading: a spinner while the request is fetched', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/oauth/request/abc',
      (_) => Completer<Map<String, dynamic>>().future,
    );
    await tester.pumpWidget(app());
    await tester.pump();
    await tester.pump();
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.text('Allow'), findsNothing);
  });

  testWidgets('Allow posts approve true and opens the redirect URL', (
    tester,
  ) async {
    await usePhoneSize(tester);
    answerWithRequest();
    h.api.on(
      'POST',
      '/oauth/request/abc/decision',
      (_) => {'redirectUrl': 'https://webui.example.test/cb?code=1&state=s'},
    );
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    await tester.tap(find.text('Allow'));
    await tester.pumpAndSettle();

    final i = h.api.calls.indexOf('POST /oauth/request/abc/decision');
    expect(h.api.bodies[i], {'approve': true});
    expect(opened, [Uri.parse('https://webui.example.test/cb?code=1&state=s')]);
    // On a phone the app comes back to the Assistants page.
    expect(find.text('connections page'), findsOneWidget);
  });

  testWidgets('Deny posts approve false', (tester) async {
    await usePhoneSize(tester);
    answerWithRequest();
    h.api.on(
      'POST',
      '/oauth/request/abc/decision',
      (_) => {
        'redirectUrl': 'https://webui.example.test/cb?error=access_denied',
      },
    );
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    await tester.tap(find.text('Deny'));
    await tester.pumpAndSettle();

    final i = h.api.calls.indexOf('POST /oauth/request/abc/decision');
    expect(h.api.bodies[i], {'approve': false});
    expect(opened, hasLength(1));
  });

  testWidgets('while deciding both buttons are disabled with a spinner', (
    tester,
  ) async {
    await usePhoneSize(tester);
    answerWithRequest();
    final decision = Completer<Map<String, dynamic>>();
    h.api.on('POST', '/oauth/request/abc/decision', (_) => decision.future);
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    await tester.tap(find.text('Allow'));
    await tester.pump();
    expect(
      tester
          .widget<FilledButton>(find.widgetWithText(FilledButton, 'Allow'))
          .onPressed,
      isNull,
    );
    expect(
      tester
          .widget<OutlinedButton>(find.widgetWithText(OutlinedButton, 'Deny'))
          .onPressed,
      isNull,
    );
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    decision.complete({'redirectUrl': 'https://webui.example.test/cb'});
    await tester.pumpAndSettle();
  });

  testWidgets('a failed decision says so and lets the person try again', (
    tester,
  ) async {
    await usePhoneSize(tester);
    answerWithRequest();
    h.api.fail(
      'POST',
      '/oauth/request/abc/decision',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    await tester.tap(find.text('Allow'));
    await tester.pumpAndSettle();
    expect(find.text('The server had a problem.'), findsOneWidget);
    expect(
      tester
          .widget<FilledButton>(find.widgetWithText(FilledButton, 'Allow'))
          .onPressed,
      isNotNull,
    );
    expect(opened, isEmpty);
  });

  testWidgets('an address that could run script is refused', (tester) async {
    await usePhoneSize(tester);
    answerWithRequest();
    h.api.on(
      'POST',
      '/oauth/request/abc/decision',
      (_) => {'redirectUrl': 'javascript:alert(1)'},
    );
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    await tester.tap(find.text('Allow'));
    await tester.pumpAndSettle();
    expect(opened, isEmpty);
    expect(find.text('Allow'), findsOneWidget);
  });

  testWidgets('expired: a 404 shows the calm message and a way out', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.fail(
      'GET',
      '/oauth/request/abc',
      const ApiException('Not found.', statusCode: 404),
    );
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    expect(find.text(ConsentPage.expired), findsOneWidget);
    expect(find.text('Allow'), findsNothing);
    expect(find.widgetWithText(OutlinedButton, 'Go to Today'), findsOneWidget);
  });

  testWidgets('a decision that finds the request gone shows the message', (
    tester,
  ) async {
    await usePhoneSize(tester);
    answerWithRequest();
    h.api.fail(
      'POST',
      '/oauth/request/abc/decision',
      const ApiException('Not found.', statusCode: 404),
    );
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    await tester.tap(find.text('Allow'));
    await tester.pumpAndSettle();
    expect(find.text(ConsentPage.expired), findsOneWidget);
  });

  testWidgets('missing request parameter shows the message, no call', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(app(requestId: null));
    await tester.pumpAndSettle();
    expect(find.text(ConsentPage.expired), findsOneWidget);
    expect(h.api.calls.where((c) => c.contains('/oauth/')), isEmpty);
  });

  testWidgets('an empty request parameter is the same as missing', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(app(requestId: ''));
    await tester.pumpAndSettle();
    expect(find.text(ConsentPage.expired), findsOneWidget);
  });

  testWidgets('Go to Today leaves the page', (tester) async {
    await usePhoneSize(tester);
    final router = GoRouter(
      routes: [
        GoRoute(
          path: '/',
          builder: (context, state) => const Scaffold(body: Text('today')),
        ),
        GoRoute(
          path: '/connect',
          builder: (context, state) => const ConsentPage(),
        ),
      ],
      initialLocation: '/connect',
    );
    await tester.pumpWidget(h.wrap(const SizedBox.shrink(), router: router));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Go to Today'));
    await tester.pumpAndSettle();
    expect(find.text('today'), findsOneWidget);
  });
}
