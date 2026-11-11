import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/collectors/channel_health_collector.dart';
import 'package:selfkept/models/source.dart';

/// The rows HealthConnectCollector.kt writes for one NutritionRecord that
/// carries energy, protein and fat but no carbohydrate, and one
/// LeanBodyMassRecord from a scale.
Map<String, Object?> _page() => {
  'samples': [
    _row('hc-n1-energy', 'energy_intake_kcal', 640, 'kcal'),
    _row('hc-n1-protein', 'protein_g', 38.5, 'g'),
    _row('hc-n1-fat', 'fat_g', 21, 'g'),
    {
      'sourceUid': 'hc-l1',
      'metric': 'lean_mass_kg',
      'start': '2026-11-09T06:30:00Z',
      'end': null,
      'value': 61.2,
      'unit': 'kg',
      'meta': {'hcType': 'LeanBodyMassRecord', 'sourcePackage': 'com.scale'},
    },
  ],
  'workouts': <Object?>[],
  'cursor': '{"NutritionRecord":{"token":"t1","done":true}}',
  'hasMore': false,
};

Map<String, Object?> _row(String uid, String metric, num value, String unit) =>
    {
      'sourceUid': uid,
      'metric': metric,
      'start': '2026-11-09T12:00:00Z',
      'end': '2026-11-09T12:30:00Z',
      'value': value,
      'unit': unit,
      'meta': {'hcType': 'NutritionRecord', 'sourcePackage': 'com.food'},
    };

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel(ChannelHealthCollector.methodChannelName);
  final messenger =
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;

  setUp(() {
    messenger.setMockMethodCallHandler(channel, (call) async {
      if (call.method == 'collectSince') return _page();
      return null;
    });
  });
  tearDown(() => messenger.setMockMethodCallHandler(channel, null));

  test('nutrition and body composition rows reach the outbox shape', () async {
    final collector = ChannelHealthCollector(kind: SourceKind.healthConnect);
    final page = await collector.collectSince(null);

    expect(page.samples, hasLength(4));
    expect(page.hasMore, isFalse);
    expect(page.cursor, contains('NutritionRecord'));

    // One sample per field the record carried; the missing carbohydrate
    // field sends nothing, not a zero.
    final nutrition = page.samples.take(3).toList();
    expect(nutrition.map((s) => s.metric), [
      'energy_intake_kcal',
      'protein_g',
      'fat_g',
    ]);
    expect(page.samples.where((s) => s.metric == 'carbs_g'), isEmpty);
    expect(nutrition.map((s) => s.sourceUid).toSet(), hasLength(3));
    for (final s in nutrition) {
      expect(s.isInterval, isTrue);
      expect(s.start, DateTime.utc(2026, 11, 9, 12));
      expect(s.end, DateTime.utc(2026, 11, 9, 12, 30));
    }
    expect(nutrition[1].value, 38.5);
    expect(nutrition[1].unit, 'g');

    final lean = page.samples.last;
    expect(lean.metric, 'lean_mass_kg');
    expect(lean.isInterval, isFalse);
    expect(lean.value, 61.2);
    expect(lean.toIngestJson(), {
      'sourceUid': 'hc-l1',
      'metric': 'lean_mass_kg',
      'start': '2026-11-09T06:30:00.000Z',
      'end': null,
      'value': 61.2,
      'unit': 'kg',
      'meta': {'hcType': 'LeanBodyMassRecord', 'sourcePackage': 'com.scale'},
    });
  });
}
