import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/you/profile_section.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;
  String? deviceZone;

  Map<String, dynamic> authWithZone(String zone) {
    final auth = Fixtures.authResponse();
    final user = Map<String, dynamic>.from(auth['user'] as Map);
    user['person'] = {...Fixtures.person(), 'timeZone': zone};
    return {...auth, 'user': user};
  }

  setUp(() {
    deviceZone = 'America/Chicago';
    h = TestHarness(deviceZone: () async => deviceZone);
    h.api.on('PUT', '/me/profile', (data) {
      final body = data as Map;
      return {...Fixtures.person(), 'timeZone': body['timeZone']};
    });
  });
  tearDown(() => h.dispose());

  Future<void> signIn(String zone) async {
    h.api.on('POST', '/auth/login', (_) => authWithZone(zone));
    await h.auth.login(email: 'a@b', password: 'x');
    // Let the quiet update finish.
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);
  }

  test('a UTC profile takes the device zone once after sign-in', () async {
    await signIn('UTC');

    final puts = [
      for (var i = 0; i < h.api.calls.length; i++)
        if (h.api.calls[i] == 'PUT /me/profile') h.api.bodies[i] as Map,
    ];
    expect(puts, hasLength(1));
    expect(puts.single['timeZone'], 'America/Chicago');
    expect(h.auth.user!.person.timeZone, 'America/Chicago');
    expect(h.profile.person!.timeZone, 'America/Chicago');
  });

  test('a zone the person already set is left alone', () async {
    await signIn('Europe/Berlin');
    expect(h.api.calls, isNot(contains('PUT /me/profile')));
    expect(h.auth.user!.person.timeZone, 'Europe/Berlin');
  });

  test('no device zone, or a device on UTC, changes nothing', () async {
    deviceZone = null;
    await signIn('UTC');
    expect(h.api.calls, isNot(contains('PUT /me/profile')));
  });

  test('a failed update stays quiet', () async {
    h.api.fail(
      'PUT',
      '/me/profile',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await signIn('UTC');
    expect(h.profile.error, isNull);
    expect(h.auth.user!.person.timeZone, 'UTC');
  });

  testBothThemes('the picker filters zones and offers the device zone', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    String? picked;
    await tester.pumpWidget(
      MaterialApp(
        theme: theme,
        home: Builder(
          builder: (context) => Scaffold(
            body: TextButton(
              onPressed: () async {
                picked = await showDialog<String>(
                  context: context,
                  builder: (_) => TimeZonePicker(
                    current: 'UTC',
                    catalogue: () async => [
                      'America/Chicago',
                      'America/New_York',
                      'Europe/London',
                      'UTC',
                    ],
                    deviceZone: () async => 'America/Chicago',
                  ),
                );
              },
              child: const Text('open'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    expect(find.text('Use this device: America/Chicago'), findsOneWidget);

    await tester.enterText(find.byType(TextField), 'new york');
    await tester.pumpAndSettle();
    expect(find.text('America/New York'), findsOneWidget);
    expect(find.text('Europe/London'), findsNothing);

    await tester.tap(find.text('America/New York'));
    await tester.pumpAndSettle();
    expect(picked, 'America/New_York');
    expect(tester.takeException(), isNull);
  });
}
