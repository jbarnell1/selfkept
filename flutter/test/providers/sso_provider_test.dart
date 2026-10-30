import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/sso.dart';
import 'package:selfkept/services/sso_service.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;
  final opened = <Uri>[];
  Uri? answer;

  Map<String, dynamic> info({bool enabled = true}) => {
    'enabled': enabled,
    'name': enabled ? 'Pocket ID' : null,
  };

  Map<String, dynamic> link({bool linked = false}) => {
    ...info(),
    'linked': linked,
    'email': linked ? 'ada@id.example.test' : null,
    'linkedAt': linked ? '2026-10-30T18:00:00Z' : null,
  };

  setUp(() {
    opened.clear();
    answer = null;
    h = TestHarness(
      ssoBrowser: (url) async {
        opened.add(url);
        return answer;
      },
    );
    h.api.serverUrl = 'https://box.example.test';
    h.api.on('GET', '/auth/sso', (_) => info());
  });
  tearDown(() => h.dispose());

  test('sign-in opens the server start address for a phone', () async {
    await h.sso.loadInfo();
    await h.sso.signIn();
    expect(
      opened.single.toString(),
      'https://box.example.test/api/auth/sso/start?client=mobile',
    );
  });

  test('a code on the app link is traded for a session', () async {
    h.api.on('POST', '/auth/sso/exchange', (_) => Fixtures.authResponse());
    answer = Uri.parse('selfkept://sso?code=handoff-1');

    final signedIn = await h.sso.signIn();

    expect(signedIn, isTrue);
    expect(h.auth.isAuthenticated, isTrue);
    final body = h.api.bodies[h.api.calls.indexOf('POST /auth/sso/exchange')];
    expect((body as Map)['code'], 'handoff-1');
    expect(h.sso.error, isNull);
  });

  test('an error on the app link becomes a sentence, not a session', () async {
    await h.sso.loadInfo();
    answer = Uri.parse('selfkept://sso?error=not_linked');

    expect(await h.sso.signIn(), isFalse);

    expect(h.auth.isAuthenticated, isFalse);
    expect(h.sso.error, contains('No account here is connected'));
    expect(h.sso.error, contains('Pocket ID'));
    expect(h.api.calls, isNot(contains('POST /auth/sso/exchange')));
  });

  test('closing the sheet is quiet', () async {
    answer = null;
    expect(await h.sso.signIn(), isFalse);
    expect(h.sso.error, isNull);
    expect(h.sso.isBusy, isFalse);
  });

  test('a server without provider sign-in shows no button', () async {
    h.api.on('GET', '/auth/sso', (_) => info(enabled: false));
    await h.sso.loadInfo();
    expect(h.sso.info.enabled, isFalse);

    h.api.fail(
      'GET',
      '/auth/sso',
      const ApiException('Not found.', statusCode: 404),
    );
    await h.sso.loadInfo();
    expect(h.sso.info.enabled, isFalse, reason: 'an older server has no route');
  });

  test('connect follows the link address and reloads the status', () async {
    var linked = false;
    h.api.on('GET', '/auth/sso/link', (_) => link(linked: linked));
    h.api.on(
      'POST',
      '/auth/sso/link?client=mobile',
      (_) => {'url': 'https://id.example.test/authorize?state=s'},
    );
    await h.sso.loadLink();
    expect(h.sso.link!.linked, isFalse);

    answer = Uri.parse('selfkept://sso?linked=1');
    linked = true;
    await h.sso.connect();

    expect(
      opened.single.toString(),
      'https://id.example.test/authorize?state=s',
    );
    expect(h.sso.link!.linked, isTrue);
    expect(h.sso.notice, SsoMessages.linked);
  });

  test('a web trip back under You says how it went', () async {
    h.api.on('GET', '/auth/sso/link', (_) => link(linked: true));
    await h.sso.loadLink();

    h.sso.showReturn('linked_elsewhere');
    expect(h.sso.error, contains('already connected to another account'));

    h.sso.showReturn('linked');
    expect(h.sso.notice, SsoMessages.linked);
    expect(h.sso.error, isNull);
  });

  test('the start address follows the server address', () {
    final service = SsoService(h.api);
    h.api.serverUrl = 'https://other.example.test/';
    expect(
      service.startUrl('web').toString(),
      'https://other.example.test/api/auth/sso/start?client=web',
    );
  });
}
