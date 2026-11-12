import 'dart:typed_data';

import 'package:selfkept/services/photo_picker.dart';

/// JSON POST /assistant/photos answers with, one per kind.
abstract final class PhotoFixtures {
  /// Starts like a JPEG and is not a picture, so the card shows its stand-in.
  static final Uint8List jpeg = Uint8List.fromList([
    0xFF,
    0xD8,
    0xFF,
    0xE0,
    0,
    0x10,
    0x4A,
    0x46,
  ]);

  static Map<String, dynamic> meal() => {
    'kind': 'meal',
    'description': 'A plate of salmon, rice and greens.',
    'meal': {
      'text': 'Salmon, rice and greens',
      'tags': ['high_protein', 'vegetables'],
      'portion': 'usual',
      'energyKcal': 620,
      'proteinG': 35,
      'carbsG': 60,
      'fatG': 22.5,
    },
  };

  static Map<String, dynamic> label() => {
    'kind': 'nutrition_label',
    'description': 'A nutrition label on an oat bar wrapper.',
    'label': {
      'product': 'Oat bar',
      'servingSize': '1 bar (40 g)',
      'servingsPerContainer': 6,
      'energyKcal': 230,
      'proteinG': 3,
      'carbsG': 31.5,
      'fatG': 9,
    },
  };

  static Map<String, dynamic> medicine() => {
    'kind': 'medicine_label',
    'description': 'A supplement bottle.',
    'medicine': {
      'kind': 'supplement',
      'brand': 'Acme',
      'product': 'Magnesium glycinate',
      'dose': '2 capsules',
      'ingredients': [
        {'name': 'Magnesium', 'amount': 200, 'unit': 'mg'},
      ],
    },
  };

  static Map<String, dynamic> display() => {
    'kind': 'device_display',
    'description': 'A blood pressure cuff display.',
    'readings': [
      {
        'metricKey': 'blood_pressure_sys',
        'value': 128,
        'unit': 'mmHg',
        'label': 'Systolic pressure',
      },
      {
        'metricKey': 'blood_pressure_dia',
        'value': 82,
        'unit': 'mmHg',
        'label': 'Diastolic pressure',
      },
      {
        'metricKey': 'heart_rate_bpm',
        'value': 64,
        'unit': 'bpm',
        'label': 'Pulse',
      },
    ],
  };

  static Map<String, dynamic> scale() => {
    'kind': 'device_display',
    'description': 'A bathroom scale.',
    'readings': [
      {'metricKey': 'weight_kg', 'value': 80, 'unit': 'kg', 'label': 'Weight'},
      {
        'metricKey': 'body_temp_c',
        'value': 37,
        'unit': 'C',
        'label': 'Temperature',
      },
    ],
  };

  static Map<String, dynamic> lab() => {
    'kind': 'lab_report',
    'description': 'A printed lab report.',
    'lab': {
      'candidates': [
        {
          'metricKey': 'ferritin',
          'value': 48,
          'unit': 'ng/mL',
          'confidence': 0.9,
          'rawText': 'Ferritin 48 ng/mL',
        },
        {
          'metricKey': 'tsh',
          'value': 2.1,
          'unit': 'mIU/L',
          'confidence': 0.8,
          'rawText': 'TSH 2.1',
        },
      ],
      'unmatchedLines': <String>[],
    },
  };

  static Map<String, dynamic> other() => {
    'kind': 'other',
    'description': 'A running shoe with a worn heel.',
  };
}

/// Hands over [bytes] for any source and remembers what was asked.
class FakePhotoPicker extends PhotoPicker {
  final Uint8List? bytes;
  final List<PhotoSource> asked = [];

  FakePhotoPicker([this.bytes]);

  @override
  Future<Uint8List?> pick(PhotoSource source) async {
    asked.add(source);
    return bytes ?? PhotoFixtures.jpeg;
  }
}
