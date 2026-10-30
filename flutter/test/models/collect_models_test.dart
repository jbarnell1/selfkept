import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/collectors/health_collector.dart';
import 'package:selfkept/models/checkin.dart';
import 'package:selfkept/models/enums.dart';
import 'package:selfkept/models/metric.dart';
import 'package:selfkept/models/sample.dart';
import 'package:selfkept/models/source.dart';
import 'package:selfkept/providers/collector_provider.dart';

import '../helpers/fake_api_client.dart';

void main() {
  for (final pascal in [false, true]) {
    final casing = pascal ? 'PascalCase' : 'camelCase';

    group('Source ($casing)', () {
      test('fromJson reads every field', () {
        final s = Source.fromJson(Fixtures.source(pascal: pascal));
        expect(s.id, 's1');
        expect(s.kind, SourceKind.healthkit);
        expect(s.deviceName, "Ada's phone");
        expect(s.platform, 'ios');
        expect(s.enabled, isTrue);
        expect(s.lastSyncAt, DateTime.utc(2026, 10, 28, 7));
        expect(s.lastSampleAt, DateTime.utc(2026, 10, 28, 6, 58));
        expect(s.sampleCount, 120);
        expect(s.cursor, 'anchor-1');
      });

      test('a null cursor stays null and enabled defaults to true', () {
        final json = Fixtures.source(pascal: pascal, cursor: null);
        json.remove(pascal ? 'Enabled' : 'enabled');
        final s = Source.fromJson(json);
        expect(s.cursor, isNull);
        expect(s.enabled, isTrue);
      });
    });

    group('Sample and Workout ($casing)', () {
      test('Sample.fromJson handles instant readings and meta', () {
        final s = Sample.fromJson(Fixtures.sample(pascal: pascal));
        expect(s.id, 'smp1');
        expect(s.sourceUid, 'hk-1');
        expect(s.metric, 'heart_rate_bpm');
        expect(s.metricKey, MetricKey.heartRateBpm);
        expect(s.start, DateTime.utc(2026, 10, 29, 6));
        expect(s.end, isNull);
        expect(s.isInterval, isFalse);
        expect(s.value, 58);
        expect(s.unit, 'bpm');
        expect(s.sourceId, 's1');
        expect(s.meta['motion'], 'sedentary');
      });

      test('Workout.fromJson reads numbers and tolerates nulls', () {
        final w = Workout.fromJson(Fixtures.workout(pascal: pascal));
        expect(w.type, 'running');
        expect(w.distanceM, 10020);
        expect(w.energyKcal, 612);
        expect(w.avgHr, 148);
        expect(w.maxHr, 171);
        expect(w.routeGeoJson, isNull);
        expect(w.duration, const Duration(minutes: 50));
      });

      test('SamplesPage and IngestResult parse their envelopes', () {
        final page = SamplesPage.fromJson(
          pascal
              ? {
                  'Items': [Fixtures.sample(pascal: true)],
                  'NextCursor': 'n1',
                }
              : {
                  'items': [Fixtures.sample()],
                  'nextCursor': 'n1',
                },
        );
        expect(page.items, hasLength(1));
        expect(page.nextCursor, 'n1');

        final result = IngestResult.fromJson(
          Fixtures.ingestResult(pascal: pascal),
        );
        expect(result.accepted, 2);
        expect(result.duplicates, 0);
        expect(result.rejected, isEmpty);
        expect(result.cursor, 'anchor-2');
      });
    });

    group('Checkin ($casing)', () {
      test('fromJson reads the DTO and clamps the mood', () {
        final c = Checkin.fromJson(Fixtures.checkin(pascal: pascal));
        expect(c.id, 'c1');
        expect(c.personId, 'p1');
        expect(c.at, DateTime.utc(2026, 10, 29, 8));
        expect(c.mood, 5);
        expect(c.note, 'slept well');
        expect(c.tags, ['morning']);

        final high = Checkin.fromJson(
          Fixtures.checkin(pascal: pascal, mood: 9),
        );
        expect(high.mood, 6);
      });
    });
  }

  group('ingest bodies', () {
    test('Sample.toIngestJson writes UTC ISO times and a null end', () {
      final s = Sample.of(
        MetricKey.heartRateBpm,
        sourceUid: 'hk-1',
        start: DateTime(2026, 10, 29, 7).toUtc(),
        value: 58,
        meta: const {'motion': 'sedentary'},
      );
      final json = s.toIngestJson();
      expect(json['sourceUid'], 'hk-1');
      expect(json['metric'], 'heart_rate_bpm');
      expect(json['unit'], 'bpm');
      expect(json['end'], isNull);
      expect((json['start'] as String).endsWith('Z'), isTrue);
      expect(json['meta'], {'motion': 'sedentary'});
    });

    test('Checkin.toCreateJson omits what is not given', () {
      expect(Checkin.toCreateJson(mood: 4), {'mood': 4});
      expect(Checkin.toCreateJson(mood: 4, note: '  '), {'mood': 4});
      final full = Checkin.toCreateJson(
        mood: 7,
        note: ' fine ',
        at: DateTime.utc(2026, 1, 1),
      );
      expect(full['mood'], 6);
      expect(full['note'], 'fine');
      expect(full['at'], '2026-01-01T00:00:00.000Z');
    });

    test('Source.toCreateJson', () {
      expect(
        Source.toCreateJson(
          kind: SourceKind.healthConnect,
          deviceName: 'Pixel',
          platform: 'android',
          appVersion: '0.1.0',
        ),
        {
          'kind': 'health_connect',
          'deviceName': 'Pixel',
          'platform': 'android',
          'appVersion': '0.1.0',
        },
      );
    });
  });

  group('enums', () {
    test('SourceKind', () {
      expect(SourceKind.fromString('health_connect'), SourceKind.healthConnect);
      expect(SourceKind.fromString('HealthConnect'), SourceKind.healthConnect);
      expect(SourceKind.fromString('lab_scan'), SourceKind.labScan);
      expect(SourceKind.fromString('nope'), SourceKind.manual);
      expect(SourceKind.fromInt(0), SourceKind.healthkit);
      expect(SourceKind.fromInt(99), SourceKind.manual);
      expect(SourceKind.healthkit.toJson(), 'healthkit');
      expect(SourceKind.healthkit.displayName, 'Apple Health');
      expect(SourceKind.oura.isPhoneCollector, isFalse);
    });

    test('MetricKey carries unit, kind and category from the contract', () {
      expect(MetricKey.fromString('sleep_stage'), MetricKey.sleepStage);
      expect(MetricKey.fromString('SLEEP_STAGE'), MetricKey.sleepStage);
      expect(MetricKey.fromString('lab.apob'), isNull);
      expect(MetricKey.fromInt(0), MetricKey.steps);
      expect(MetricKey.steps.toJson(), 'steps');
      expect(MetricKey.respiratoryRateBpm.unit, 'breaths/min');
      expect(MetricKey.vo2max.unit, 'mL/kg/min');
      expect(MetricKey.restingHrBpm.kind, MetricKind.daily);
      expect(MetricKey.menstrualFlow.category, ShareCategory.cycle);
      expect(MetricKey.checkinMood.category, ShareCategory.checkins);
      expect(MetricKey.collectable, isNot(contains(MetricKey.ouraReadiness)));
      expect(MetricKey.collectable, isNot(contains(MetricKey.checkinMood)));
      expect(MetricKey.collectable, contains(MetricKey.steps));
      expect(MetricKey.values.length, 30);
      for (final k in MetricKey.values) {
        expect(k.displayName, isNotEmpty);
        expect(MetricKey.fromString(k.wireValue), k);
      }
    });

    test('SleepStage and MenstrualFlow use the contract integers', () {
      expect(SleepStage.fromInt(0), SleepStage.inBed);
      expect(SleepStage.fromInt(3), SleepStage.deep);
      expect(SleepStage.fromInt(42), SleepStage.unspecified);
      expect(SleepStage.fromString('rem'), SleepStage.rem);
      expect(SleepStage.fromString('in_bed'), SleepStage.inBed);
      expect(SleepStage.deep.toJson(), 3);
      expect(SleepStage.light.displayName, 'Light');

      expect(MenstrualFlow.fromInt(0), MenstrualFlow.none);
      expect(MenstrualFlow.fromInt(3), MenstrualFlow.heavy);
      expect(MenstrualFlow.fromString('medium'), MenstrualFlow.medium);
      expect(MenstrualFlow.fromString('x'), MenstrualFlow.unspecified);
      expect(MenstrualFlow.heavy.toJson(), 3);
    });

    test('MetricKind, PermissionState, CollectorStatus', () {
      expect(MetricKind.fromString('DAILY'), MetricKind.daily);
      expect(MetricKind.fromInt(-1), MetricKind.instant);
      expect(MetricKind.interval.toJson(), 'interval');

      expect(
        PermissionState.fromString('not_determined'),
        PermissionState.notDetermined,
      );
      expect(
        PermissionState.fromString('not-determined'),
        PermissionState.notDetermined,
      );
      expect(PermissionState.fromString('GRANTED'), PermissionState.granted);
      expect(PermissionState.fromInt(3), PermissionState.denied);
      expect(PermissionState.partial.toJson(), 'partial');
      expect(PermissionState.partial.allowsReading, isTrue);
      expect(PermissionState.denied.allowsReading, isFalse);

      expect(
        CollectorStatus.fromString('collecting'),
        CollectorStatus.collecting,
      );
      expect(CollectorStatus.fromInt(1), CollectorStatus.unavailable);
      expect(CollectorStatus.fromInt(50), CollectorStatus.unknown);
      expect(CollectorStatus.connected.toJson(), 'connected');
      expect(CollectorStatus.error.displayName, 'Needs attention');
    });
  });
}
