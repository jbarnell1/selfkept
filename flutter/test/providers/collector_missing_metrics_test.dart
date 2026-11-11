import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/collectors/health_collector.dart';
import 'package:selfkept/models/metric.dart';
import 'package:selfkept/models/source.dart';
import 'package:selfkept/providers/collector_provider.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/fake_health_collector.dart';
import '../helpers/pump.dart';

/// The four kinds 0.1.6 taught the Android collector to read.
const Set<MetricKey> _new016 = {
  MetricKey.energyIntakeKcal,
  MetricKey.proteinG,
  MetricKey.carbsG,
  MetricKey.fatG,
  MetricKey.leanMassKg,
  MetricKey.boneMassKg,
  MetricKey.bodyWaterKg,
};

MetricPermissions _access({Set<MetricKey> missing = const {}}) =>
    MetricPermissions(
      granted: MetricKey.collectable.difference(missing),
      missing: missing,
    );

void main() {
  late FakeHealthCollector collector;
  late TestHarness h;

  setUp(() async {
    collector = FakeHealthCollector(kind: SourceKind.healthConnect);
    h = TestHarness(collector: collector);
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/sources', (_) => <Map<String, dynamic>>[]);
    h.api.on('POST', '/ingest/batch', (data) {
      final body = data as Map<String, dynamic>;
      return Fixtures.ingestResult(
        accepted: (body['samples'] as List).length,
        cursor: body['cursor'] as String?,
      );
    });
  });

  tearDown(() async {
    h.dispose();
    await collector.close();
  });

  /// A phone that connected on an earlier build.
  Future<void> initConnected() async {
    await h.outbox.writeState('collector.u1.sourceId', 'src-phone');
    await h.collector.init(collect: false);
    expect(h.collector.isConnected, isTrue);
  }

  test('a phone that has not connected gets no card', () async {
    collector.metricAccess = MetricPermissions(missing: MetricKey.collectable);
    await h.collector.init(collect: false);
    expect(h.collector.isConnected, isFalse);
    expect(h.collector.offerMissingMetrics, isFalse);
    expect(h.collector.hasMissingMetrics, isFalse);
  });

  test('a connected phone with nothing granted gets no card', () async {
    collector.metricAccess = MetricPermissions(missing: MetricKey.collectable);
    await initConnected();
    expect(h.collector.offerMissingMetrics, isFalse);
    // Sources still offers the ask.
    expect(h.collector.hasMissingMetrics, isTrue);
  });

  test('everything granted: no card and nothing missing', () async {
    collector.metricAccess = _access();
    await initConnected();
    expect(h.collector.missingMetrics, isEmpty);
    expect(h.collector.offerMissingMetrics, isFalse);
    expect(h.collector.hasMissingMetrics, isFalse);
  });

  test('some kinds missing: the card offers them in everyday words', () async {
    collector.metricAccess = _access(missing: _new016);
    await initConnected();
    expect(h.collector.offerMissingMetrics, isTrue);
    expect(h.collector.missingMetrics, _new016);
    expect(
      MetricKey.everydayList(h.collector.missingMetrics),
      'food and drink, lean mass, bone mass and body water',
    );
  });

  test('a platform that cannot say gets no card', () async {
    collector.metricAccess = null;
    await initConnected();
    expect(h.collector.missingMetrics, isEmpty);
    expect(h.collector.offerMissingMetrics, isFalse);
    expect(h.collector.hasMissingMetrics, isFalse);
  });

  test('keys outside the collectable set are ignored', () async {
    collector.metricAccess = const MetricPermissions(
      granted: {MetricKey.steps},
      missing: {MetricKey.ouraReadiness},
    );
    await initConnected();
    expect(h.collector.missingMetrics, isEmpty);
    expect(h.collector.offerMissingMetrics, isFalse);
  });

  test('Not now is remembered on the device for that set', () async {
    collector.metricAccess = _access(missing: _new016);
    await initConnected();

    await h.collector.notNowForMissingMetrics();

    expect(h.collector.offerMissingMetrics, isFalse);
    // Sources keeps the ask.
    expect(h.collector.hasMissingMetrics, isTrue);
    expect(
      await h.outbox.readState(CollectorProvider.notNowKey),
      'body_water_kg,bone_mass_kg,carbs_g,energy_intake_kcal,fat_g,'
      'lean_mass_kg,protein_g',
    );

    // A fresh start on the same device does not ask again.
    final again = CollectorProvider(
      collector: collector,
      outbox: h.outbox,
      sources: h.sources,
      sync: h.sync,
      auth: h.auth,
      platformName: 'test',
    );
    addTearDown(again.dispose);
    await again.init(collect: false);
    expect(again.missingMetrics, _new016);
    expect(again.offerMissingMetrics, isFalse);
  });

  test('a later build that reads something new asks again', () async {
    collector.metricAccess = _access(missing: _new016);
    await initConnected();
    await h.collector.notNowForMissingMetrics();
    expect(h.collector.offerMissingMetrics, isFalse);

    // A newer build adds blood glucose to what it may read.
    collector.metricAccess = _access(
      missing: {..._new016, MetricKey.bloodGlucoseMmol},
    );
    await h.collector.refreshMetricPermissions();
    expect(h.collector.offerMissingMetrics, isTrue);

    await h.collector.notNowForMissingMetrics();
    expect(h.collector.offerMissingMetrics, isFalse);
  });

  test('allowing one kind elsewhere does not bring the card back', () async {
    collector.metricAccess = _access(missing: _new016);
    await initConnected();
    await h.collector.notNowForMissingMetrics();

    // The person allowed lean mass in the Health Connect settings.
    collector.metricAccess = _access(
      missing: _new016.difference({MetricKey.leanMassKg}),
    );
    await h.collector.refreshMetricPermissions();
    expect(h.collector.offerMissingMetrics, isFalse);
    expect(h.collector.missingMetrics, isNot(contains(MetricKey.leanMassKg)));
  });

  test('Allow asks for every collectable kind and the card goes', () async {
    collector.metricAccess = _access(missing: _new016);
    collector.metricAccessAfterRequest = _access();
    await initConnected();
    final collectsBefore = collector.cursorsSeen.length;

    await h.collector.askForMissingMetrics();

    expect(collector.requested, MetricKey.collectable);
    expect(h.collector.missingMetrics, isEmpty);
    expect(h.collector.offerMissingMetrics, isFalse);
    expect(h.collector.hasMissingMetrics, isFalse);
    expect(h.collector.isAskingForMore, isFalse);
    expect(await h.outbox.readState(CollectorProvider.notNowKey), isNull);
    // The newly allowed kinds are read at once.
    await h.collector.collectAndFlush();
    expect(collector.cursorsSeen.length, greaterThan(collectsBefore));
  });

  test('declining on the sheet hides the card like Not now', () async {
    collector.metricAccess = _access(missing: _new016);
    collector.permission = PermissionState.partial;
    await initConnected();

    await h.collector.askForMissingMetrics();

    expect(collector.requestCount, 1);
    expect(h.collector.offerMissingMetrics, isFalse);
    expect(h.collector.hasMissingMetrics, isTrue);
    expect(h.collector.askedAndStillMissing, isTrue);
    expect(await h.outbox.readState(CollectorProvider.notNowKey), isNotNull);
  });

  test('a sheet that could not open is not taken as a no', () async {
    collector.metricAccess = _access(missing: _new016);
    collector.requestError = PlatformException(code: 'no_activity');
    await initConnected();

    await h.collector.askForMissingMetrics();

    expect(h.collector.offerMissingMetrics, isTrue);
    expect(h.collector.askedAndStillMissing, isFalse);
    expect(await h.outbox.readState(CollectorProvider.notNowKey), isNull);
  });

  test('a resume reads access again', () async {
    collector.metricAccess = _access(missing: _new016);
    await initConnected();
    expect(h.collector.offerMissingMetrics, isTrue);

    collector.metricAccess = _access();
    var notified = 0;
    h.collector.addListener(() => notified++);
    h.collector.onResume();
    await Future<void>.delayed(Duration.zero);
    await h.collector.collectAndFlush();

    expect(h.collector.offerMissingMetrics, isFalse);
    expect(notified, greaterThan(0));
  });

  test('everydayList folds repeats and joins with and', () {
    expect(MetricKey.everydayList(const {}), '');
    expect(
      MetricKey.everydayList({MetricKey.bloodGlucoseMmol}),
      'blood glucose',
    );
    expect(
      MetricKey.everydayList({
        MetricKey.bloodPressureSys,
        MetricKey.bloodPressureDia,
        MetricKey.steps,
      }),
      'steps and blood pressure',
    );
    for (final key in MetricKey.collectable) {
      expect(key.everydayName, isNotNull, reason: key.wireValue);
    }
  });
}
