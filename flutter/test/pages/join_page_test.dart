import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/public/join_page.dart';
import 'package:selfkept/providers/auth_provider.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() => h = TestHarness());
  tearDown(() => h.dispose());

  Future<void> fill(WidgetTester tester, {bool code = true}) async {
    if (code) {
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Invite code'),
        'ABCD-1234',
      );
    }
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Your name'),
      'Charles',
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Email'),
      'charles@example.test',
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Password'),
      'longenough1',
    );
  }

  testBothThemes(
    'renders the invite explanation and prefills a code from the link',
    (tester, theme) async {
      await usePhoneSize(tester);
      await tester.pumpWidget(
        h.wrap(const JoinPage(initialCode: 'FROM-LINK'), theme: theme),
      );
      expect(find.text('Join a household.'), findsOneWidget);
      expect(find.text('FROM-LINK'), findsOneWidget);
      expect(find.text('Join'), findsOneWidget);
    },
  );

  testWidgets('loading: the button shows a spinner while joining', (
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
    await tester.pumpWidget(h.wrap(const JoinPage()));
    await fill(tester);
    await tester.tap(find.text('Join'));
    await tester.pump();
    expect(h.auth.isLoading, isTrue);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    await tester.pump(const Duration(seconds: 6));
  });

  testWidgets('success: the invite code travels with the registration', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.on('POST', '/auth/register', (_) => Fixtures.authResponse());
    await tester.pumpWidget(h.wrap(const JoinPage()));
    await fill(tester);
    await tester.tap(find.text('Join'));
    await tester.pumpAndSettle();
    expect(h.auth.isAuthenticated, isTrue);
    final body = h.api.bodies.single as Map;
    expect(body['inviteCode'], 'ABCD-1234');
    expect(body['displayName'], 'Charles');
  });

  testWidgets('error: a used or expired code is reported in place', (
    tester,
  ) async {
    await usePhoneSize(tester);
    h.api.fail(
      'POST',
      '/auth/register',
      const ApiException('That invite code has expired.', statusCode: 400),
    );
    await tester.pumpWidget(h.wrap(const JoinPage()));
    await fill(tester);
    await tester.tap(find.text('Join'));
    await tester.pumpAndSettle();
    expect(find.text('That invite code has expired.'), findsOneWidget);
    expect(h.auth.status, AuthStatus.error);
  });

  testWidgets('a missing code is caught before any request', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const JoinPage()));
    await fill(tester, code: false);
    await tester.tap(find.text('Join'));
    await tester.pump();
    expect(find.text('The code from your admin.'), findsOneWidget);
    expect(h.api.calls, isEmpty);
  });
}
