import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/retention.dart';

void main() {
  group('Retention.fromJson', () {
    test('reads camelCase', () {
      final r = Retention.fromJson({
        'rawMonths': 24,
        'source': 'app',
        'minimumMonths': 4,
        'rawSince': '2024-11-01',
        'months': [
          {
            'month': '2024-10-01',
            'rolledAt': '2026-11-04T04:00:00Z',
            'rawRows': 1200000,
            'minuteRows': 43000,
            'compactedAt': '2026-11-04T04:01:00Z',
          },
        ],
      });
      expect(r.rawMonths, 24);
      expect(r.source, 'app');
      expect(r.fromEnvironment, isFalse);
      expect(r.keepsForever, isFalse);
      expect(r.rawSince, DateTime(2024, 11, 1));
      expect(r.months.single.month, DateTime(2024, 10, 1));
      expect(r.months.single.rawRows, 1200000);
      expect(r.months.single.minuteRows, 43000);
      expect(r.months.single.compactedAt, isNotNull);
    });

    test('reads PascalCase', () {
      final r = Retention.fromJson({
        'RawMonths': 0,
        'Source': 'environment',
        'MinimumMonths': 4,
        'RawSince': null,
        'Months': [
          {'Month': '2024-10-01', 'RawRows': 3, 'MinuteRows': 2},
        ],
      });
      expect(r.keepsForever, isTrue);
      expect(r.fromEnvironment, isTrue);
      expect(r.rawSince, isNull);
      expect(r.months.single.minuteRows, 2);
      expect(r.months.single.compactedAt, isNull);
    });

    test('falls back to the defaults on an empty body', () {
      final r = Retention.fromJson({});
      expect(r.rawMonths, 12);
      expect(r.source, 'default');
      expect(r.months, isEmpty);
    });
  });

  group('RetentionRun.fromJson', () {
    test('counts what was folded and compacted, either casing', () {
      final camel = RetentionRun.fromJson({
        'rawSince': '2025-11-01',
        'folded': [
          {
            'month': '2025-09-01',
            'rawRows': 4,
            'minuteRows': 2,
            'compacted': true,
          },
          {
            'month': '2025-10-01',
            'rawRows': 4,
            'minuteRows': 2,
            'compacted': true,
          },
        ],
        'compacted': ['2025-09', '2025-10'],
      });
      expect(camel.folded, 2);
      expect(camel.compacted, 2);

      final pascal = RetentionRun.fromJson({
        'Folded': <Object>[],
        'Compacted': ['2025-09'],
      });
      expect(pascal.folded, 0);
      expect(pascal.compacted, 1);
    });
  });
}
