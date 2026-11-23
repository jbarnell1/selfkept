import 'dart:async';
import 'dart:typed_data';

import 'package:dio/dio.dart' show FormData;
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/enums.dart';
import 'package:selfkept/pages/assistant/assistant_chat_page.dart';
import 'package:selfkept/pages/lab_entry_page.dart';
import 'package:selfkept/pages/you/medicines_page.dart';
import 'package:selfkept/services/photo_picker.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/assistant/chat_view.dart';
import 'package:selfkept/widgets/assistant/photo_cards.dart';
import 'package:selfkept/widgets/day_log.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/body_fixtures.dart';
import '../helpers/fake_event_stream.dart';
import '../helpers/photo_fixtures.dart';
import '../helpers/pump.dart';

/// The `kind` part of a photo request, or null when none was sent.
String? kindOf(Object? body) {
  for (final field in (body as FormData).fields) {
    if (field.key == 'kind') return field.value;
  }
  return null;
}

void main() {
  late TestHarness h;
  late FakePhotoPicker picker;

  setUp(() async {
    h = TestHarness();
    picker = FakePhotoPicker();
    await signInWithAssistant(h);
    h.api.on(
      'GET',
      '/assistant/status',
      (_) => {...AssistantFixtures.status(), 'photos': true},
    );
    h.api.on(
      'GET',
      '/assistant/conversations/c1',
      (_) => AssistantFixtures.conversation(messages: []),
    );
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
  });
  tearDown(() => h.dispose());

  Future<void> pump(WidgetTester tester, {ThemeData? theme}) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(
        AssistantChatPage(conversationId: 'c1', picker: picker),
        theme: theme,
      ),
    );
    await tester.pumpAndSettle();
  }

  /// Opens the sheet and chooses a photo; the read starts.
  Future<void> choosePhoto(WidgetTester tester) async {
    await tester.tap(find.byTooltip(PhotoButton.tooltip));
    await tester.pumpAndSettle();
    await tester.tap(find.text(PhotoSourceSheet.chooseLabel));
    await pumpFrames(tester, frames: 4);
  }

  /// Answers every photo read with [answer].
  void answerWith(dynamic Function(Object? data) answer) =>
      h.api.on('POST', '/assistant/photos', answer);

  List<Object?> bodiesOf(String call) => [
    for (var i = 0; i < h.api.calls.length; i++)
      if (h.api.calls[i] == call) h.api.bodies[i],
  ];

  group('the photo button', () {
    testWidgets('is hidden when the server has photos off', (tester) async {
      h.api.on('GET', '/assistant/status', (_) => AssistantFixtures.status());
      await pump(tester);
      expect(find.byTooltip(ChatComposer.sendTooltip), findsOneWidget);
      expect(find.byTooltip(PhotoButton.tooltip), findsNothing);
    });

    testWidgets('opens the sheet with both sources and the privacy line', (
      tester,
    ) async {
      await pump(tester);
      await tester.tap(find.byTooltip(PhotoButton.tooltip));
      await tester.pumpAndSettle();
      expect(find.text(PhotoSourceSheet.takeLabel), findsOneWidget);
      expect(find.text(PhotoSourceSheet.chooseLabel), findsOneWidget);
      expect(find.text(PhotoSourceSheet.privacyLine), findsOneWidget);
      expect(
        PhotoSourceSheet.privacyLine,
        'The photo goes to your home server and its model, is read once and '
        'is not kept.',
      );
    });

    testWidgets('the sheet has no camera where there is none (the web)', (
      tester,
    ) async {
      await usePhoneSize(tester);
      await tester.pumpWidget(
        h.wrap(const Scaffold(body: PhotoSourceSheet(camera: false))),
      );
      expect(find.text(PhotoSourceSheet.takeLabel), findsNothing);
      expect(find.text(PhotoSourceSheet.chooseLabel), findsOneWidget);
    });

    testWidgets('taking a photo asks the camera', (tester) async {
      answerWith((_) => PhotoFixtures.other());
      await pump(tester);
      await tester.tap(find.byTooltip(PhotoButton.tooltip));
      await tester.pumpAndSettle();
      await tester.tap(find.text(PhotoSourceSheet.takeLabel));
      await tester.pumpAndSettle();
      expect(picker.asked, [PhotoSource.camera]);
    });
  });

  group('a meal', () {
    testWidgets('loading, then the card with the guess line; Save sends the '
        'edited numbers; Undo takes the entry back', (tester) async {
      final answer = Completer<Map<String, dynamic>>();
      answerWith((_) => answer.future);
      h.api.on(
        'POST',
        '/journal',
        (_) => {
          ...BodyFixtures.journalEntry(id: 'j7', text: 'Salmon and rice'),
          'source': 'photo',
        },
      );
      h.api.on('DELETE', '/journal/j7', (_) => null);
      await pump(tester);
      await choosePhoto(tester);

      expect(find.text(PhotoCard.lookingLine), findsOneWidget);
      expect(find.byType(Image), findsWidgets);
      expect(find.byTooltip(PhotoButton.tooltip), findsOneWidget);
      expect(
        tester
            .widget<IconButton>(
              find.ancestor(
                of: find.byIcon(Icons.photo_camera_outlined),
                matching: find.byType(IconButton),
              ),
            )
            .onPressed,
        isNull,
      );
      expect(kindOf(bodiesOf('POST /assistant/photos').single), isNull);

      answer.complete(PhotoFixtures.meal());
      await tester.pumpAndSettle();
      expect(find.text(PhotoCard.lookingLine), findsNothing);
      expect(find.text('A meal'), findsOneWidget);
      expect(find.text(PhotoCard.guessLine), findsOneWidget);
      expect(
        PhotoCard.guessLine,
        'A guess from the picture. It can be well off, so change anything '
        'that looks wrong.',
      );
      expect(find.text('Salmon, rice and greens'), findsOneWidget);
      expect(find.text('620'), findsOneWidget);

      await tester.enterText(
        find.widgetWithText(TextField, 'Salmon, rice and greens'),
        'Salmon and rice',
      );
      await tester.enterText(find.widgetWithText(TextField, '620'), '700');
      await tester.enterText(find.widgetWithText(TextField, '22.5'), '');
      await scrollTo(tester, find.text(PhotoCard.saveLabel));
      await tester.tap(find.text(PhotoCard.saveLabel));
      await tester.pumpAndSettle();

      final sent = bodiesOf('POST /journal').single as Map<String, dynamic>;
      expect(sent['kind'], 'food');
      expect(sent['text'], 'Salmon and rice');
      expect(sent['tags'], ['high_protein', 'vegetables']);
      expect(sent['portion'], 'usual');
      expect(sent['nutrition'], {
        'basis': 'estimate',
        'energyKcal': 700.0,
        'proteinG': 35.0,
        'carbsG': 60.0,
      });

      expect(find.text(PhotoCard.guessLine), findsNothing);
      expect(find.text('Food: Salmon and rice'), findsOneWidget);
      expect(find.byType(Image), findsNothing);
      await tester.tap(find.text(PhotoCard.undoLabel));
      await tester.pumpAndSettle();
      expect(h.api.calls, contains('DELETE /journal/j7'));
      expect(find.text(PhotoCard.undoneLine), findsOneWidget);
    });

    testWidgets('a number that is not a number stops the save', (tester) async {
      answerWith((_) => PhotoFixtures.meal());
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextField, '35'), 'lots');
      await scrollTo(tester, find.text(PhotoCard.saveLabel));
      await tester.tap(find.text(PhotoCard.saveLabel));
      await tester.pumpAndSettle();
      expect(h.api.calls, isNot(contains('POST /journal')));
      expect(find.textContaining('Use numbers only'), findsOneWidget);
    });

    testWidgets('a refused save shows the server message and keeps the card', (
      tester,
    ) async {
      answerWith((_) => PhotoFixtures.meal());
      h.api.fail(
        'POST',
        '/journal',
        const ApiException(
          'Energy must be between 0 and 5000 kcal.',
          statusCode: 400,
        ),
      );
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      await scrollTo(tester, find.text(PhotoCard.saveLabel));
      await tester.tap(find.text(PhotoCard.saveLabel));
      await tester.pumpAndSettle();
      expect(
        find.text('Energy must be between 0 and 5000 kcal.'),
        findsOneWidget,
      );
      expect(find.text(PhotoCard.guessLine), findsOneWidget);
    });

    testWidgets('Discard drops the card and saves nothing', (tester) async {
      answerWith((_) => PhotoFixtures.meal());
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      await scrollTo(tester, find.text(PhotoCard.discardLabel));
      await tester.tap(find.text(PhotoCard.discardLabel));
      await tester.pumpAndSettle();
      expect(find.text(PhotoCard.guessLine), findsNothing);
      expect(find.byType(Image), findsNothing);
      expect(h.api.calls, isNot(contains('POST /journal')));
    });
  });

  group('a nutrition label', () {
    testWidgets('Not right? reads it again as a label; the servings eaten '
        'multiply the totals that are saved', (tester) async {
      answerWith(
        (data) => kindOf(data) == 'nutrition_label'
            ? PhotoFixtures.label()
            : PhotoFixtures.meal(),
      );
      h.api.on(
        'POST',
        '/journal',
        (_) => BodyFixtures.journalEntry(id: 'j8', text: 'Oat bar'),
      );
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      expect(find.text('A meal'), findsOneWidget);

      await scrollTo(tester, find.text(PhotoCard.notRightLabel));
      await tester.tap(find.text(PhotoCard.notRightLabel));
      await tester.pumpAndSettle();
      expect(find.text('A meal'), findsOneWidget);
      for (final other in [
        'A nutrition label',
        'A medicine or supplement label',
        'A scale, cuff or meter',
        'A lab report',
        'Something else',
      ]) {
        expect(find.text(other), findsOneWidget);
      }
      await tester.tap(find.text('A nutrition label'));
      await tester.pumpAndSettle();

      final reads = bodiesOf('POST /assistant/photos');
      expect(reads, hasLength(2));
      expect(kindOf(reads.last), 'nutrition_label');
      expect(find.text('A nutrition label'), findsOneWidget);
      expect(find.text(PhotoCard.guessLine), findsNothing);
      expect(
        find.text('One serving: 1 bar (40 g). 6 servings in the pack'),
        findsOneWidget,
      );
      final totals = find.byKey(const ValueKey('label-totals'));
      expect(
        tester.widget<Text>(totals).data,
        '230 kcal, 3 g protein, 31.5 g carbohydrate, 9 g fat',
      );

      await scrollTo(
        tester,
        find.widgetWithText(TextField, PhotoCard.servingsLabel),
      );
      await tester.enterText(
        find.widgetWithText(TextField, PhotoCard.servingsLabel),
        '2',
      );
      await tester.pumpAndSettle();
      expect(
        tester.widget<Text>(totals).data,
        '460 kcal, 6 g protein, 63 g carbohydrate, 18 g fat',
      );

      await scrollTo(tester, find.text(PhotoCard.saveLabel));
      await tester.tap(find.text(PhotoCard.saveLabel));
      await tester.pumpAndSettle();
      final sent = bodiesOf('POST /journal').single as Map<String, dynamic>;
      expect(sent['text'], 'Oat bar');
      expect(sent['tags'], isEmpty);
      expect(sent['nutrition'], {
        'basis': 'label',
        'energyKcal': 460.0,
        'proteinG': 6.0,
        'carbsG': 63.0,
        'fatG': 18.0,
      });
      expect(find.text('Food: Oat bar'), findsOneWidget);
    });

    test('labelTotal multiplies and rounds to one decimal', () {
      expect(labelTotal(230, 1.5), 345);
      expect(labelTotal(31.5, 0.5), 15.8);
      expect(labelTotal(null, 2), isNull);
    });
  });

  group('errors', () {
    testWidgets('a failed read says why, and Try again reads it again', (
      tester,
    ) async {
      var first = true;
      answerWith((_) {
        if (first) {
          first = false;
          throw const ApiException(
            'The photo could not be read. Try a closer, sharper picture.',
            statusCode: 422,
          );
        }
        return PhotoFixtures.meal();
      });
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      expect(
        find.text(
          'The photo could not be read. Try a closer, sharper picture.',
        ),
        findsOneWidget,
      );
      await tester.tap(find.text(PhotoCard.tryAgainLabel));
      await tester.pumpAndSettle();
      expect(bodiesOf('POST /assistant/photos'), hasLength(2));
      expect(find.text(PhotoCard.guessLine), findsOneWidget);
    });

    testWidgets('a photo over 6 MB is refused without an upload', (
      tester,
    ) async {
      picker = FakePhotoPicker(Uint8List(6 * 1024 * 1024 + 1));
      answerWith((_) => PhotoFixtures.meal());
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      expect(find.text('The photo is larger than 6 MB.'), findsOneWidget);
      expect(bodiesOf('POST /assistant/photos'), isEmpty);
    });

    testWidgets('photos turned off on the server show its message', (
      tester,
    ) async {
      h.api.fail(
        'POST',
        '/assistant/photos',
        const ApiException(
          'Photos are not turned on for this server.',
          statusCode: 409,
        ),
      );
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      expect(
        find.text('Photos are not turned on for this server.'),
        findsOneWidget,
      );
      await tester.tap(find.text(PhotoCard.discardLabel));
      await tester.pumpAndSettle();
      expect(find.text(PhotoCard.tryAgainLabel), findsNothing);
    });
  });

  group('the other kinds', () {
    testWidgets('a medicine label opens the medicines form filled in', (
      tester,
    ) async {
      answerWith((_) => PhotoFixtures.medicine());
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      expect(find.text('Acme Magnesium glycinate'), findsOneWidget);
      expect(find.text('Supplement, 2 capsules'), findsOneWidget);
      expect(find.text('Magnesium 200 mg'), findsOneWidget);
      await tester.tap(find.text(PhotoCard.addToMedicinesLabel));
      await tester.pumpAndSettle();
      expect(find.byType(MedicationEditor), findsOneWidget);
      final product = tester.widget<TextField>(
        find.widgetWithText(TextField, MedicationEditor.productLabel),
      );
      expect(product.controller!.text, 'Magnesium glycinate');
      final brand = tester.widget<TextField>(
        find.widgetWithText(TextField, MedicationEditor.brandLabel),
      );
      expect(brand.controller!.text, 'Acme');
      expect(h.api.calls, isNot(contains('POST /medications')));
    });

    testWidgets('a display: one row per reading, Save sends each one', (
      tester,
    ) async {
      answerWith((_) => PhotoFixtures.display());
      h.api.on(
        'POST',
        '/body/measurements',
        (data) => {
          'id': 'r1',
          'metricKey': (data as Map)['metricKey'],
          'value': data['value'],
          'unit': '',
        },
      );
      h.api.on('GET', '/progress', (_) => throw const ApiException('x'));
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      expect(find.text('Systolic pressure'), findsWidgets);
      expect(find.text('Pulse'), findsWidgets);
      await tester.enterText(find.widgetWithText(TextField, '82'), '84');
      await tester.enterText(find.widgetWithText(TextField, '64'), '');
      await scrollTo(tester, find.text(PhotoCard.saveLabel));
      await tester.tap(find.text(PhotoCard.saveLabel));
      await tester.pumpAndSettle();
      final sent = bodiesOf('POST /body/measurements');
      expect(sent, hasLength(2));
      expect((sent[0] as Map)['metricKey'], 'blood_pressure_sys');
      expect((sent[0] as Map)['value'], 128.0);
      expect((sent[1] as Map)['metricKey'], 'blood_pressure_dia');
      expect((sent[1] as Map)['value'], 84.0);
      expect(find.text('Saved 2 readings'), findsOneWidget);
      expect(find.text(PhotoCard.undoLabel), findsNothing);
    });

    test('display units: pounds and Fahrenheit for imperial', () {
      expect(ReadingUnits.unit('weight_kg', 'kg', Units.imperial), 'lb');
      expect(ReadingUnits.unit('body_temp_c', 'C', Units.imperial), 'F');
      expect(ReadingUnits.unit('body_temp_c', 'C', Units.metric), 'C');
      expect(ReadingUnits.shown('body_temp_c', 37, Units.imperial), 98.6);
      expect(
        ReadingUnits.stored('body_temp_c', 98.6, Units.imperial),
        closeTo(37, 0.001),
      );
      expect(
        ReadingUnits.stored(
          'weight_kg',
          ReadingUnits.shown('weight_kg', 80, Units.imperial),
          Units.imperial,
        ),
        closeTo(80, 0.001),
      );
      expect(ReadingUnits.shown('spo2_pct', 97, Units.imperial), 97);
    });

    testWidgets('a lab report counts the markers and opens the review', (
      tester,
    ) async {
      answerWith((_) => PhotoFixtures.lab());
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      expect(find.text('Found 2 markers on the report.'), findsOneWidget);
      await tester.tap(find.text(PhotoCard.reviewLabel));
      await tester.pumpAndSettle();
      final page = tester.widget<LabEntryPage>(find.byType(LabEntryPage));
      expect(page.prefill!.candidates, hasLength(2));
      expect(page.prefillProvider, 'other');
    });

    testWidgets('something else goes with the question and the reply '
        'streams in', (tester) async {
      answerWith((_) => PhotoFixtures.other());
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      expect(find.text('Something else'), findsOneWidget);
      expect(find.text('A running shoe with a worn heel.'), findsOneWidget);
      expect(find.text(AssistantChatPage.photoHint), findsOneWidget);

      h.streams.answer(200);
      await tester.enterText(
        find.byType(TextField).last,
        'Time for new shoes?',
      );
      await tester.tap(find.byTooltip(ChatComposer.sendTooltip));
      await pumpFrames(tester, frames: 2);
      final request = h.streams.requests.single.json;
      expect(request['text'], 'Time for new shoes?');
      expect(request['photo'], {
        'kind': 'other',
        'description': 'A running shoe with a worn heel.',
      });
      expect(
        find.text(
          'Time for new shoes?\nPhoto: A running shoe with a worn heel.',
        ),
        findsOneWidget,
      );
      expect(find.text('Something else'), findsNothing);

      h.streams.frame('delta', {'text': 'The heel looks worn down.'});
      h.streams.frame(
        'done',
        StreamFixtures.done(text: 'The heel looks worn down.'),
      );
      await h.streams.close();
      await tester.pumpAndSettle();
      expect(find.text('The heel looks worn down.'), findsOneWidget);
      expect(find.text(AssistantChatPage.hint), findsOneWidget);
    });

    testWidgets('something else can be sent with no question', (tester) async {
      answerWith((_) => PhotoFixtures.other());
      await pump(tester);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      h.streams.answer(200);
      await tester.tap(find.text(PhotoCard.sendLabel));
      await pumpFrames(tester, frames: 2);
      final request = h.streams.requests.single.json;
      expect(request['text'], '');
      expect(request['photo']['kind'], 'other');
      expect(
        find.text(
          'What is in this photo?\nPhoto: A running shoe with a worn heel.',
        ),
        findsOneWidget,
      );
      h.streams.frame('done', StreamFixtures.done(text: 'A worn shoe.'));
      await h.streams.close();
      await tester.pumpAndSettle();
      expect(find.text('A worn shoe.'), findsOneWidget);
    });
  });

  testBothThemes('smoke: every card renders', (tester, theme) async {
    final answers = [
      PhotoFixtures.meal(),
      PhotoFixtures.label(),
      PhotoFixtures.medicine(),
      PhotoFixtures.display(),
      PhotoFixtures.lab(),
      PhotoFixtures.other(),
    ];
    h.api.on('GET', '/progress', (_) => throw const ApiException('x'));
    for (final answer in answers) {
      answerWith((_) => answer);
      await pump(tester, theme: theme);
      await choosePhoto(tester);
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.text(PhotoCard.notRightLabel), findsOneWidget);
      expect(find.text(PhotoCard.discardLabel), findsOneWidget);
      // A fresh page for the next card.
      await tester.pumpWidget(const SizedBox());
    }
  });

  group('the day log', () {
    testBothThemes('shows the numbers and where they came from', (
      tester,
      theme,
    ) async {
      h.api.on('GET', '/journal/tags', (_) => BodyFixtures.tagGroups());
      h.api.on(
        'GET',
        '/journal',
        (_) => [
          {
            ...BodyFixtures.journalEntry(id: 'j1', text: 'Salmon and rice'),
            'source': 'photo',
            'nutrition': {
              'basis': 'estimate',
              'energyKcal': 620,
              'proteinG': 35,
            },
          },
          {
            ...BodyFixtures.journalEntry(
              id: 'j2',
              text: 'Oat bar',
              tags: const [],
            ),
            'source': 'photo',
            'nutrition': {'basis': 'label', 'energyKcal': 230, 'proteinG': 3},
          },
          BodyFixtures.journalEntry(id: 'j3', text: 'Toast'),
        ],
      );
      await usePhoneSize(tester);
      await tester.pumpWidget(
        h.wrap(
          const Scaffold(body: SingleChildScrollView(child: AddToToday())),
          theme: theme,
        ),
      );
      await h.journal.load();
      await tester.pumpAndSettle();
      expect(
        find.text('About 620 kcal, 35 g protein (guessed from a photo)'),
        findsOneWidget,
      );
      expect(
        find.text('230 kcal, 3 g protein (from the label)'),
        findsOneWidget,
      );
      expect(find.byKey(const ValueKey('entry-numbers-j3')), findsNothing);
    });
  });
}
