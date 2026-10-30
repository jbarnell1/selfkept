import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/enums.dart';

void main() {
  group('ShareCategory', () {
    test('has the eight contract values in order', () {
      expect(ShareCategory.values.map((c) => c.wireValue), [
        'activity',
        'sleep',
        'vitals',
        'body',
        'labs',
        'cycle',
        'checkins',
        'insights',
      ]);
    });

    test('fromString is case-insensitive and null for unknown', () {
      expect(ShareCategory.fromString('Labs'), ShareCategory.labs);
      expect(ShareCategory.fromString(' CHECKINS '), ShareCategory.checkins);
      expect(ShareCategory.fromString('weather'), isNull);
      expect(ShareCategory.fromString(null), isNull);
    });

    test('fromInt clamps out-of-range values', () {
      expect(ShareCategory.fromInt(3), ShareCategory.body);
      expect(ShareCategory.fromInt(-1), ShareCategory.activity);
      expect(ShareCategory.fromInt(99), ShareCategory.activity);
    });

    test('every value has a display name and description', () {
      for (final c in ShareCategory.values) {
        expect(c.displayName, isNotEmpty);
        expect(c.description, isNotEmpty);
        expect(c.toJson(), c.name);
      }
    });
  });

  group('Role', () {
    test('round trips and falls back to member', () {
      expect(Role.fromString('ADMIN'), Role.admin);
      expect(Role.fromString('member'), Role.member);
      expect(Role.fromString('owner'), Role.member);
      expect(Role.fromInt(0), Role.admin);
      expect(Role.fromInt(7), Role.member);
      expect(Role.admin.displayName, 'Admin');
      expect(Role.member.toJson(), 'member');
    });
  });

  group('SexAtBirth', () {
    test('round trips and falls back to unspecified', () {
      expect(SexAtBirth.fromString('female'), SexAtBirth.female);
      expect(SexAtBirth.fromString('Male'), SexAtBirth.male);
      expect(SexAtBirth.fromString(''), SexAtBirth.unspecified);
      expect(SexAtBirth.fromInt(1), SexAtBirth.male);
      expect(SexAtBirth.fromInt(-3), SexAtBirth.unspecified);
      expect(SexAtBirth.unspecified.displayName, 'Prefer not to say');
      expect(SexAtBirth.female.toJson(), 'female');
    });
  });

  group('Units', () {
    test('round trips and falls back to metric', () {
      expect(Units.fromString('imperial'), Units.imperial);
      expect(Units.fromString('stone'), Units.metric);
      expect(Units.fromInt(1), Units.imperial);
      expect(Units.fromInt(5), Units.metric);
      expect(Units.imperial.displayName, 'Imperial');
      expect(Units.metric.toJson(), 'metric');
    });
  });
}
