import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/connect.dart';

import '../helpers/fake_api_client.dart';

void main() {
  for (final pascal in [false, true]) {
    final label = pascal ? 'PascalCase' : 'camelCase';

    group('models from $label keys', () {
      test('ConnectedAssistant', () {
        final a = ConnectedAssistant.fromJson(
          Fixtures.assistant(pascal: pascal),
        );
        expect(a.clientId, 'https://webui.example.test/client.json');
        expect(a.clientName, 'Open WebUI');
        expect(a.clientUri, 'https://webui.example.test');
        expect(a.kind, 'cimd');
        expect(a.connectedAt, DateTime.utc(2026, 10, 1, 9));
        expect(a.lastUsedAt, DateTime.utc(2026, 10, 29, 8));
      });

      test('ConnectedAssistant without a last use', () {
        final a = ConnectedAssistant.fromJson(
          Fixtures.assistant(pascal: pascal, lastUsedAt: null),
        );
        expect(a.lastUsedAt, isNull);
      });

      test('PersonalToken', () {
        final t = PersonalToken.fromJson(Fixtures.token(pascal: pascal));
        expect(t.id, 't1');
        expect(t.name, 'Kitchen tablet');
        expect(t.prefix, 'pat_Ab12Cd');
        expect(t.scope, 'health:read memory:write');
        expect(t.createdAt, DateTime.utc(2026, 10, 2, 9));
        expect(t.lastUsedAt, isNull);
        expect(t.canRemember, isTrue);
      });

      test('Connections', () {
        final c = Connections.fromJson(Fixtures.connections(pascal: pascal));
        expect(c.assistants, hasLength(1));
        expect(c.tokens, hasLength(1));
        expect(c.mcpUrl, 'https://box.tailnet.ts.net/mcp');
      });

      test('CreatedToken', () {
        final c = CreatedToken.fromJson(Fixtures.createdToken(pascal: pascal));
        expect(c.secret, startsWith('pat_'));
        expect(c.token.name, 'Kitchen tablet');
      });

      test('AiMemoryItem', () {
        final m = AiMemoryItem.fromJson(Fixtures.memoryItem(pascal: pascal));
        expect(m.id, 'm1');
        expect(m.type, MemoryType.goal);
        expect(m.key, 'goal.sleep');
        expect(m.value, 'seven and a half hours');
        expect(m.displayText, 'Aiming for seven and a half hours of sleep');
        expect(m.text, 'Aiming for seven and a half hours of sleep');
        expect(m.source, 'Open WebUI');
        expect(m.updatedAt, DateTime.utc(2026, 10, 11, 9));
      });

      test('ConsentRequest and ConsentScope', () {
        final r = ConsentRequest.fromJson(
          Fixtures.consentRequest(pascal: pascal),
        );
        expect(r.requestId, 'abc');
        expect(r.clientName, 'Open WebUI');
        expect(r.clientUri, 'https://webui.example.test');
        expect(r.logoUri, 'https://webui.example.test/logo.png');
        expect(r.redirectHost, 'webui.example.test');
        expect(r.scopes.map((s) => s.name), ['health:read', 'memory:write']);
        expect(r.scopes.first.description, 'Read your health data');
        expect(r.expiresAt, DateTime.utc(2026, 10, 30, 12));
      });

      test('OuraStatus', () {
        final s = OuraStatus.fromJson(
          Fixtures.ouraStatus(
            pascal: pascal,
            connected: true,
            lastError: 'Token expired',
            sampleCount: 42,
          ),
        );
        expect(s.configured, isTrue);
        expect(s.connected, isTrue);
        expect(s.connectedAt, DateTime.utc(2026, 10, 20, 9));
        expect(s.lastPullAt, DateTime.utc(2026, 10, 29, 9));
        expect(s.lastError, 'Token expired');
        expect(s.sampleCount, 42);
      });
    });
  }

  test('AiMemoryItem without displayText reads key: value', () {
    final m = AiMemoryItem.fromJson(Fixtures.memoryItem(displayText: null));
    expect(m.displayText, isNull);
    expect(m.text, 'goal.sleep: seven and a half hours');
  });

  test('OuraStatus defaults to not configured', () {
    final s = OuraStatus.fromJson(const {});
    expect(s.configured, isFalse);
    expect(s.connected, isFalse);
    expect(s.sampleCount, 0);
    expect(s.lastError, isNull);
  });

  group('PersonalToken.canRemember', () {
    test('is true when memory:write is in the scope', () {
      expect(
        PersonalToken.fromJson(
          Fixtures.token(scope: 'health:read memory:write'),
        ).canRemember,
        isTrue,
      );
    });

    test('is false for a read only token', () {
      expect(
        PersonalToken.fromJson(
          Fixtures.token(scope: 'health:read'),
        ).canRemember,
        isFalse,
      );
    });
  });

  group('MemoryType', () {
    test('fromString reads wire values, either casing, and falls back', () {
      expect(MemoryType.fromString('goal'), MemoryType.goal);
      expect(MemoryType.fromString('Condition'), MemoryType.condition);
      expect(MemoryType.fromString('medication'), MemoryType.medication);
      expect(MemoryType.fromString('life_event'), MemoryType.lifeEvent);
      expect(MemoryType.fromString('lifeEvent'), MemoryType.lifeEvent);
      expect(MemoryType.fromString('household'), MemoryType.household);
      expect(MemoryType.fromString('nonsense'), MemoryType.preference);
      expect(MemoryType.fromString(null), MemoryType.preference);
    });

    test('fromInt reads by position and falls back', () {
      expect(MemoryType.fromInt(0), MemoryType.goal);
      expect(MemoryType.fromInt(4), MemoryType.lifeEvent);
      expect(MemoryType.fromInt(-1), MemoryType.preference);
      expect(MemoryType.fromInt(99), MemoryType.preference);
    });

    test('toJson round trips through fromString', () {
      for (final t in MemoryType.values) {
        expect(MemoryType.fromString(t.toJson()), t);
      }
      expect(MemoryType.lifeEvent.toJson(), 'life_event');
    });

    test('displayName is plain words', () {
      expect(MemoryType.values.map((t) => t.displayName), [
        'Goal',
        'Condition',
        'Medication',
        'Preference',
        'Life event',
        'Household',
      ]);
    });
  });
}
