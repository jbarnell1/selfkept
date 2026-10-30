import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/config/app_config.dart';

void main() {
  tearDown(AppConfig.resetOverrides);

  group('normalizeServerUrl', () {
    test('adds a scheme, trims, and strips trailing slash and /api', () {
      expect(
        AppConfig.normalizeServerUrl(' box.tailnet.ts.net '),
        'http://box.tailnet.ts.net',
      );
      expect(
        AppConfig.normalizeServerUrl('https://box.tailnet.ts.net/'),
        'https://box.tailnet.ts.net',
      );
      expect(
        AppConfig.normalizeServerUrl('http://localhost:5000/api/'),
        'http://localhost:5000',
      );
      expect(AppConfig.normalizeServerUrl(''), '');
    });

    test('apiBaseFor appends the prefix once', () {
      expect(
        AppConfig.apiBaseFor('http://localhost:5000/api'),
        'http://localhost:5000/api',
      );
      expect(
        AppConfig.apiBaseFor('http://localhost:5000'),
        'http://localhost:5000/api',
      );
    });
  });

  group('defaultServerUrl on mobile', () {
    setUp(() => AppConfig.isWebOverride = false);

    test('Android emulator gets the loopback alias', () {
      AppConfig.platformOverride = TargetPlatform.android;
      expect(AppConfig.defaultServerUrl, 'http://10.0.2.2:5000');
    });

    test('iOS gets localhost', () {
      AppConfig.platformOverride = TargetPlatform.iOS;
      expect(AppConfig.defaultServerUrl, 'http://localhost:5000');
    });

    test('an explicit define wins', () {
      AppConfig.platformOverride = TargetPlatform.android;
      AppConfig.definedServerUrlOverride = 'https://box.tailnet.ts.net/';
      expect(AppConfig.defaultServerUrl, 'https://box.tailnet.ts.net');
    });

    test('same-origin counts as unset', () {
      AppConfig.platformOverride = TargetPlatform.android;
      AppConfig.definedServerUrlOverride = AppConfig.sameOrigin;
      expect(AppConfig.isSameOriginBuild, isTrue);
      expect(AppConfig.defaultServerUrl, 'http://10.0.2.2:5000');
      expect(AppConfig.clientKind, 'mobile');
    });
  });

  group('defaultServerUrl on web', () {
    setUp(() => AppConfig.isWebOverride = true);

    test('an explicit URL define wins', () {
      AppConfig.definedServerUrlOverride = 'http://localhost:5000';
      expect(AppConfig.defaultServerUrl, 'http://localhost:5000');
      expect(AppConfig.clientKind, 'web');
    });

    test('same-origin is recognised (the origin itself needs a browser)', () {
      AppConfig.definedServerUrlOverride = AppConfig.sameOrigin;
      expect(AppConfig.isSameOriginBuild, isTrue);
      expect(AppConfig.hasDefinedServerUrl, isTrue);
    });
  });
}
