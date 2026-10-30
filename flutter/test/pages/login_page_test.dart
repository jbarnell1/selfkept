import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/pages/public/login_page.dart';
import 'package:selfkept/providers/auth_provider.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() => h = TestHarness());
  tearDown(() => h.dispose());

  Future<void> fill(WidgetTester tester) async {
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Email'),
      'ada@example.test',
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Password'),
      'secret',
    );
  }

  testBothThemes('renders the form and the join link', (tester, theme) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const LoginPage(), theme: theme));
    expect(find.text('Welcome back.'), findsOneWidget);
    expect(find.text('Sign in'), findsOneWidget);
    expect(find.text('Join a household'), findsOneWidget);
  });

  testWidgets('loading: the button shows a spinner while signing in', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on(
      'POST',
      '/auth/login',
      (_) => Future<Map<String, dynamic>>.delayed(
        const Duration(seconds: 5),
        Fixtures.authResponse,
      ),
    );
    await tester.pumpWidget(h.wrap(const LoginPage()));
    await fill(tester);
    await tester.tap(find.text('Sign in'));
    await tester.pump();
    expect(h.auth.isLoading, isTrue);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    await tester.pump(const Duration(seconds: 6));
  });

  testWidgets('success: signing in posts the trimmed email and authenticates', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await tester.pumpWidget(h.wrap(const LoginPage()));
    await fill(tester);
    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();
    expect(h.auth.isAuthenticated, isTrue);
    expect((h.api.bodies.single as Map)['email'], 'ada@example.test');
  });

  testWidgets(
    'error: a rejected sign-in shows the message and stays on the page',
    (tester) async {
      await usePhoneSize(tester);
      h.api.fail(
        'POST',
        '/auth/login',
        const ApiException('Wrong email or password.', statusCode: 401),
      );
      await tester.pumpWidget(h.wrap(const LoginPage()));
      await fill(tester);
      await tester.tap(find.text('Sign in'));
      await tester.pumpAndSettle();
      expect(find.text('Wrong email or password.'), findsOneWidget);
      expect(h.auth.status, AuthStatus.error);
      expect(find.text('Welcome back.'), findsOneWidget);
    },
  );

  testWidgets('an expired session is explained once', (tester) async {
    await usePhoneSize(tester);
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.fireUnauthorized();
    await tester.pumpWidget(h.wrap(const LoginPage()));
    await tester.pumpAndSettle();
    expect(find.textContaining('Your session ended'), findsOneWidget);
  });

  testWidgets('success with a from value goes back to it', (tester) async {
    await usePhoneSize(tester);
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    final router = GoRouter(
      routes: [
        GoRoute(
          path: '/',
          builder: (context, state) =>
              LoginPage(from: state.uri.queryParameters['from']),
        ),
        GoRoute(
          path: '/connect',
          builder: (context, state) => Scaffold(
            body: Text('connect ${state.uri.queryParameters['request']}'),
          ),
        ),
      ],
      initialLocation: '/?from=%2Fconnect%3Frequest%3Dabc',
    );
    await tester.pumpWidget(h.wrap(const SizedBox.shrink(), router: router));
    await fill(tester);
    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();
    expect(find.text('connect abc'), findsOneWidget);
  });

  testWidgets('a from value that leaves the app is ignored', (tester) async {
    await usePhoneSize(tester);
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    final router = GoRouter(
      routes: [
        GoRoute(
          path: '/',
          builder: (context, state) =>
              const LoginPage(from: 'https://evil.test'),
        ),
      ],
    );
    await tester.pumpWidget(h.wrap(const SizedBox.shrink(), router: router));
    await fill(tester);
    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();
    expect(h.auth.isAuthenticated, isTrue);
    expect(find.text('Welcome back.'), findsOneWidget);
  });
}
