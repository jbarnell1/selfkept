import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
    h.api.on('GET', '/labs/reports', (_) => [Fixtures.labReport()]);
  });
  tearDown(() => h.dispose());

  test('load fetches markers and reports together', () async {
    expect(h.labs.hasLoaded, isFalse);
    await h.labs.load();
    expect(h.labs.hasLoaded, isTrue);
    expect(h.labs.loading, isFalse);
    expect(h.labs.error, isNull);
    expect(h.labs.markers, hasLength(7));
    expect(h.labs.reports, hasLength(1));
    expect(h.labs.hasAnyResults, isTrue);
    expect(
      h.api.calls,
      containsAll(['GET /labs/markers', 'GET /labs/reports']),
    );
  });

  test('hasAnyResults is false when no marker has a value', () async {
    h.api.on(
      'GET',
      '/labs/markers',
      (_) => [Fixtures.labMarker(latestValue: null, severity: null)],
    );
    h.api.on('GET', '/labs/reports', (_) => <Map<String, dynamic>>[]);
    await h.labs.load();
    expect(h.labs.hasLoaded, isTrue);
    expect(h.labs.hasAnyResults, isFalse);
    expect(h.labs.reports, isEmpty);
  });

  test('load records the error and stays unloaded', () async {
    h.api.fail(
      'GET',
      '/labs/markers',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await h.labs.load();
    expect(h.labs.hasLoaded, isFalse);
    expect(h.labs.loading, isFalse);
    expect(h.labs.error, 'The server had a problem.');
    expect(h.labs.markers, isEmpty);

    h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
    await h.labs.load();
    expect(h.labs.error, isNull);
    expect(h.labs.hasLoaded, isTrue);
  });

  test('refresh loads again', () async {
    await h.labs.load();
    await h.labs.refresh();
    expect(h.api.calls.where((c) => c == 'GET /labs/markers'), hasLength(2));
  });

  test('deleteReport deletes, then reloads', () async {
    await h.labs.load();
    h.api.on('DELETE', '/labs/reports/r1', (_) => null);
    h.api.on('GET', '/labs/reports', (_) => <Map<String, dynamic>>[]);
    final ok = await h.labs.deleteReport('r1');
    expect(ok, isTrue);
    expect(h.api.calls, contains('DELETE /labs/reports/r1'));
    expect(h.labs.reports, isEmpty);
    expect(h.api.calls.where((c) => c == 'GET /labs/markers'), hasLength(2));
  });

  test('deleteReport failure keeps the data and reports the error', () async {
    await h.labs.load();
    h.api.fail(
      'DELETE',
      '/labs/reports/r1',
      const ApiException('Not found.', statusCode: 404),
    );
    final ok = await h.labs.deleteReport('r1');
    expect(ok, isFalse);
    expect(h.labs.error, 'Not found.');
    expect(h.labs.reports, hasLength(1));
  });

  test('signing out clears what was loaded', () async {
    await h.labs.load();
    await h.auth.logout();
    expect(h.labs.markers, isEmpty);
    expect(h.labs.hasLoaded, isFalse);
  });
}
