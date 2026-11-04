import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/you/server_section.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/retention_card.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  Future<void> signIn({String role = 'admin'}) async {
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse(role: role));
    await h.auth.login(email: 'a@b', password: 'x');
  }

  Map<String, dynamic> retention({
    int months = 12,
    String source = 'default',
    bool folded = true,
  }) => {
    'rawMonths': months,
    'source': source,
    'minimumMonths': 4,
    'rawSince': months == 0 ? null : '2025-11-01',
    'months': [
      if (folded) ...[
        {
          'month': '2025-10-01',
          'rolledAt': '2026-11-04T04:00:00Z',
          'rawRows': 1200000,
          'minuteRows': 43000,
        },
        {
          'month': '2025-09-01',
          'rolledAt': '2026-11-04T04:00:00Z',
          'rawRows': 1100000,
          'minuteRows': 42000,
        },
      ],
    ],
  };

  setUp(() => h = TestHarness());
  tearDown(() => h.dispose());

  testBothThemes('the admin sees the window and what was folded', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.on('GET', '/retention', (_) => retention());
    await tester.pumpWidget(h.wrap(const RetentionCard(), theme: theme));
    await tester.pumpAndSettle();
    expect(find.text('Readings kept'), findsOneWidget);
    expect(find.text('1 year'), findsOneWidget);
    expect(find.text(RetentionCard.explainer), findsOneWidget);
    expect(
      find.text('2 months are folded, back to September 2025.'),
      findsOneWidget,
    );
    expect(find.text(RetentionCard.runLabel), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a new install has nothing folded', (tester) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.on('GET', '/retention', (_) => retention(folded: false));
    await tester.pumpWidget(h.wrap(const RetentionCard()));
    await tester.pumpAndSettle();
    expect(find.text(RetentionCard.nothingFolded), findsOneWidget);
  });

  testWidgets('picking a window saves it', (tester) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.on('GET', '/retention', (_) => retention());
    h.api.on('PUT', '/retention', (data) {
      final months = (data as Map)['rawMonths'] as int;
      return retention(months: months, source: 'app');
    });
    await tester.pumpWidget(h.wrap(const RetentionCard()));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('retention-months')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('2 years').last);
    await tester.pumpAndSettle();

    expect(h.api.calls, contains('PUT /retention'));
    expect(h.api.bodies[h.api.calls.indexOf('PUT /retention')], {
      'rawMonths': 24,
    });
    expect(find.text('2 years'), findsOneWidget);
  });

  testWidgets('keeping raw for ever hides the run button', (tester) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.on('GET', '/retention', (_) => retention(months: 0, folded: false));
    await tester.pumpWidget(h.wrap(const RetentionCard()));
    await tester.pumpAndSettle();
    expect(find.text('For ever'), findsOneWidget);
    expect(find.text(RetentionCard.runLabel), findsNothing);
  });

  testWidgets('running now says how many months were folded', (tester) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.on('GET', '/retention', (_) => retention());
    h.api.on(
      'POST',
      '/retention/run',
      (_) => {
        'rawSince': '2025-11-01',
        'folded': [
          {
            'month': '2025-08-01',
            'rawRows': 5,
            'minuteRows': 3,
            'compacted': true,
          },
        ],
        'compacted': ['2025-08'],
      },
    );
    await tester.pumpWidget(
      h.wrap(
        const Scaffold(body: SingleChildScrollView(child: RetentionCard())),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text(RetentionCard.runLabel));
    await tester.pumpAndSettle();
    expect(find.text('Folded 1 month.'), findsOneWidget);
  });

  testWidgets('a window set in the environment cannot be changed here', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.on('GET', '/retention', (_) => retention(source: 'environment'));
    await tester.pumpWidget(h.wrap(const RetentionCard()));
    await tester.pumpAndSettle();
    expect(find.text(RetentionCard.fromEnvironment), findsOneWidget);
    final dropdown = tester.widget<DropdownButton<int>>(
      find.byKey(const ValueKey('retention-months')),
    );
    expect(dropdown.onChanged, isNull);
  });

  testWidgets('a failed load says why', (tester) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.fail(
      'GET',
      '/retention',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(h.wrap(const RetentionCard()));
    await tester.pumpAndSettle();
    expect(find.text('The server had a problem.'), findsOneWidget);
  });

  testWidgets('the admin sees it only on a server that has retention', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.on('GET', '/retention', (_) => retention());
    h.api.on('GET', '/diagnostics/problems', (_) => {'last24Hours': 0});
    h.api.on('GET', '/system/setup-status', (_) => Fixtures.setupStatus());
    h.api.on(
      'GET',
      '/system/version',
      (_) => {
        ...Fixtures.version(),
        'features': ['data.daily'],
      },
    );
    await h.startup.run();
    await tester.pumpWidget(
      h.wrap(
        const Scaffold(body: SingleChildScrollView(child: ServerSection())),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(RetentionCard), findsNothing);
    expect(h.api.calls, isNot(contains('GET /retention')));

    h.api.on(
      'GET',
      '/system/version',
      (_) => {
        ...Fixtures.version(),
        'features': ['data.daily', RetentionCard.feature],
      },
    );
    await h.startup.run();
    await tester.pumpAndSettle();
    expect(find.byType(RetentionCard), findsOneWidget);
    expect(h.api.calls, contains('GET /retention'));
  });

  testWidgets('members never see the card or ask for the setting', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await signIn(role: 'member');
    await tester.pumpWidget(h.wrap(const ServerSection()));
    await tester.pumpAndSettle();
    expect(find.byType(RetentionCard), findsNothing);
    expect(h.api.calls, isNot(contains('GET /retention')));
  });
}
