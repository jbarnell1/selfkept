import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/journal.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/day_log.dart';

import '../helpers/body_fixtures.dart';
import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/journal/tags', (_) => BodyFixtures.tagGroups());
    h.api.on('GET', '/journal', (_) => <Map<String, dynamic>>[]);
  });
  tearDown(() => h.dispose());

  Future<void> pumpRow(WidgetTester tester, {ThemeData? theme}) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(
        const Scaffold(body: SingleChildScrollView(child: AddToToday())),
        theme: theme,
      ),
    );
    await h.journal.load();
    await tester.pumpAndSettle();
  }

  testBothThemes('six chips and an empty day', (tester, theme) async {
    await pumpRow(tester, theme: theme);
    for (final label in [
      'Food',
      'Drink',
      'Symptom',
      'Training',
      'Sleep',
      'Note',
    ]) {
      expect(find.widgetWithText(ActionChip, label), findsOneWidget);
    }
    expect(find.text(AddToToday.emptyLine), findsOneWidget);
  });

  testWidgets('food: tags, a portion and words save at once, then Undo', (
    tester,
  ) async {
    h.api.on('POST', '/journal', (_) => BodyFixtures.journalEntry(id: 'j2'));
    h.api.on('DELETE', '/journal/j2', (_) => null);
    await pumpRow(tester);

    await tester.tap(find.widgetWithText(ActionChip, 'Food'));
    await tester.pumpAndSettle();
    expect(find.text(DayLogSheet.titleFor(JournalKind.food)), findsOneWidget);
    // No calorie or weight fields anywhere.
    expect(find.textContaining('kcal'), findsNothing);
    expect(find.textContaining('calorie'), findsNothing);
    final add = find.widgetWithText(FilledButton, DayLogSheet.saveLabel);
    expect(tester.widget<FilledButton>(add).onPressed, isNull);

    await tester.tap(find.widgetWithText(FilterChip, 'High protein'));
    await tester.tap(find.widgetWithText(ChoiceChip, 'Usual'));
    await tester.enterText(find.byType(TextField), 'Oats and berries');
    await tester.pumpAndSettle();
    await tester.tap(add);
    await tester.pumpAndSettle();

    expect(h.api.calls, contains('POST /journal'));
    expect(h.api.bodies[h.api.calls.indexOf('POST /journal')], {
      'kind': 'food',
      'text': 'Oats and berries',
      'tags': ['high_protein'],
      'portion': 'usual',
    });
    expect(find.text(AddToToday.savedMessage), findsOneWidget);
    expect(find.text('Oats and berries'), findsOneWidget);
    expect(find.text('High protein'), findsOneWidget);

    await tester.tap(find.text(AddToToday.undoLabel));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /journal/j2'));
    expect(find.text('Oats and berries'), findsNothing);
    expect(find.text(AddToToday.emptyLine), findsOneWidget);
  });

  testWidgets('a note has no tags and no portion, only words', (tester) async {
    h.api.on(
      'POST',
      '/journal',
      (_) => BodyFixtures.journalEntry(
        id: 'j3',
        kind: 'note',
        text: 'Travel day',
        tags: const [],
        portion: null,
      ),
    );
    await pumpRow(tester);
    await tester.tap(find.widgetWithText(ActionChip, 'Note'));
    await tester.pumpAndSettle();
    expect(find.byType(FilterChip), findsNothing);
    expect(find.text(DayLogSheet.portionLabel), findsNothing);
    await tester.enterText(find.byType(TextField), 'Travel day');
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, DayLogSheet.saveLabel));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /journal')]
            as Map<String, dynamic>;
    expect(body['kind'], 'note');
    expect(body['portion'], isNull);
  });

  testWidgets('a refused save keeps the sheet open and says why', (
    tester,
  ) async {
    h.api.fail(
      'POST',
      '/journal',
      const ApiException('Nothing to save.', statusCode: 400),
    );
    await pumpRow(tester);
    await tester.tap(find.widgetWithText(ActionChip, 'Drink'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilterChip, 'Alcohol'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, DayLogSheet.saveLabel));
    await tester.pumpAndSettle();
    expect(find.text('Nothing to save.'), findsOneWidget);
    expect(find.text(AddToToday.savedMessage), findsNothing);
  });

  testWidgets('today\'s entries are listed and can be removed', (tester) async {
    h.api.on('GET', '/journal', (_) => [BodyFixtures.journalEntry()]);
    h.api.on('DELETE', '/journal/j1', (_) => null);
    await pumpRow(tester);
    expect(find.text('Oats and berries'), findsOneWidget);
    await tester.tap(find.byTooltip(AddToToday.removeTooltip));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /journal/j1'));
    expect(find.text(AddToToday.removedMessage), findsOneWidget);
    expect(find.text(AddToToday.emptyLine), findsOneWidget);
  });

  testWidgets('loading: a quiet line under the chips', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/journal',
      (_) => Completer<List<Map<String, dynamic>>>().future,
    );
    await tester.pumpWidget(h.wrap(const Scaffold(body: AddToToday())));
    unawaited(h.journal.load());
    await pumpFrames(tester);
    expect(find.text('Checking today\'s entries'), findsOneWidget);
  });

  testWidgets('error: the chips still work and the list can retry', (
    tester,
  ) async {
    h.api.fail(
      'GET',
      '/journal',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await pumpRow(tester);
    expect(find.text(AddToToday.errorLine), findsOneWidget);
    expect(find.widgetWithText(ActionChip, 'Food'), findsOneWidget);
    h.api.on('GET', '/journal', (_) => <Map<String, dynamic>>[]);
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text(AddToToday.emptyLine), findsOneWidget);
  });
}
