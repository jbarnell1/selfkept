import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/setup.dart';
import 'package:selfkept/models/user.dart';

import '../helpers/fake_api_client.dart';

/// Built from its code point so this file has none itself.
final emDash = String.fromCharCode(0x2014);

void main() {
  group('SetupItem', () {
    test('reads the wire values in any case and skips unknown ones', () {
      expect(SetupItem.fromString('appleHealth'), SetupItem.appleHealth);
      expect(SetupItem.fromString('HealthConnect'), SetupItem.healthConnect);
      expect(SetupItem.fromString(' oura '), SetupItem.oura);
      expect(SetupItem.fromString('fitbitCloud'), isNull);
      expect(SetupItem.fromString(null), isNull);
      expect(SetupItem.assistant.toJson(), 'assistant');
    });

    test('every item has words for the questions', () {
      for (final item in SetupItem.values) {
        expect(item.displayName, isNotEmpty);
        expect(item.description, isNotEmpty);
        expect(item.description, isNot(contains(emDash)));
      }
    });
  });

  group('OuraApp.fromJson', () {
    for (final pascal in [false, true]) {
      test(pascal ? 'PascalCase' : 'camelCase', () {
        final app = OuraApp.fromJson(
          Fixtures.ouraApp(
            pascal: pascal,
            configured: true,
            source: 'environment',
            clientId: 'app-1',
          ),
        );
        expect(app.configured, isTrue);
        expect(app.source, CredentialSource.environment);
        expect(app.editable, isFalse);
        expect(app.clientId, 'app-1');
        expect(app.redirectUri, 'https://box.tailnet.ts.net/api/oura/callback');
        expect(app.developerUrl, startsWith('https://cloud.ouraring.com/'));
      });
    }

    test('an unknown source reads as none and stays editable', () {
      final app = OuraApp.fromJson({
        'source': 'somewhere',
        'configured': false,
      });
      expect(app.source, CredentialSource.none);
      expect(app.editable, isTrue);
      expect(app.redirectUri, isNull);
    });
  });

  group('User setup fields', () {
    for (final pascal in [false, true]) {
      test(pascal ? 'PascalCase' : 'camelCase', () {
        final user = User.fromJson(
          Fixtures.user(
            pascal: pascal,
            setupPlan: ['oura', 'labs', 'somethingNew'],
            setupDone: false,
          ),
        );
        expect(user.setupPlan, [SetupItem.oura, SetupItem.labs]);
        expect(user.setupPending, isTrue);
      });
    }

    test('a finished setup is not pending and copyWith keeps it', () {
      final user = User.fromJson(Fixtures.user(setupPlan: ['assistant']));
      expect(user.setupPending, isFalse);
      final renamed = user.copyWith(displayName: 'Ada L.');
      expect(renamed.setupPlan, [SetupItem.assistant]);
      expect(renamed.setupCompletedAt, user.setupCompletedAt);
    });

    test('a server without the fields reads as finished with no answers', () {
      final json = Fixtures.user()
        ..remove('setupPlan')
        ..['setupCompletedAt'] = '2026-01-01T00:00:00Z';
      final user = User.fromJson(json);
      expect(user.setupPlan, isEmpty);
      expect(user.setupPending, isFalse);
    });
  });
}
