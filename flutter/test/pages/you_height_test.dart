import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/you/you_page.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;
  Map<String, dynamic>? sent;

  setUp(() async {
    sent = null;
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/household', (_) => Fixtures.household());
    h.api.on('GET', '/household/shares', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => <Map<String, dynamic>>[],
    );
    h.api.on('GET', '/household/invites', (_) => <Map<String, dynamic>>[]);
    h.api.on('PUT', '/me/profile', (data) {
      sent = Map<String, dynamic>.from(data as Map);
      return {...Fixtures.person(), ...sent!, 'id': 'p1'};
    });
  });
  tearDown(() => h.dispose());

  Future<void> pumpYou(
    WidgetTester tester, {
    Map<String, dynamic>? person,
  }) async {
    await usePhoneSize(tester);
    h.api.on('GET', '/me/profile', (_) => person ?? Fixtures.person());
    await tester.pumpWidget(h.wrap(const YouPage()));
    await tester.pumpAndSettle();
  }

  Future<void> openEditor(WidgetTester tester) async {
    await tester.tap(find.text('Edit').first);
    await tester.pumpAndSettle();
  }

  Future<void> save(WidgetTester tester) async {
    final button = find.widgetWithText(FilledButton, 'Save');
    await tester.scrollUntilVisible(
      button,
      200,
      scrollable: find.byType(Scrollable).last,
    );
    await tester.pumpAndSettle();
    await tester.tap(button);
    await tester.pumpAndSettle();
  }

  testWidgets('the profile shows the height in the person\'s units', (
    tester,
  ) async {
    await pumpYou(
      tester,
      person: {...Fixtures.person(), 'heightCm': 178.0, 'units': 'imperial'},
    );
    expect(find.text('5 ft 10 in'), findsOneWidget);
  });

  testWidgets('no height reads as not set, and the links are there', (
    tester,
  ) async {
    await pumpYou(tester);
    expect(find.text('Height'), findsOneWidget);
    expect(find.text(AboutYouSection.goalsTitle), findsOneWidget);
    expect(find.text(AboutYouSection.medicinesTitle), findsOneWidget);
  });

  testWidgets('a height typed in cm is sent in cm', (tester) async {
    await pumpYou(tester);
    await openEditor(tester);
    final field = find.widgetWithText(TextField, 'Height (optional)');
    await tester.scrollUntilVisible(
      field,
      200,
      scrollable: find.byType(Scrollable).last,
    );
    await tester.enterText(field, '178');
    await save(tester);
    expect(sent?['heightCm'], 178);
  });

  testWidgets('switching to imperial turns the height into feet and inches', (
    tester,
  ) async {
    await pumpYou(tester, person: {...Fixtures.person(), 'heightCm': 178.0});
    await openEditor(tester);
    await tester.tap(find.text('Imperial'));
    await tester.pumpAndSettle();
    expect(find.widgetWithText(TextField, '5'), findsOneWidget);
    expect(find.widgetWithText(TextField, '10'), findsOneWidget);
    await save(tester);
    expect(sent?['units'], 'imperial');
    expect((sent?['heightCm'] as num).toDouble(), closeTo(177.8, 0.01));
  });

  testWidgets('a height out of range is not sent', (tester) async {
    await pumpYou(tester);
    await openEditor(tester);
    final field = find.widgetWithText(TextField, 'Height (optional)');
    await tester.scrollUntilVisible(
      field,
      200,
      scrollable: find.byType(Scrollable).last,
    );
    await tester.enterText(field, '40');
    await save(tester);
    expect(find.text('Height must be between 90 and 250 cm.'), findsOneWidget);
    expect(sent, isNull);
  });

  testWidgets('an emptied height is cleared with 0', (tester) async {
    await pumpYou(tester, person: {...Fixtures.person(), 'heightCm': 178.0});
    await openEditor(tester);
    final field = find.widgetWithText(TextField, 'Height (optional)');
    await tester.scrollUntilVisible(
      field,
      200,
      scrollable: find.byType(Scrollable).last,
    );
    await tester.enterText(field, '');
    await save(tester);
    expect(sent?['heightCm'], 0);
  });
}
