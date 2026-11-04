import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/today.dart';
import 'package:selfkept/widgets/readiness_row.dart';

void main() {
  final now = DateTime(2026, 11, 3, 18);

  group('ReadinessRow.missingText', () {
    test('no gap means the range is still being learned', () {
      expect(ReadinessRow.missingText(null, now: now), 'no range yet');
    });

    test('a recent last day is named by weekday', () {
      final gap = ReadingGap(lastDay: DateTime(2026, 10, 31));
      expect(ReadinessRow.missingText(gap, now: now), 'no reading since Sat');
    });

    test('an older last day is named by date', () {
      final gap = ReadingGap(lastDay: DateTime(2026, 10, 24));
      expect(
        ReadinessRow.missingText(gap, now: now),
        'no reading since Oct 24',
      );
    });

    test('no reading in two weeks says so', () {
      expect(
        ReadinessRow.missingText(const ReadingGap(), now: now),
        ReadinessRow.noRecentReadingText,
      );
    });
  });
}
