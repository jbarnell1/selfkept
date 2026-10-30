import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/brand/brand.dart';
import 'package:selfkept/pages/welcome/setup_steps_page.dart';
import 'package:selfkept/widgets/open_webui_steps.dart';
import 'package:selfkept/widgets/setup_step_card.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;
  late List<Uri> opened;

  const everything = [
    'healthConnect',
    'appleHealth',
    'oura',
    'labs',
    'household',
    'assistant',
  ];

  Future<void> signIn({
    String role = 'admin',
    List<String> plan = everything,
    bool done = false,
  }) async {
    h.api.on(
      'POST',
      '/auth/login',
      (_) =>
          Fixtures.authResponse(role: role, setupPlan: plan, setupDone: done),
    );
    await h.auth.login(email: 'a@b', password: 'x');
  }

  /// Every endpoint the steps read, in the "nothing done yet" state.
  void nothingYet() {
    h.api.on('GET', '/sources', (_) => <Map<String, dynamic>>[]);
    h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus());
    h.api.on('GET', '/labs/markers', (_) => <Map<String, dynamic>>[]);
    h.api.on('GET', '/labs/reports', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/household',
      (_) =>
          Fixtures.household()
            ..['members'] = [(Fixtures.household()['members'] as List).first],
    );
    h.api.on('GET', '/household/shares', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => <Map<String, dynamic>>[],
    );
    h.api.on('GET', '/household/invites', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/connect',
      (_) => {
        'assistants': [],
        'tokens': [],
        'mcpUrl': 'https://box.tailnet.ts.net/mcp',
      },
    );
    h.api.on('GET', '/connect/memory', (_) => <Map<String, dynamic>>[]);
  }

  SetupStepCard card(WidgetTester tester, String title) =>
      tester.widget<SetupStepCard>(find.widgetWithText(SetupStepCard, title));

  setUp(() {
    opened = [];
    h = TestHarness(
      openUrl: (uri) async {
        opened.add(uri);
        return true;
      },
    );
  });
  tearDown(() => h.dispose());

  testBothThemes('one card per answer, none done yet', (tester, theme) async {
    await usePhoneSize(tester);
    await signIn();
    nothingYet();
    await tester.pumpWidget(h.wrap(const SetupStepsPage(), theme: theme));
    await tester.pumpAndSettle();

    for (final title in [
      'Health Connect',
      'Apple Health',
      'Oura ring',
      'Blood test results',
      'Your household',
      'A private assistant',
    ]) {
      await scrollTo(tester, find.widgetWithText(SetupStepCard, title));
      expect(card(tester, title).done, isFalse, reason: title);
    }
    expect(tester.takeException(), isNull);
  });

  testWidgets('on web the phone steps give the server address to sign in to', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn(plan: ['healthConnect']);
    nothingYet();
    await tester.pumpWidget(h.wrap(const SetupStepsPage()));
    await tester.pumpAndSettle();
    expect(
      find.textContaining('On your Android phone, install'),
      findsOneWidget,
    );
    expect(find.text(h.settings.currentUrl), findsOneWidget);
    expect(find.textContaining('turn on Health Connect'), findsOneWidget);
    // The download link shows only when brand.yaml names a published app.
    final link = find.text('Get the Android phone app');
    if (Brand.androidAppUrl.isEmpty) {
      expect(link, findsNothing);
    } else {
      await tester.tap(link);
      expect(opened.single.toString(), Brand.androidAppUrl);
    }
  });

  testWidgets('steps tick themselves from what the server already has', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn();
    nothingYet();
    h.api.on(
      'GET',
      '/sources',
      (_) => [Fixtures.source(kind: 'health_connect')],
    );
    h.api.on(
      'GET',
      '/oura',
      (_) => Fixtures.ouraStatus(connected: true, sampleCount: 40),
    );
    h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
    h.api.on('GET', '/household', (_) => Fixtures.household());
    h.api.on('GET', '/connect', (_) => Fixtures.connections());
    await tester.pumpWidget(h.wrap(const SetupStepsPage()));
    await tester.pumpAndSettle();

    expect(card(tester, 'Health Connect').done, isTrue);
    expect(card(tester, 'Apple Health').done, isFalse);
    await scrollTo(tester, find.widgetWithText(SetupStepCard, 'Oura ring'));
    expect(card(tester, 'Oura ring').done, isTrue);
    expect(find.textContaining('40 readings so far'), findsOneWidget);
    await scrollTo(
      tester,
      find.widgetWithText(SetupStepCard, 'Blood test results'),
    );
    expect(card(tester, 'Blood test results').done, isTrue);
    await scrollTo(
      tester,
      find.widgetWithText(SetupStepCard, 'Your household'),
    );
    expect(card(tester, 'Your household').done, isTrue);
    expect(find.text('Charles joined.'), findsOneWidget);
    await scrollTo(
      tester,
      find.widgetWithText(SetupStepCard, 'A private assistant'),
    );
    expect(card(tester, 'A private assistant').done, isTrue);
  });

  testWidgets('connect Oura asks to come back to the steps', (tester) async {
    await usePhoneSize(tester);
    await signIn(plan: ['oura']);
    nothingYet();
    h.api.on(
      'POST',
      '/oura/connect?returnTo=%2Fwelcome%2Fsteps',
      (_) => {
        'authorizeUrl': 'https://cloud.ouraring.com/oauth/authorize?state=x',
      },
    );
    await tester.pumpWidget(h.wrap(const SetupStepsPage()));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Connect Oura'));
    await tester.pumpAndSettle();
    expect(opened.single.host, 'cloud.ouraring.com');
  });

  testWidgets('Oura not set up: the admin gets the form', (tester) async {
    await usePhoneSize(tester);
    await signIn(plan: ['oura']);
    nothingYet();
    h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(configured: false));
    h.api.on('GET', '/integrations/oura', (_) => Fixtures.ouraApp());
    await tester.pumpWidget(h.wrap(const SetupStepsPage()));
    await tester.pumpAndSettle();
    expect(find.text("Open Oura's developer page"), findsOneWidget);
    await tester.tap(find.text("Open Oura's developer page"));
    expect(
      opened.single.toString(),
      'https://cloud.ouraring.com/oauth/applications',
    );
  });

  testWidgets('Oura not set up: a member is told who can do it', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn(role: 'member', plan: ['oura']);
    nothingYet();
    h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(configured: false));
    h.api.on('GET', '/household', (_) => Fixtures.household());
    await tester.pumpWidget(h.wrap(const SetupStepsPage()));
    await tester.pumpAndSettle();
    expect(
      find.textContaining('one-time setup by Ada, who runs'),
      findsOneWidget,
    );
    expect(h.api.calls, isNot(contains('GET /integrations/oura')));
  });

  testWidgets('the assistant step shows the address to paste', (tester) async {
    await usePhoneSize(tester);
    await signIn(plan: ['assistant']);
    nothingYet();
    await tester.pumpWidget(h.wrap(const SetupStepsPage()));
    await tester.pumpAndSettle();
    expect(find.text(OpenWebUiSteps.adminStep), findsOneWidget);
    expect(find.text('https://box.tailnet.ts.net/mcp'), findsOneWidget);
  });

  testWidgets('the household step makes an invite code', (tester) async {
    await usePhoneSize(tester);
    await signIn(plan: ['household']);
    nothingYet();
    h.api.on(
      'POST',
      '/household/invites',
      (_) => {
        'id': 'i1',
        'code': 'ABCDE-FGHJK-LMNPQ-RSTUV',
        'expiresAt': '2026-11-06T10:00:00Z',
      },
    );
    await tester.pumpWidget(h.wrap(const SetupStepsPage()));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Create invite code'));
    await tester.pumpAndSettle();
    expect(find.text('ABCDE-FGHJK-LMNPQ-RSTUV'), findsOneWidget);
  });

  testWidgets('nothing picked says how to add a step', (tester) async {
    await usePhoneSize(tester);
    await signIn(plan: const []);
    await tester.pumpWidget(h.wrap(const SetupStepsPage()));
    await tester.pumpAndSettle();
    expect(find.textContaining('Nothing is picked yet'), findsOneWidget);
    expect(find.byType(SetupStepCard), findsNothing);
  });

  testWidgets('go to Today finishes setup with the saved answers', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn(plan: ['labs']);
    nothingYet();
    h.api.on('PUT', '/me/setup', (data) {
      final body = data! as Map<String, dynamic>;
      return Fixtures.user(
        setupPlan: [for (final p in body['plan'] as List) p as String],
        setupDone: body['completed'] as bool,
      );
    });
    final router = GoRouter(
      initialLocation: '/welcome/steps',
      routes: [
        GoRoute(
          path: '/welcome/steps',
          builder: (_, _) => const SetupStepsPage(),
        ),
        GoRoute(path: '/', builder: (_, _) => const Text('today page')),
      ],
    );
    await tester.pumpWidget(h.wrap(const SizedBox(), router: router));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Go to Today'));
    await tester.tap(find.text('Go to Today'));
    await tester.pumpAndSettle();
    expect(h.api.bodies.last, {
      'plan': ['labs'],
      'completed': true,
    });
    expect(find.text('today page'), findsOneWidget);
  });

  testWidgets('revisited after finishing, Done just leaves', (tester) async {
    await usePhoneSize(tester);
    await signIn(plan: ['labs'], done: true);
    nothingYet();
    final router = GoRouter(
      initialLocation: '/welcome/steps',
      routes: [
        GoRoute(
          path: '/welcome/steps',
          builder: (_, _) => const SetupStepsPage(),
        ),
        GoRoute(path: '/', builder: (_, _) => const Text('today page')),
      ],
    );
    await tester.pumpWidget(h.wrap(const SizedBox(), router: router));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text('Done'));
    await tester.tap(find.text('Done'));
    await tester.pumpAndSettle();
    expect(h.api.calls, isNot(contains('PUT /me/setup')));
    expect(find.text('today page'), findsOneWidget);
  });

  testWidgets('an Oura error from the round trip is said once', (tester) async {
    await usePhoneSize(tester);
    await signIn(plan: ['oura']);
    nothingYet();
    await tester.pumpWidget(h.wrap(const SetupStepsPage(ouraResult: 'error')));
    await tester.pumpAndSettle();
    expect(find.text('Oura did not connect. Try again.'), findsOneWidget);
  });

  testWidgets('fits a narrow phone without overflow', (tester) async {
    tester.view.physicalSize = const Size(320, 4000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await signIn();
    nothingYet();
    h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(configured: false));
    h.api.on('GET', '/integrations/oura', (_) => Fixtures.ouraApp());
    await tester.pumpWidget(h.wrap(const SetupStepsPage()));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });
}
