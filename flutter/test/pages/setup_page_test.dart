import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/brand/brand.dart';
import 'package:selfkept/pages/public/setup_page.dart';
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
      find.widgetWithText(TextFormField, 'Your name'),
      'Ada',
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Email'),
      'ada@example.test',
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Password'),
      'longenough1',
    );
  }

  testBothThemes('renders the first-admin explanation with the brand name', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const SetupPage(), theme: theme));
    expect(find.text('Set up ${Brand.shortName}.'), findsOneWidget);
    expect(find.textContaining('becomes the admin'), findsOneWidget);
    expect(find.text('Create the admin account'), findsOneWidget);
  });

  testWidgets('validation blocks an empty form without a request', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const SetupPage()));
    await tester.tap(find.text('Create the admin account'));
    await tester.pump();
    expect(find.text('A name, so the household knows you.'), findsOneWidget);
    expect(h.api.calls, isEmpty);
  });

  testWidgets('loading: the button shows a spinner while registering', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on(
      'POST',
      '/auth/register',
      (_) => Future<Map<String, dynamic>>.delayed(
        const Duration(seconds: 5),
        Fixtures.authResponse,
      ),
    );
    await tester.pumpWidget(h.wrap(const SetupPage()));
    await fill(tester);
    await tester.tap(find.text('Create the admin account'));
    await tester.pump();
    expect(h.auth.status, AuthStatus.loading);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    await tester.pump(const Duration(seconds: 6));
  });

  testWidgets('success: registering signs in without an invite code', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('POST', '/auth/register', (_) => Fixtures.authResponse());
    await tester.pumpWidget(h.wrap(const SetupPage()));
    await fill(tester);
    await tester.tap(find.text('Create the admin account'));
    await tester.pumpAndSettle();
    expect(h.auth.isAuthenticated, isTrue);
    final body = h.api.bodies.single as Map;
    expect(body['displayName'], 'Ada');
    expect(body.containsKey('inviteCode'), isFalse);
  });

  testWidgets('error: the server message appears in the attention line', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.fail(
      'POST',
      '/auth/register',
      const ApiException('Registration is closed.', statusCode: 403),
    );
    await tester.pumpWidget(h.wrap(const SetupPage()));
    await fill(tester);
    await tester.tap(find.text('Create the admin account'));
    await tester.pumpAndSettle();
    expect(find.text('Registration is closed.'), findsOneWidget);
    expect(h.auth.status, AuthStatus.error);
  });

  testWidgets('the server address field is tucked away until asked for', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const SetupPage()));
    expect(find.widgetWithText(TextField, 'Server address'), findsNothing);
    await scrollTo(tester, find.text('Server address'));
    await tester.tap(find.text('Server address'));
    await tester.pumpAndSettle();
    expect(find.widgetWithText(TextField, 'Server address'), findsOneWidget);
  });
}
