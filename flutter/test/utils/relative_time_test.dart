import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/utils/relative_time.dart';

void main() {
  final now = DateTime(2026, 10, 30, 12);

  test('plain words are lower-cased in a sentence', () {
    expect(relativeTimeInSentence(null), 'never');
    expect(
      relativeTimeInSentence(
        now.subtract(const Duration(seconds: 5)),
        now: now,
      ),
      'just now',
    );
    expect(
      relativeTimeInSentence(DateTime(2026, 10, 29, 21, 14), now: now),
      startsWith('yesterday at '),
    );
  });

  test('weekdays, months and AM or PM keep their capitals', () {
    final monday = relativeTimeInSentence(
      DateTime(2026, 10, 26, 21, 14),
      now: now,
    );
    expect(monday, startsWith('Monday at '));
    expect(monday, endsWith('PM'));
    expect(
      relativeTimeInSentence(DateTime(2026, 9, 1), now: now),
      'Sep 1, 2026',
    );
  });

  test('counts read the same either way', () {
    final at = now.subtract(const Duration(minutes: 12));
    expect(relativeTimeInSentence(at, now: now), relativeTime(at, now: now));
  });
}
