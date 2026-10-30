import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/you/server_section.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/recent_problems_card.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  Future<void> signIn({String role = 'admin'}) async {
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse(role: role));
    await h.auth.login(email: 'a@b', password: 'x');
  }

  Map<String, dynamic> problems({int last24 = 2}) => {
    'last24Hours': last24,
    'items': [
      if (last24 > 0) ...[
        {
          'at': DateTime.now().toUtc().toIso8601String(),
          'level': 'warning',
          'source': 'DiagnosticsController',
          'message': 'App error (sync) on android 0.1.1 at /: Server error.',
          'detail':
              '#0 SyncEngine.flush (package:selfkept/offline/sync_engine.dart:120)',
        },
        {
          'at': DateTime.now().toUtc().toIso8601String(),
          'level': 'error',
          'source': 'ErrorHandlingMiddleware',
          'message': 'Unhandled exception on GET /api/today.',
        },
      ],
    ],
  };

  setUp(() => h = TestHarness());
  tearDown(() => h.dispose());

  testBothThemes('the admin sees the count and each problem', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.on('GET', '/diagnostics/problems', (_) => problems());
    await tester.pumpWidget(h.wrap(const RecentProblemsCard(), theme: theme));
    await tester.pumpAndSettle();
    expect(find.text('2 in the last 24 hours.'), findsOneWidget);
    expect(find.textContaining('App error (sync)'), findsOneWidget);
    expect(
      find.textContaining('Unhandled exception on GET /api/today'),
      findsOneWidget,
    );

    await tester.tap(find.textContaining('App error (sync)'));
    await tester.pumpAndSettle();
    expect(find.textContaining('sync_engine.dart:120'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a quiet day says so', (tester) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.on('GET', '/diagnostics/problems', (_) => problems(last24: 0));
    await tester.pumpWidget(h.wrap(const RecentProblemsCard()));
    await tester.pumpAndSettle();
    expect(find.text(RecentProblemsCard.allClear), findsOneWidget);
  });

  testWidgets('a failed load says why', (tester) async {
    await usePhoneSize(tester);
    await signIn();
    h.api.fail(
      'GET',
      '/diagnostics/problems',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(h.wrap(const RecentProblemsCard()));
    await tester.pumpAndSettle();
    expect(find.text('The server had a problem.'), findsOneWidget);
  });

  testWidgets('members never see the card or ask for the list', (tester) async {
    await usePhoneSize(tester);
    await signIn(role: 'member');
    await tester.pumpWidget(h.wrap(const ServerSection()));
    await tester.pumpAndSettle();
    expect(find.byType(RecentProblemsCard), findsNothing);
    expect(h.api.calls, isNot(contains('GET /diagnostics/problems')));
  });
}
