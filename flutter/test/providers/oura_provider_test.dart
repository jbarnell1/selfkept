import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;
  late List<Uri> opened;
  var openResult = true;

  setUp(() async {
    opened = [];
    openResult = true;
    h = TestHarness(
      openUrl: (uri) async {
        opened.add(uri);
        return openResult;
      },
    );
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus());
  });
  tearDown(() => h.dispose());

  test('load reads the status', () async {
    expect(h.oura.status, isNull);
    await h.oura.load();
    expect(h.oura.status!.configured, isTrue);
    expect(h.oura.status!.connected, isFalse);
    expect(h.oura.loading, isFalse);
    expect(h.oura.error, isNull);
  });

  test('load records the error and keeps no status', () async {
    h.api.fail(
      'GET',
      '/oura',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await h.oura.load();
    expect(h.oura.status, isNull);
    expect(h.oura.error, 'The server had a problem.');
  });

  test('connect asks for the address and hands it to the launcher', () async {
    h.api.on(
      'POST',
      '/oura/connect',
      (_) => {
        'authorizeUrl': 'https://cloud.ouraring.com/oauth/authorize?state=x',
      },
    );
    await h.oura.connect();
    expect(opened, [
      Uri.parse('https://cloud.ouraring.com/oauth/authorize?state=x'),
    ]);
    expect(h.oura.error, isNull);
  });

  test('connect says so when the launcher cannot open the address', () async {
    h.api.on(
      'POST',
      '/oura/connect',
      (_) => {'authorizeUrl': 'https://cloud.ouraring.com/oauth/authorize'},
    );
    openResult = false;
    await h.oura.connect();
    expect(h.oura.error, isNotNull);
  });

  test('connect failure sets the error and opens nothing', () async {
    h.api.fail(
      'POST',
      '/oura/connect',
      const ApiException('Oura is not set up.', statusCode: 400),
    );
    await h.oura.connect();
    expect(h.oura.error, 'Oura is not set up.');
    expect(opened, isEmpty);
  });

  test('sync posts, waits, then reloads the status', () async {
    h.api.on('POST', '/oura/sync', (_) => null);
    await h.oura.sync(reloadAfter: Duration.zero);
    expect(h.api.calls, containsAllInOrder(['POST /oura/sync', 'GET /oura']));
    expect(h.oura.syncing, isFalse);
    expect(h.oura.status, isNotNull);
  });

  test('sync failure sets the error and does not reload', () async {
    h.api.fail(
      'POST',
      '/oura/sync',
      const ApiException('Oura is not connected.', statusCode: 409),
    );
    await h.oura.sync(reloadAfter: Duration.zero);
    expect(h.oura.error, 'Oura is not connected.');
    expect(h.oura.syncing, isFalse);
    expect(h.api.calls, isNot(contains('GET /oura')));
  });

  test('disconnect deletes, then reloads', () async {
    h.api.on('DELETE', '/oura', (_) => null);
    expect(await h.oura.disconnect(), isTrue);
    expect(h.api.calls, containsAllInOrder(['DELETE /oura', 'GET /oura']));
  });

  test('a failed disconnect sets the error', () async {
    h.api.fail(
      'DELETE',
      '/oura',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    expect(await h.oura.disconnect(), isFalse);
    expect(h.oura.error, 'The server had a problem.');
  });
}
