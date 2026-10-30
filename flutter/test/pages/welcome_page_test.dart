import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/models/setup.dart';
import 'package:selfkept/pages/welcome/welcome_page.dart';
import 'package:selfkept/theme/app_theme.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  Future<void> signIn({
    String role = 'admin',
    List<String> plan = const [],
  }) async {
    h.api.on(
      'POST',
      '/auth/login',
      (_) =>
          Fixtures.authResponse(role: role, setupPlan: plan, setupDone: false),
    );
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('PUT', '/me/setup', (data) {
      final body = data! as Map<String, dynamic>;
      return Fixtures.user(
        role: role,
        setupPlan: [for (final p in body['plan'] as List) p as String],
        setupDone: body['completed'] as bool,
      );
    });
  }

  GoRouter router() => GoRouter(
    initialLocation: '/welcome',
    routes: [
      GoRoute(path: '/welcome', builder: (_, _) => const WelcomePage()),
      GoRoute(
        path: '/welcome/steps',
        builder: (_, _) => const Text('steps page'),
      ),
      GoRoute(path: '/', builder: (_, _) => const Text('today page')),
    ],
  );

  Finder choice(SetupItem item) =>
      find.byKey(ValueKey('choice-${item.wireValue}'));

  setUp(() => h = TestHarness());
  tearDown(() => h.dispose());

  test('suggested ticks only the store of the phone the app runs on', () {
    expect(
      WelcomePage.suggested(isWeb: true, platform: TargetPlatform.android),
      isEmpty,
    );
    expect(
      WelcomePage.suggested(isWeb: false, platform: TargetPlatform.android),
      [SetupItem.healthConnect],
    );
    expect(WelcomePage.suggested(isWeb: false, platform: TargetPlatform.iOS), [
      SetupItem.appleHealth,
    ]);
    expect(
      WelcomePage.suggested(isWeb: false, platform: TargetPlatform.windows),
      isEmpty,
    );
  });

  testBothThemes('the admin sees every question, greeted by first name', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    await signIn();
    await tester.pumpWidget(h.wrap(const WelcomePage(), theme: theme));
    await tester.pumpAndSettle();
    expect(find.text('Welcome, Ada.'), findsOneWidget);
    for (final item in SetupItem.values) {
      await scrollTo(tester, choice(item));
      expect(find.text(item.displayName), findsOneWidget);
    }
    expect(tester.takeException(), isNull);
  });

  testWidgets('a member is not asked about inviting the household', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn(role: 'member');
    await tester.pumpWidget(h.wrap(const WelcomePage()));
    await tester.pumpAndSettle();
    await scrollTo(tester, choice(SetupItem.assistant));
    expect(choice(SetupItem.household), findsNothing);
  });

  testWidgets('earlier answers come back ticked', (tester) async {
    await usePhoneSize(tester);
    await signIn(plan: ['oura']);
    await tester.pumpWidget(h.wrap(const WelcomePage()));
    await tester.pumpAndSettle();
    expect(h.setup.chosen, [SetupItem.oura]);
    final box = tester.widget<Checkbox>(
      find.descendant(
        of: choice(SetupItem.oura),
        matching: find.byType(Checkbox),
      ),
    );
    expect(box.value, isTrue);
  });

  testWidgets('continue saves the answers and opens the steps', (tester) async {
    await usePhoneSize(tester);
    await signIn();
    await tester.pumpWidget(h.wrap(const SizedBox(), router: router()));
    await tester.pumpAndSettle();
    await tester.tap(choice(SetupItem.oura));
    await scrollTo(tester, choice(SetupItem.labs));
    await tester.tap(choice(SetupItem.labs));
    await tester.pump();
    await scrollTo(tester, find.text('Continue'));
    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();

    // Tests run as Android, so Health Connect starts ticked.
    expect(h.api.bodies.last, {
      'plan': ['healthConnect', 'oura', 'labs'],
      'completed': false,
    });
    expect(find.text('steps page'), findsOneWidget);
  });

  testWidgets('continue with nothing ticked finishes and goes to Today', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn();
    await tester.pumpWidget(h.wrap(const SizedBox(), router: router()));
    await tester.pumpAndSettle();
    await tester.tap(choice(SetupItem.healthConnect));
    await tester.pump();
    await scrollTo(tester, find.text('Continue'));
    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();
    expect(h.api.bodies.last, {'plan': <String>[], 'completed': true});
    expect(find.text('today page'), findsOneWidget);
  });

  testWidgets('skip finishes with no answers', (tester) async {
    await usePhoneSize(tester);
    await signIn(plan: ['labs']);
    await tester.pumpWidget(h.wrap(const SizedBox(), router: router()));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Skip for now'));
    await tester.tap(find.text('Skip for now'));
    await tester.pumpAndSettle();
    expect(h.api.bodies.last, {'plan': <String>[], 'completed': true});
    expect(find.text('today page'), findsOneWidget);
  });

  testWidgets('a failed save stays on the page and says why', (tester) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.fail(
      'PUT',
      '/me/setup',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(h.wrap(const SizedBox(), router: router()));
    await tester.pumpAndSettle();
    await tester.tap(choice(SetupItem.oura));
    await scrollTo(tester, find.text('Continue'));
    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();
    expect(find.text('The server had a problem.'), findsOneWidget);
    expect(find.text('steps page'), findsNothing);
  });

  testWidgets('fits a narrow phone without overflow', (tester) async {
    tester.view.physicalSize = const Size(320, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await signIn();
    await tester.pumpWidget(h.wrap(const WelcomePage(), theme: AppTheme.dark));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });
}
