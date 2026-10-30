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
    h.api.on('GET', '/connect', (_) => Fixtures.connections());
    h.api.on('GET', '/connect/memory', (_) => [Fixtures.memoryItem()]);
  });
  tearDown(() => h.dispose());

  test('load fetches connections and memory together', () async {
    expect(h.connect.hasLoaded, isFalse);
    await h.connect.load();
    expect(h.connect.hasLoaded, isTrue);
    expect(h.connect.loading, isFalse);
    expect(h.connect.error, isNull);
    expect(h.connect.connections!.assistants, hasLength(1));
    expect(h.connect.connections!.tokens, hasLength(1));
    expect(h.connect.memory, hasLength(1));
    expect(h.api.calls, containsAll(['GET /connect', 'GET /connect/memory']));
  });

  test('load records the error and a later load clears it', () async {
    h.api.fail(
      'GET',
      '/connect',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await h.connect.load();
    expect(h.connect.hasLoaded, isFalse);
    expect(h.connect.error, 'The server had a problem.');

    h.api.on('GET', '/connect', (_) => Fixtures.connections());
    await h.connect.load();
    expect(h.connect.error, isNull);
    expect(h.connect.hasLoaded, isTrue);
  });

  test(
    'createToken posts the name and the memory choice, then reloads',
    () async {
      h.api.on('POST', '/connect/tokens', (_) => Fixtures.createdToken());
      final created = await h.connect.createToken(
        'Kitchen tablet',
        allowMemoryWrite: false,
      );
      expect(created, isNotNull);
      expect(created!.secret, startsWith('pat_'));
      final i = h.api.calls.indexOf('POST /connect/tokens');
      expect(h.api.bodies[i], {
        'name': 'Kitchen tablet',
        'allowMemoryWrite': false,
      });
      expect(h.api.calls.last, anyOf('GET /connect', 'GET /connect/memory'));
      expect(h.connect.hasLoaded, isTrue);
    },
  );

  test('createToken defaults to letting the token remember', () async {
    h.api.on('POST', '/connect/tokens', (_) => Fixtures.createdToken());
    await h.connect.createToken('Tablet');
    final i = h.api.calls.indexOf('POST /connect/tokens');
    expect((h.api.bodies[i] as Map)['allowMemoryWrite'], isTrue);
  });

  test('createToken failure returns null and sets the error', () async {
    h.api.fail(
      'POST',
      '/connect/tokens',
      const ApiException('That name is taken.', statusCode: 409),
    );
    final created = await h.connect.createToken('Tablet');
    expect(created, isNull);
    expect(h.connect.error, 'That name is taken.');
  });

  test('revokeToken deletes, then reloads', () async {
    h.api.on('DELETE', '/connect/tokens/t1', (_) => null);
    expect(await h.connect.revokeToken('t1'), isTrue);
    expect(h.api.calls, contains('DELETE /connect/tokens/t1'));
    expect(h.api.calls.where((c) => c == 'GET /connect'), hasLength(1));
  });

  test('revokeAssistant encodes the client id as one path segment', () async {
    const clientId = 'https://webui.example.test/client.json';
    final path = '/connect/assistants/${Uri.encodeComponent(clientId)}';
    h.api.on('DELETE', path, (_) => null);
    expect(await h.connect.revokeAssistant(clientId), isTrue);
    expect(h.api.calls, contains('DELETE $path'));
    expect(path, isNot(contains('://')));
  });

  test('deleteMemory deletes, then reloads', () async {
    h.api.on('DELETE', '/connect/memory/m1', (_) => null);
    expect(await h.connect.deleteMemory('m1'), isTrue);
    expect(h.api.calls, contains('DELETE /connect/memory/m1'));
    expect(h.api.calls.where((c) => c == 'GET /connect/memory'), hasLength(1));
  });

  test('a failed revoke sets the error and does not reload', () async {
    h.api.fail(
      'DELETE',
      '/connect/tokens/t1',
      const ApiException('Not found.', statusCode: 404),
    );
    expect(await h.connect.revokeToken('t1'), isFalse);
    expect(h.connect.error, 'Not found.');
    expect(h.api.calls.where((c) => c == 'GET /connect'), isEmpty);
  });

  test('signing out clears what was loaded', () async {
    await h.connect.load();
    h.api.on('POST', '/auth/logout', (_) => null);
    await h.auth.logout();
    expect(h.connect.hasLoaded, isFalse);
    expect(h.connect.memory, isEmpty);
  });
}
