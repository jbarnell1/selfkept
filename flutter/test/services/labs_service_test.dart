import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/lab.dart';
import 'package:selfkept/services/labs_service.dart';

import '../helpers/fake_api_client.dart';

void main() {
  late FakeApiClient api;
  late LabsService service;

  setUp(() {
    api = FakeApiClient();
    service = LabsService(api);
  });

  test('parse posts the text and reads the candidates', () async {
    api.on(
      'POST',
      '/labs/parse',
      (_) => {
        'candidates': [
          {
            'metricKey': 'lab.apob',
            'displayName': 'ApoB',
            'value': 92.0,
            'unit': 'mg/dL',
            'flag': null,
            'rawText': 'ApoB 92 mg/dL',
            'confidence': 1.0,
            'line': 0,
          },
        ],
        'unmatchedLines': <String>[],
      },
    );
    final result = await service.parse('ApoB 92 mg/dL\nTSH 2.1');
    expect(api.bodies.single, {'text': 'ApoB 92 mg/dL\nTSH 2.1'});
    expect(result.candidates.single.value, 92);
  });

  test('createReport sends a date only and omits missing parts', () async {
    api.on('POST', '/labs/reports', (_) => Fixtures.labReport());
    final report = await service.createReport(
      provider: 'manual',
      collectedOn: DateTime(2026, 10, 4, 15, 30),
      results: const [
        LabResultInput(metricKey: 'lab.apob', value: 92, unit: 'mg/dL'),
      ],
    );
    final body = api.bodies.single as Map<String, dynamic>;
    expect(body['provider'], 'manual');
    expect(body['collectedOn'], '2026-10-04');
    expect(body.containsKey('image'), isFalse);
    expect(body.containsKey('notes'), isFalse);
    expect((body['results'] as List).single, {
      'metricKey': 'lab.apob',
      'value': 92.0,
      'unit': 'mg/dL',
    });
    expect(report.id, 'r1');
  });

  test('createReport sends the image and notes when given', () async {
    api.on('POST', '/labs/reports', (_) => Fixtures.labReport());
    await service.createReport(
      provider: 'rythm',
      collectedOn: DateTime(2026, 10, 4),
      results: const [],
      imageBase64: 'AAAA',
      notes: 'Fasted',
    );
    final body = api.bodies.single as Map<String, dynamic>;
    expect(body['image'], 'AAAA');
    expect(body['notes'], 'Fasted');
  });

  test('personId is sent only when given', () async {
    api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
    api.on('GET', '/labs/reports', (_) => [Fixtures.labReport()]);
    api.on('GET', '/labs/markers/lab.apob', (_) => Fixtures.labMarkerDetail());
    await service.markers();
    await service.markers(personId: 'p2');
    await service.reports(personId: 'p2');
    final detail = await service.marker('lab.apob');
    expect(api.bodies[0], isEmpty);
    expect(api.bodies[1], {'personId': 'p2'});
    expect(api.bodies[2], {'personId': 'p2'});
    expect(detail.marker.metricKey, 'lab.apob');
  });

  test('summaryPdf asks for yyyy-MM-dd bounds and returns the bytes', () async {
    api.onBytes(
      '/labs/summary.pdf',
      (_) => Uint8List.fromList([37, 80, 68, 70]),
    );
    final bytes = await service.summaryPdf(
      from: DateTime(2026, 7, 31, 8),
      to: DateTime(2026, 10, 29, 23),
    );
    expect(bytes, [37, 80, 68, 70]);
    expect(api.bodies.single, {'from': '2026-07-31', 'to': '2026-10-29'});
  });
}
