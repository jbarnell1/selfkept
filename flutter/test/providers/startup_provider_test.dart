import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/providers/startup_provider.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() => h = TestHarness());
  tearDown(() => h.dispose());

  test(
    'unreachable server stops at unreachable and never asks setup status',
    () async {
      h.api.fail('GET', '/system/version', const ApiException.network());
      await h.startup.run();
      expect(h.startup.phase, StartupPhase.unreachable);
      expect(h.api.calls, ['GET /system/version']);
      expect(h.startup.isReady, isFalse);
    },
  );

  test('with no server address it asks for one and calls nothing', () async {
    h.api.serverUrl = '';
    await h.startup.run();
    expect(h.startup.phase, StartupPhase.needsAddress);
    expect(h.api.calls, isEmpty);
    expect(h.startup.isRunning, isFalse);
  });

  test('a compatible server reads setup status and bootstraps auth', () async {
    h.api.on('GET', '/system/version', (_) => Fixtures.version());
    h.api.on(
      'GET',
      '/system/setup-status',
      (_) => Fixtures.setupStatus(needsFirstUser: true),
    );
    await h.startup.run();
    expect(h.startup.phase, StartupPhase.ready);
    expect(h.startup.needsSetup, isTrue);
    expect(h.auth.isBootstrapped, isTrue);
    expect(h.startup.versionInfo?.version, '0.1.0');
  });

  test('an old server stops at serverNeedsUpdate', () async {
    h.api.on('GET', '/system/version', (_) => Fixtures.version(v: '0.0.1'));
    await h.startup.run();
    expect(h.startup.phase, StartupPhase.serverNeedsUpdate);
    expect(h.startup.versionResult?.requiredServerVersion, '0.1.0');
  });

  test('a server that wants a newer app stops at appNeedsUpdate', () async {
    h.api.on(
      'GET',
      '/system/version',
      (_) => {...Fixtures.version(), 'minClientVersion': '9.0.0'},
    );
    await h.startup.run();
    expect(h.startup.phase, StartupPhase.appNeedsUpdate);
  });

  test('a missing setup-status endpoint counts as already set up', () async {
    h.api.on('GET', '/system/version', (_) => Fixtures.version());
    await h.startup.run();
    expect(h.startup.phase, StartupPhase.ready);
    expect(h.startup.needsSetup, isFalse);
  });

  test('signing in clears needsSetup', () async {
    h.api.on('GET', '/system/version', (_) => Fixtures.version());
    h.api.on(
      'GET',
      '/system/setup-status',
      (_) => Fixtures.setupStatus(needsFirstUser: true),
    );
    h.api.on('POST', '/auth/register', (_) => Fixtures.authResponse());
    await h.startup.run();
    expect(h.startup.needsSetup, isTrue);
    await h.auth.register(
      email: 'a@b',
      password: 'longenough1',
      displayName: 'Ada',
    );
    expect(h.startup.needsSetup, isFalse);
  });

  test('run can be repeated after a failure', () async {
    h.api.fail('GET', '/system/version', const ApiException.network());
    await h.startup.run();
    expect(h.startup.phase, StartupPhase.unreachable);
    h.api.on('GET', '/system/version', (_) => Fixtures.version());
    await h.startup.run();
    expect(h.startup.phase, StartupPhase.ready);
  });
}
