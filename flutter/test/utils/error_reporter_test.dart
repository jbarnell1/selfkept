import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/services/diagnostics_service.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/utils/error_reporter.dart';

import '../helpers/fake_api_client.dart';

void main() {
  late FakeApiClient api;
  late DateTime now;
  late ErrorReporter reporter;

  List<Map<String, dynamic>> sent() => [
    for (var i = 0; i < api.calls.length; i++)
      if (api.calls[i] == 'POST /diagnostics/client-errors')
        api.bodies[i]! as Map<String, dynamic>,
  ];

  setUp(() {
    api = FakeApiClient();
    api.on('POST', '/diagnostics/client-errors', (_) => null);
    now = DateTime(2026, 10, 30, 12);
    reporter = ErrorReporter(DiagnosticsService(api), clock: () => now)
      ..where = () => '/labs';
  });

  test('a bug is sent with where it happened and what it was', () async {
    reporter.report(
      StateError('Bad state: no element'),
      StackTrace.fromString('#0 main'),
      kind: 'flutter',
    );
    await pumpEventQueue();
    final body = sent().single;
    expect(body['kind'], 'flutter');
    expect(body['message'], contains('no element'));
    expect(body['stack'], '#0 main');
    expect(body['where'], '/labs');
    expect(body['appVersion'], isNotEmpty);
    expect(body['platform'], isNotEmpty);
  });

  test('being offline, signed out or refused is not a bug', () async {
    reporter.report(const ApiException.network(), null, kind: 'sync');
    reporter.report(
      const ApiException('Sign in again.', statusCode: 401),
      null,
      kind: 'sync',
    );
    reporter.report(
      const ApiException('No.', statusCode: 403),
      null,
      kind: 'sync',
    );
    await pumpEventQueue();
    expect(sent(), isEmpty);
    reporter.report(
      const ApiException('Server error.', statusCode: 500),
      null,
      kind: 'sync',
    );
    await pumpEventQueue();
    expect(
      sent(),
      hasLength(1),
      reason: 'a server error is worth knowing about',
    );
  });

  test('the same error is sent once a minute at most', () async {
    for (var i = 0; i < 5; i++) {
      reporter.report(StateError('loop'), null, kind: 'flutter');
    }
    now = now.add(const Duration(seconds: 61));
    reporter.report(StateError('loop'), null, kind: 'flutter');
    await pumpEventQueue();
    expect(sent(), hasLength(2));
  });

  test('a run of the app sends at most its cap', () async {
    for (var i = 0; i < ErrorReporter.maxPerSession + 10; i++) {
      reporter.report(StateError('error $i'), null, kind: 'flutter');
    }
    await pumpEventQueue();
    expect(sent(), hasLength(ErrorReporter.maxPerSession));
  });

  test('long text is cut and a failed send is swallowed', () async {
    api.fail(
      'POST',
      '/diagnostics/client-errors',
      const ApiException.network(),
    );
    reporter.report(
      StateError('x' * 2000),
      StackTrace.fromString('y' * 9000),
      kind: 'flutter',
    );
    await pumpEventQueue();
    final body = api.bodies.last! as Map<String, dynamic>;
    expect((body['message'] as String).length, 500);
    expect((body['stack'] as String).length, 4000);
  });
}
