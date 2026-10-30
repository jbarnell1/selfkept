import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/widgets/check_in_entry.dart';

import '../helpers/pump.dart';

void main() {
  testBothThemes('shows six mood buttons, a note field and Keep', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      MaterialApp(
        theme: theme,
        home: const Scaffold(body: CheckInEntry(onKeep: _ignore)),
      ),
    );
    expect(find.byType(MoodButton), findsNWidgets(6));
    expect(find.byType(TextField), findsOneWidget);
    expect(find.text('Keep'), findsOneWidget);
    for (final mood in Mood.values) {
      expect(find.bySemanticsLabel(mood.displayName), findsOneWidget);
    }
  });

  testWidgets('tapping a mood selects it and tapping again clears it', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(body: CheckInEntry(onKeep: _ignore)),
      ),
    );
    final good = find.bySemanticsLabel('Good');
    await tester.tap(good);
    await tester.pumpAndSettle();
    MoodButton button() => tester.widget<MoodButton>(
      find.byWidgetPredicate((w) => w is MoodButton && w.mood == Mood.good),
    );
    expect(button().selected, isTrue);
    await tester.tap(good);
    await tester.pumpAndSettle();
    expect(button().selected, isFalse);
  });

  testWidgets('Keep hands the mood and the trimmed note to onKeep', (
    tester,
  ) async {
    await usePhoneSize(tester);
    Mood? gotMood;
    String? gotNote;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: CheckInEntry(
            onKeep: (m, n) {
              gotMood = m;
              gotNote = n;
            },
          ),
        ),
      ),
    );
    await tester.tap(find.bySemanticsLabel('Bright'));
    await tester.enterText(find.byType(TextField), '  slept well  ');
    await tester.tap(find.text('Keep'));
    await tester.pump();
    expect(gotMood, Mood.bright);
    expect(gotNote, 'slept well');
  });

  test('Mood follows the enum pattern', () {
    expect(Mood.fromInt(5), Mood.bright);
    expect(Mood.fromInt(-1), Mood.okay);
    expect(Mood.fromString('LOW'), Mood.low);
    expect(Mood.fromString('nope'), Mood.okay);
    expect(Mood.low.curve, lessThan(Mood.bright.curve));
  });
}

void _ignore(Mood? mood, String note) {}
