import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/band_position.dart';
import 'package:selfkept/models/lab.dart';

import '../helpers/fake_api_client.dart';

Map<String, dynamic> _pascalDeep(Map<String, dynamic> map) => {
  for (final e in map.entries)
    e.key[0].toUpperCase() + e.key.substring(1): switch (e.value) {
      Map<String, dynamic> m => _pascalDeep(m),
      List<dynamic> l => [
        for (final x in l)
          if (x is Map<String, dynamic>) _pascalDeep(x) else x,
      ],
      final other => other,
    },
};

void main() {
  group('LabGroup', () {
    test('fromString is case insensitive and falls back to other', () {
      expect(LabGroup.fromString('Heart'), LabGroup.heart);
      expect(LabGroup.fromString(' thyroid '), LabGroup.thyroid);
      expect(LabGroup.fromString('nope'), LabGroup.other);
      expect(LabGroup.fromString(null), LabGroup.other);
    });

    test('display names and wire values', () {
      expect(LabGroup.blood.displayName, 'Blood count');
      expect(LabGroup.hormones.displayName, 'Hormones');
      expect(LabGroup.other.displayName, 'Other');
      expect(LabGroup.kidney.toJson(), 'kidney');
      expect(LabGroup.fromInt(2), LabGroup.heart);
      expect(LabGroup.fromInt(99), LabGroup.other);
    });
  });

  group('LabSeverity', () {
    test('fromString is null when absent or unknown', () {
      expect(LabSeverity.fromString('watch'), LabSeverity.watch);
      expect(LabSeverity.fromString('Clinician'), LabSeverity.clinician);
      expect(LabSeverity.fromString(null), isNull);
      expect(LabSeverity.fromString('red'), isNull);
    });

    test('display names and wire values', () {
      expect(LabSeverity.watch.displayName, 'Watch');
      expect(LabSeverity.clinician.displayName, 'Clinician');
      expect(LabSeverity.clinician.toJson(), 'clinician');
      expect(LabSeverity.fromInt(1), LabSeverity.clinician);
      expect(LabSeverity.fromInt(-1), LabSeverity.watch);
    });
  });

  group('LabResultInput', () {
    test('toJson omits nulls', () {
      const minimal = LabResultInput(
        metricKey: 'lab.apob',
        value: 92,
        unit: 'mg/dL',
      );
      expect(minimal.toJson(), {
        'metricKey': 'lab.apob',
        'value': 92.0,
        'unit': 'mg/dL',
      });
      const full = LabResultInput(
        metricKey: 'lab.apob',
        value: 92,
        unit: 'mg/dL',
        flag: 'high',
        rawText: 'ApoB 92 mg/dL',
      );
      expect(full.toJson()['flag'], 'high');
      expect(full.toJson()['rawText'], 'ApoB 92 mg/dL');
    });
  });

  group('LabMarker', () {
    for (final pascal in [false, true]) {
      test('fromJson (${pascal ? 'PascalCase' : 'camelCase'})', () {
        var json = Fixtures.labMarker();
        if (pascal) json = _pascalDeep(json);
        final m = LabMarker.fromJson(json);
        expect(m.metricKey, 'lab.apob');
        expect(m.displayName, 'ApoB');
        expect(m.unit, 'mg/dL');
        expect(m.group, LabGroup.heart);
        expect(m.applicable, isTrue);
        expect(m.goodDirection, 'down');
        expect(m.latestValue, 92);
        expect(m.latestDate, DateTime.parse('2026-10-24'));
        expect(m.previousValue, 78);
        expect(m.populationRange!.low, 40);
        expect(m.populationRange!.high, 90);
        expect(m.optimalRange!.low, isNull);
        expect(m.optimalRange!.high, 80);
        expect(m.tier, LabSeverity.watch);
        expect(m.position, BandPosition.above);
        expect(m.severity, LabSeverity.watch);
        expect(m.lastThree, hasLength(3));
        expect(m.lastThree.first.date, DateTime.parse('2026-08-20'));
        expect(m.lastThree.last.value, 92);
        expect(m.isRatio, isFalse);
      });
    }

    test('missing optionals stay null and ratios are detected', () {
      final m = LabMarker.fromJson(
        Fixtures.labMarker(
          metricKey: 'lab.tg_hdl_ratio',
          latestValue: null,
          latestDate: null,
          previousValue: null,
          populationRange: null,
          optimalRange: null,
          tier: null,
          position: null,
          severity: null,
        ),
      );
      expect(m.hasValue, isFalse);
      expect(m.populationRange, isNull);
      expect(m.position, isNull);
      expect(m.severity, isNull);
      expect(m.lastThree, isEmpty);
      expect(m.isRatio, isTrue);
      expect(
        LabMarker.fromJson(Fixtures.labMarker(metricKey: 'lab.ldl_apob_ratio')),
        isA<LabMarker>().having((x) => x.isRatio, 'isRatio', isTrue),
      );
      expect(
        LabMarker.fromJson(Fixtures.labMarker(metricKey: 'lab.tc_hdl_ratio')),
        isA<LabMarker>().having((x) => x.isRatio, 'isRatio', isTrue),
      );
    });

    test('lastThree is sorted oldest first', () {
      final m = LabMarker.fromJson(
        Fixtures.labMarker(
          lastThree: [
            Fixtures.labPoint(date: '2026-10-24', value: 3),
            Fixtures.labPoint(date: '2026-08-20', value: 1),
          ],
        ),
      );
      expect([for (final p in m.lastThree) p.value], [1, 3]);
    });
  });

  group('LabReport and LabResult', () {
    for (final pascal in [false, true]) {
      test('fromJson (${pascal ? 'PascalCase' : 'camelCase'})', () {
        var json = Fixtures.labReport(results: 2);
        if (pascal) json = _pascalDeep(json);
        final r = LabReport.fromJson(json);
        expect(r.id, 'r1');
        expect(r.personId, 'p1');
        expect(r.provider, 'rythm');
        expect(r.providerLabel, 'Rythm');
        expect(r.collectedOn, DateTime.parse('2026-10-24'));
        expect(r.receivedOn, DateTime.parse('2026-10-26'));
        expect(r.hasImage, isTrue);
        expect(r.notes, 'Fasted');
        expect(r.createdAt, isNotNull);
        expect(r.confirmedAt, isNotNull);
        expect(r.results, hasLength(2));
        final x = r.results.first;
        expect(x.metricKey, 'lab.k0');
        expect(x.value, 92);
        expect(x.unit, 'mg/dL');
        expect(x.flag, 'high');
        expect(x.rawText, 'ApoB 92 mg/dL');
        expect(x.derived, isFalse);
        expect(x.referenceLow, 40);
        expect(x.referenceHigh, 90);
      });
    }

    test('listFromJson reads an array', () {
      final list = LabReport.listFromJson([
        Fixtures.labReport(),
        Fixtures.labReport(id: 'r2', provider: 'manual'),
      ]);
      expect(list.map((r) => r.id), ['r1', 'r2']);
      expect(list.last.providerLabel, 'Manual');
    });
  });

  group('LabCandidate and ParseResult', () {
    for (final pascal in [false, true]) {
      test('fromJson (${pascal ? 'PascalCase' : 'camelCase'})', () {
        var json = <String, dynamic>{
          'candidates': [
            {
              'metricKey': 'lab.apob',
              'displayName': 'ApoB',
              'value': 92.0,
              'unit': 'mg/dL',
              'flag': 'high',
              'rawText': 'ApoB 92 mg/dL',
              'confidence': 1.0,
              'line': 0,
            },
            {
              'metricKey': 'lab.apob',
              'displayName': 'ApoB',
              'value': 95,
              'unit': null,
              'flag': null,
              'rawText': 'ApoB 95',
              'confidence': 0.7,
              'line': 4,
            },
          ],
          'unmatchedLines': ['Some heading'],
        };
        if (pascal) json = _pascalDeep(json);
        final result = ParseResult.fromJson(json);
        expect(result.candidates, hasLength(2));
        expect(result.unmatchedLines, ['Some heading']);
        final c = result.candidates.first;
        expect(c.metricKey, 'lab.apob');
        expect(c.value, 92);
        expect(c.unit, 'mg/dL');
        expect(c.flag, 'high');
        expect(c.confidence, 1.0);
        expect(c.line, 0);
        expect(result.candidates.last.unit, isNull);
        // The most confident candidate wins.
        expect(result.bestByMetric()['lab.apob']!.value, 92);
      });
    }

    test('a tie keeps the first candidate', () {
      const a = LabCandidate(metricKey: 'k', value: 1, confidence: 0.7);
      const b = LabCandidate(metricKey: 'k', value: 2, confidence: 0.7);
      expect(
        const ParseResult(candidates: [a, b]).bestByMetric()['k']!.value,
        1,
      );
    });
  });

  group('LabMarkerDetail', () {
    for (final pascal in [false, true]) {
      test('fromJson (${pascal ? 'PascalCase' : 'camelCase'})', () {
        var json = Fixtures.labMarkerDetail();
        if (pascal) json = _pascalDeep(json);
        final d = LabMarkerDetail.fromJson(json);
        expect(d.marker.metricKey, 'lab.apob');
        expect(d.marker.populationRange!.low, 66);
        expect(d.blurb, startsWith('ApoB counts'));
        expect(d.whatMovesIt, startsWith('Saturated fat'));
        expect(d.sourceUrl, 'https://example.test/apob');
        expect(d.history, hasLength(3));
        expect(d.history.first.flag, 'high');
        expect(d.history.first.date, DateTime.parse('2026-10-24'));
      });
    }

    test('sourceUrl is optional', () {
      final d = LabMarkerDetail.fromJson(
        Fixtures.labMarkerDetail(sourceUrl: null),
      );
      expect(d.sourceUrl, isNull);
    });
  });

  group('formatting', () {
    test('formatLabValue', () {
      expect(formatLabValue(92), '92');
      expect(formatLabValue(1180), '1,180');
      expect(formatLabValue(4.5), '4.5');
      expect(formatLabValue(0.826), '0.83');
    });

    test('labDateOnly', () {
      expect(labDateOnly(DateTime(2026, 3, 5)), '2026-03-05');
    });
  });

  test('a plain ratio prints no unit; other units print as given', () {
    final ratio = LabMarker.fromJson(
      Fixtures.labMarker(metricKey: 'lab.tg_hdl_ratio', unit: 'ratio'),
    );
    final apob = LabMarker.fromJson(Fixtures.labMarker());
    expect(ratio.displayUnit, '');
    expect(apob.displayUnit, 'mg/dL');
  });
}
