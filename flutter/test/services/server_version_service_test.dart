import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/system.dart';
import 'package:selfkept/services/server_version_service.dart';
import 'package:selfkept/services/system_service.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';

void main() {
  group('parseVersion', () {
    test('accepts X.Y.Z with optional v prefix and build suffix', () {
      expect(ServerVersionService.parseVersion('1.5.29'), [1, 5, 29]);
      expect(ServerVersionService.parseVersion('v1.5.29'), [1, 5, 29]);
      expect(ServerVersionService.parseVersion('1.5.29+3'), [1, 5, 29]);
      expect(ServerVersionService.parseVersion('0.1.0-rc.1'), [0, 1, 0]);
    });

    test('rejects dev, empty and null', () {
      expect(ServerVersionService.parseVersion('dev'), isNull);
      expect(ServerVersionService.parseVersion(''), isNull);
      expect(ServerVersionService.parseVersion(null), isNull);
    });
  });

  group('compareVersions', () {
    test('orders numerically', () {
      expect(
        ServerVersionService.compareVersions([1, 2, 3], [1, 2, 4]),
        lessThan(0),
      );
      expect(
        ServerVersionService.compareVersions([1, 10, 0], [1, 9, 9]),
        greaterThan(0),
      );
      expect(ServerVersionService.compareVersions([1, 2, 3], [1, 2, 3]), 0);
      expect(
        ServerVersionService.compareVersions([1, 2, 3], null),
        greaterThan(0),
      );
    });
  });

  group('evaluate', () {
    final service = ServerVersionService(
      SystemService(FakeApiClient()),
      appVersion: '1.2.0',
      minServerVersion: '1.0.0',
    );

    test('ok when both sides are satisfied', () {
      final r = service.evaluate(
        const VersionInfo(version: '1.1.0', minClientVersion: '1.0.0'),
      );
      expect(r.compatibility, ServerCompatibility.ok);
      expect(r.isOk, isTrue);
    });

    test('serverNeedsUpdate when the server is older than the minimum', () {
      final r = service.evaluate(
        const VersionInfo(version: '0.9.0', minClientVersion: '0.1.0'),
      );
      expect(r.compatibility, ServerCompatibility.serverNeedsUpdate);
      expect(r.requiredServerVersion, '1.0.0');
      expect(r.serverVersion, '0.9.0');
    });

    test('appNeedsUpdate when the server wants a newer client', () {
      final r = service.evaluate(
        const VersionInfo(version: '2.0.0', minClientVersion: '1.3.0'),
      );
      expect(r.compatibility, ServerCompatibility.appNeedsUpdate);
      expect(r.requiredAppVersion, '1.3.0');
      expect(r.appVersion, '1.2.0');
    });

    test('a dev app build never blocks', () {
      final dev = ServerVersionService(
        SystemService(FakeApiClient()),
        appVersion: 'dev',
        minServerVersion: '1.0.0',
      );
      final r = dev.evaluate(
        const VersionInfo(version: '2.0.0', minClientVersion: '9.9.9'),
      );
      expect(r.compatibility, ServerCompatibility.ok);
    });

    test('an unparseable server version never blocks', () {
      final r = service.evaluate(
        const VersionInfo(version: 'local', minClientVersion: ''),
      );
      expect(r.compatibility, ServerCompatibility.ok);
    });
  });

  group('check', () {
    test('network failure is unreachable with the failure message', () async {
      final api = FakeApiClient();
      api.fail('GET', '/system/version', const ApiException.network());
      final r = await ServerVersionService(SystemService(api)).check();
      expect(r.compatibility, ServerCompatibility.unreachable);
      expect(r.failure, isNotEmpty);
    });

    test('an HTTP error still counts as reachable', () async {
      final api = FakeApiClient();
      api.fail(
        'GET',
        '/system/version',
        const ApiException('boom', statusCode: 500),
      );
      final r = await ServerVersionService(SystemService(api)).check();
      expect(r.compatibility, ServerCompatibility.ok);
      expect(r.failure, 'boom');
    });

    test('a good answer is evaluated', () async {
      final api = FakeApiClient();
      api.on('GET', '/system/version', (_) => Fixtures.version(pascal: true));
      final r = await ServerVersionService(
        SystemService(api),
        appVersion: '0.1.0',
        minServerVersion: '0.1.0',
      ).check();
      expect(r.compatibility, ServerCompatibility.ok);
      expect(r.info?.brand.productName, 'Anything');
    });
  });
}
