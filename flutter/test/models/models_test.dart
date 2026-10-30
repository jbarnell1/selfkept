import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/enums.dart';
import 'package:selfkept/models/household.dart';
import 'package:selfkept/models/person.dart';
import 'package:selfkept/models/share.dart';
import 'package:selfkept/models/system.dart';
import 'package:selfkept/models/user.dart';

import '../helpers/fake_api_client.dart';

void main() {
  for (final pascal in [false, true]) {
    final casing = pascal ? 'PascalCase' : 'camelCase';

    group('Person.fromJson ($casing)', () {
      test('reads every field', () {
        final p = Person.fromJson(Fixtures.person(pascal: pascal));
        expect(p.id, 'p1');
        expect(p.displayName, 'Ada');
        expect(p.sexAtBirth, SexAtBirth.female);
        expect(p.birthDate, DateTime(1990, 5, 4));
        expect(p.units, Units.metric);
        expect(p.timeZone, 'Europe/London');
      });
    });

    group('User.fromJson ($casing)', () {
      test('reads the nested person and role', () {
        final u = User.fromJson(Fixtures.user(pascal: pascal));
        expect(u.id, 'u1');
        expect(u.email, 'ada@example.test');
        expect(u.role, Role.admin);
        expect(u.isAdmin, isTrue);
        expect(u.householdId, 'h1');
        expect(u.person.displayName, 'Ada');
      });
    });

    group('AuthResponse.fromJson ($casing)', () {
      test('reads tokens and user', () {
        final a = AuthResponse.fromJson(Fixtures.authResponse(pascal: pascal));
        expect(a.accessToken, 'access-1');
        expect(a.refreshToken, 'refresh-1');
        expect(a.accessTokenExpiresAt, DateTime.utc(2030));
        expect(a.user.email, 'ada@example.test');
      });
    });

    group('Household.fromJson ($casing)', () {
      test('reads members with roles and dates', () {
        final h = Household.fromJson(Fixtures.household(pascal: pascal));
        expect(h.id, 'h1');
        expect(h.name, 'The Lovelaces');
        expect(h.members, hasLength(2));
        expect(h.members.first.role, Role.admin);
        expect(h.members.last.role, Role.member);
        expect(h.members.first.joinedAt, DateTime.utc(2026, 1, 1, 10));
      });
    });

    group('VersionInfo and SetupStatus ($casing)', () {
      test('VersionInfo reads brand and features', () {
        final v = VersionInfo.fromJson(Fixtures.version(pascal: pascal));
        expect(v.version, '0.1.0');
        expect(v.minClientVersion, '0.1.0');
        expect(v.brand.productName, 'Anything');
        expect(v.features, ['labs', 'mcp']);
        expect(v.hasFeature('mcp'), isTrue);
      });

      test('SetupStatus reads booleans', () {
        final s = SetupStatus.fromJson(
          Fixtures.setupStatus(pascal: pascal, needsFirstUser: true),
        );
        expect(s.needsFirstUser, isTrue);
        expect(s.registrationOpen, isTrue);
      });
    });
  }

  group('Invite', () {
    test('reads the plain code when present and knows when it expired', () {
      final i = Invite.fromJson({
        'Id': 'i1',
        'Code': 'ABCD-1234',
        'ExpiresAt': '2000-01-01T00:00:00Z',
      });
      expect(i.id, 'i1');
      expect(i.code, 'ABCD-1234');
      expect(i.isExpired, isTrue);
    });

    test('tolerates a missing code', () {
      final i = Invite.fromJson({
        'id': 'i2',
        'expiresAt': '2999-01-01T00:00:00Z',
      });
      expect(i.code, isNull);
      expect(i.isExpired, isFalse);
    });
  });

  group('ShareGrant', () {
    test('reads categories in either casing and skips unknown ones', () {
      final g = ShareGrant.fromJson({
        'MemberPersonId': 'p2',
        'DisplayName': 'Charles',
        'Categories': ['sleep', 'LABS', 'nonsense'],
      });
      expect(g.memberPersonId, 'p2');
      expect(g.categories, {ShareCategory.sleep, ShareCategory.labs});
    });

    test('falls back to personId when memberPersonId is absent', () {
      final g = ShareGrant.fromJson({
        'personId': 'p9',
        'categories': <String>[],
      });
      expect(g.memberPersonId, 'p9');
      expect(g.categories, isEmpty);
    });

    test('toPutJson writes wire values in canonical order', () {
      final json = ShareGrant.toPutJson({
        ShareCategory.insights,
        ShareCategory.activity,
      });
      expect(json, {
        'categories': ['activity', 'insights'],
      });
    });
  });

  group('SharedWithMe', () {
    test('reads both casings', () {
      final a = SharedWithMe.fromJson({
        'personId': 'p2',
        'displayName': 'C',
        'categories': ['body'],
      });
      final b = SharedWithMe.fromJson({
        'PersonId': 'p2',
        'DisplayName': 'C',
        'Categories': ['body'],
      });
      expect(a.personId, b.personId);
      expect(a.categories, b.categories);
      expect(a.categories, {ShareCategory.body});
    });
  });

  group('Person.toUpdateJson', () {
    test('writes a date-only birth date and wire enums', () {
      const p = Person(
        id: 'p1',
        displayName: 'Ada',
        sexAtBirth: SexAtBirth.male,
        units: Units.imperial,
        timeZone: 'UTC',
      );
      final json = p.copyWith(birthDate: DateTime(1985, 12, 9)).toUpdateJson();
      expect(json['birthDate'], '1985-12-09');
      expect(json['sexAtBirth'], 'male');
      expect(json['units'], 'imperial');
      expect(json.containsKey('id'), isFalse);
    });

    test('copyWith can clear the birth date', () {
      final p = Person(id: 'p1', displayName: 'Ada', birthDate: DateTime(1990));
      expect(p.copyWith(clearBirthDate: true).birthDate, isNull);
    });
  });

  group('Tolerance', () {
    test('missing fields fall back instead of throwing', () {
      final u = User.fromJson({'id': 'u1'});
      expect(u.role, Role.member);
      expect(u.person.id, '');
      final v = VersionInfo.fromJson({'version': '1.2.3'});
      expect(v.features, isEmpty);
      expect(v.brand.productName, '');
    });
  });
}
