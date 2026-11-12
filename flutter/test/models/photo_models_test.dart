import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/assistant.dart';
import 'package:selfkept/models/journal.dart';
import 'package:selfkept/models/medication.dart';
import 'package:selfkept/models/photo.dart';
import 'package:selfkept/services/assistant_service.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/photo_fixtures.dart';

void main() {
  group('PhotoKind', () {
    test('reads every wire value and falls back to other', () {
      for (final kind in PhotoKind.values) {
        expect(PhotoKind.fromString(kind.wireValue), kind);
        expect(kind.toJson(), kind.wireValue);
        expect(kind.displayName, isNotEmpty);
      }
      expect(PhotoKind.fromString('NUTRITION_LABEL'), PhotoKind.nutritionLabel);
      expect(PhotoKind.fromString('selfie'), PhotoKind.other);
      expect(PhotoKind.fromString(null), PhotoKind.other);
    });
  });

  for (final pascal in [false, true]) {
    final casing = pascal ? 'PascalCase' : 'camelCase';
    Map<String, dynamic> json(Map<String, dynamic> body) =>
        pascal ? Map<String, dynamic>.from(pascalCase(body) as Map) : body;

    group('PhotoAnalysis.fromJson with $casing keys', () {
      test('a meal', () {
        final a = PhotoAnalysis.fromJson(json(PhotoFixtures.meal()));
        expect(a.kind, PhotoKind.meal);
        expect(a.shownKind, PhotoKind.meal);
        expect(a.description, 'A plate of salmon, rice and greens.');
        final meal = a.meal!;
        expect(meal.text, 'Salmon, rice and greens');
        expect(meal.tags, ['high_protein', 'vegetables']);
        expect(meal.portion, JournalPortion.usual);
        expect(meal.energyKcal, 620);
        expect(meal.proteinG, 35);
        expect(meal.carbsG, 60);
        expect(meal.fatG, 22.5);
        expect(a.label, isNull);
      });

      test('a nutrition label', () {
        final a = PhotoAnalysis.fromJson(json(PhotoFixtures.label()));
        expect(a.kind, PhotoKind.nutritionLabel);
        final label = a.label!;
        expect(label.product, 'Oat bar');
        expect(label.servingSize, '1 bar (40 g)');
        expect(label.servingsPerContainer, 6);
        expect(label.energyKcal, 230);
        expect(label.proteinG, 3);
        expect(label.carbsG, 31.5);
        expect(label.fatG, 9);
      });

      test('a medicine label', () {
        final a = PhotoAnalysis.fromJson(json(PhotoFixtures.medicine()));
        final m = a.medicine!;
        expect(m.kind, MedicationKind.supplement);
        expect(m.brand, 'Acme');
        expect(m.product, 'Magnesium glycinate');
        expect(m.dose, '2 capsules');
        expect(m.ingredients.single.name, 'Magnesium');
        expect(m.ingredients.single.amount, 200);
        expect(m.ingredients.single.unit, 'mg');
        final draft = m.toDraft();
        expect(draft.product, 'Magnesium glycinate');
        expect(draft.brand, 'Acme');
        expect(draft.ingredients, hasLength(1));
      });

      test('a device display', () {
        final a = PhotoAnalysis.fromJson(json(PhotoFixtures.display()));
        expect(a.kind, PhotoKind.deviceDisplay);
        expect(a.readings.map((r) => r.metricKey), [
          'blood_pressure_sys',
          'blood_pressure_dia',
          'heart_rate_bpm',
        ]);
        expect(a.readings.first.value, 128);
        expect(a.readings.first.unit, 'mmHg');
        expect(a.readings.first.label, 'Systolic pressure');
      });

      test('a lab report', () {
        final a = PhotoAnalysis.fromJson(json(PhotoFixtures.lab()));
        expect(a.kind, PhotoKind.labReport);
        expect(a.lab!.bestByMetric().keys, containsAll(['ferritin', 'tsh']));
      });

      test('something else', () {
        final a = PhotoAnalysis.fromJson(json(PhotoFixtures.other()));
        expect(a.kind, PhotoKind.other);
        expect(a.description, 'A running shoe with a worn heel.');
        expect(a.meal, isNull);
        expect(a.readings, isEmpty);
      });
    });
  }

  test('a kind without its part is shown as other', () {
    final a = PhotoAnalysis.fromJson({
      'kind': 'device_display',
      'description': 'A blank screen.',
      'readings': [
        {'metricKey': '', 'value': 1},
        {'metricKey': 'weight_kg'},
      ],
    });
    expect(a.readings, isEmpty);
    expect(a.shownKind, PhotoKind.other);
  });

  test('PhotoNote sends the kind and a description cut to 300', () {
    final note = PhotoNote(kind: PhotoKind.other, description: 'x' * 400);
    final json = note.toJson();
    expect(json['kind'], 'other');
    expect((json['description'] as String).length, 300);
  });

  for (final pascal in [false, true]) {
    test('AssistantStatus reads photos (${pascal ? 'Pascal' : 'camel'})', () {
      final body = {...AssistantFixtures.status(), 'photos': true};
      final status = AssistantStatus.fromJson(
        pascal ? Map<String, dynamic>.from(pascalCase(body) as Map) : body,
      );
      expect(status.photos, isTrue);
      expect(
        AssistantStatus.fromJson(AssistantFixtures.status()).photos,
        isFalse,
      );
    });
  }

  group('nutrition on the day log', () {
    for (final pascal in [false, true]) {
      test('JournalEntry reads nutrition (${pascal ? 'Pascal' : 'camel'})', () {
        final body = {
          'id': 'j9',
          'kind': 'food',
          'text': 'Oat bar',
          'tags': <String>[],
          'source': 'photo',
          'nutrition': {
            'basis': 'label',
            'energyKcal': 230,
            'proteinG': 3,
            'carbsG': null,
            'fatG': null,
          },
        };
        final entry = JournalEntry.fromJson(
          pascal ? Map<String, dynamic>.from(pascalCase(body) as Map) : body,
        );
        expect(entry.source, 'photo');
        expect(entry.nutrition!.basis, NutritionBasis.label);
        expect(entry.nutrition!.energyKcal, 230);
        expect(entry.nutrition!.carbsG, isNull);
      });
    }

    test('an entry without numbers has no nutrition', () {
      final entry = JournalEntry.fromJson({
        'id': 'j1',
        'kind': 'food',
        'nutrition': {'basis': 'estimate'},
      });
      expect(entry.nutrition, isNull);
      expect(
        JournalEntry.fromJson({'id': 'j2', 'kind': 'food'}).nutrition,
        isNull,
      );
    });

    test('describe words a guess and a label as the contract does', () {
      const guess = JournalNutrition(
        basis: NutritionBasis.estimate,
        energyKcal: 620,
        proteinG: 35,
      );
      expect(
        guess.describe(),
        'About 620 kcal, 35 g protein (guessed from a photo)',
      );
      const label = JournalNutrition(
        basis: NutritionBasis.label,
        energyKcal: 230,
        proteinG: 3,
      );
      expect(label.describe(), '230 kcal, 3 g protein (from the label)');
      const all = JournalNutrition(
        basis: NutritionBasis.label,
        energyKcal: 460.4,
        proteinG: 6,
        carbsG: 63,
        fatG: 18.5,
      );
      expect(
        all.describe(),
        '460 kcal, 6 g protein, 63 g carbohydrate, 18.5 g fat (from the label)',
      );
    });

    test('a draft sends nutrition only when it holds a number', () {
      const plain = JournalDraft(kind: JournalKind.food, text: 'Toast');
      expect(plain.toJson().containsKey('nutrition'), isFalse);
      const empty = JournalDraft(
        kind: JournalKind.food,
        text: 'Toast',
        nutrition: JournalNutrition(basis: NutritionBasis.estimate),
      );
      expect(empty.toJson().containsKey('nutrition'), isFalse);
      const numbers = JournalDraft(
        kind: JournalKind.drink,
        text: 'Juice',
        nutrition: JournalNutrition(
          basis: NutritionBasis.label,
          energyKcal: 110,
        ),
      );
      expect(numbers.toJson()['nutrition'], {
        'basis': 'label',
        'energyKcal': 110.0,
      });
    });
  });

  test('the media type is told by the first bytes', () {
    expect(AssistantService.photoMediaType(PhotoFixtures.jpeg).subtype, 'jpeg');
    expect(
      AssistantService.photoMediaType(
        Uint8List.fromList([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]),
      ).subtype,
      'png',
    );
    expect(
      AssistantService.photoMediaType(
        Uint8List.fromList([
          0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, //
        ]),
      ).subtype,
      'webp',
    );
  });
}
