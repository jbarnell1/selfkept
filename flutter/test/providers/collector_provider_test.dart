import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/collectors/health_collector.dart';
import 'package:selfkept/models/metric.dart';
import 'package:selfkept/models/source.dart';
import 'package:selfkept/providers/collector_provider.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/fake_health_collector.dart';
import '../helpers/pump.dart';

void main() {
  late FakeHealthCollector collector;
  late TestHarness h;

  setUp(() async {
    collector = FakeHealthCollector();
    h = TestHarness(collector: collector);
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/sources', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'POST',
      '/sources',
      (_) => Fixtures.source(id: 'src-phone', cursor: null, sampleCount: 0),
    );
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

  test('init on a platform without a store ends unavailable', () async {
    collector.available = false;
    await h.collector.init();
    expect(h.collector.status, CollectorStatus.unavailable);
    expect(h.collector.isAvailable, isFalse);
    expect(await h.collector.connect(), isFalse);
  });

  test('init with a store and no saved source is disconnected', () async {
    await h.collector.init();
    expect(h.collector.status, CollectorStatus.disconnected);
    expect(h.collector.isConnected, isFalse);
  });

  test(
    'connect asks permission, registers, backfills in pages and flushes',
    () async {
      await h.collector.init();
      collector.pages.addAll([
        CollectResult(samples: hrSamples(2), cursor: 'c1', hasMore: true),
        CollectResult(samples: hrSamples(1, from: 2), cursor: 'c2'),
      ]);
      final statuses = <CollectorStatus>[];
      h.collector.addListener(() => statuses.add(h.collector.status));

      final ok = await h.collector.connect();

      expect(ok, isTrue);
      expect(collector.requested, MetricKey.collectable);
      expect(h.collector.sourceId, 'src-phone');
      expect(h.collector.status, CollectorStatus.connected);
      expect(statuses, contains(CollectorStatus.connecting));
      expect(statuses, contains(CollectorStatus.collecting));
      expect(h.collector.error, isNull);
      expect(h.collector.progress, isNull);
      // Two pages: the first said hasMore.
      expect(collector.cursorsSeen, [null, 'c1']);
      expect(collector.backfillsSeen.every((b) => b != null), isTrue);
      final backfill = DateTime.now().toUtc().difference(
        collector.backfillsSeen.first!,
      );
      expect(backfill.inDays, inInclusiveRange(89, 91));
      expect(h.collector.cursor, 'c2');
      expect(h.collector.lastCollected, 3);
      expect(h.collector.lastCollectAt, isNotNull);
      // Everything was sent and the outbox is empty.
      expect(await h.outbox.count(), 0);
      expect(h.sync.pendingCount, 0);
      expect(h.api.calls.where((c) => c == 'POST /ingest/batch').length, 2);
      expect(h.sources.byId('src-phone')!.cursor, 'c2');
      expect(h.sources.byId('src-phone')!.sampleCount, 3);
      // The connection is remembered locally, keyed by the signed-in user.
      expect(await h.outbox.readState('collector.u1.sourceId'), 'src-phone');
      expect(await h.outbox.readState('collector.u1.cursor'), 'c2');
      final created = h.api.bodies[h.api.calls.indexOf('POST /sources')] as Map;
      expect(created['kind'], 'healthkit');
      expect(created['deviceName'], 'Test phone');
      expect(created['platform'], 'test');
    },
  );

  test(
    'denied permission leaves the collector disconnected with a message',
    () async {
      collector.permission = PermissionState.denied;
      await h.collector.init();
      expect(await h.collector.connect(), isFalse);
      expect(h.collector.status, CollectorStatus.disconnected);
      expect(h.collector.error, contains('Permission'));
      expect(h.api.calls, isNot(contains('POST /sources')));
    },
  );

  test('a refused source registration is reported', () async {
    h.api.fail(
      'POST',
      '/sources',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await h.collector.init();
    expect(await h.collector.connect(), isFalse);
    expect(h.collector.status, CollectorStatus.error);
    expect(h.collector.error, 'The server had a problem.');
    expect(h.collector.sourceId, isNull);
  });

  test('a failed flush keeps rows pending and stops paging', () async {
    await h.collector.init();
    h.api.fail('POST', '/ingest/batch', const ApiException.network());
    collector.pages.addAll([
      CollectResult(samples: hrSamples(2), cursor: 'c1', hasMore: true),
      CollectResult(samples: hrSamples(2, from: 2), cursor: 'c2'),
    ]);

    final ok = await h.collector.connect();

    expect(ok, isFalse);
    expect(h.collector.isConnected, isTrue);
    expect(h.collector.status, CollectorStatus.connected);
    expect(h.collector.error, isNotNull);
    expect(await h.outbox.count(), 2);
    expect(h.sync.pendingCount, 2);
    expect(collector.cursorsSeen, hasLength(1));
    expect(collector.pages, hasLength(1));

    // Back online: a sync sends what waited and reads the next page.
    h.api.on(
      'POST',
      '/ingest/batch',
      (_) => Fixtures.ingestResult(cursor: 'ok'),
    );
    await h.collector.collectAndFlush();
    expect(await h.outbox.count(), 0);
    expect(h.collector.error, isNull);
    expect(collector.cursorsSeen.last, 'c1');
  });

  test('init restores a saved connection and collects at once', () async {
    await h.outbox.writeState('collector.u1.sourceId', 'src-phone');
    await h.outbox.writeState('collector.u1.cursor', 'saved');
    h.api.on(
      'GET',
      '/sources',
      (_) => [Fixtures.source(id: 'src-phone', cursor: 'server')],
    );
    await h.sources.load();
    collector.pages.add(CollectResult(samples: hrSamples(1), cursor: 'c3'));

    await h.collector.init();
    await h.collector.collectAndFlush();

    expect(h.collector.isConnected, isTrue);
    expect(collector.cursorsSeen.first, 'saved');
    expect(h.collector.cursor, 'c3');
  });

  test(
    'a saved source without a local cursor falls back to the server one',
    () async {
      await h.outbox.writeState('collector.u1.sourceId', 'src-phone');
      h.api.on(
        'GET',
        '/sources',
        (_) => [Fixtures.source(id: 'src-phone', cursor: 'server')],
      );
      await h.sources.load();
      await h.collector.init();
      await h.collector.collectAndFlush();
      expect(collector.cursorsSeen.first, 'server');
    },
  );

  test('a change event and a resume each trigger a collect', () async {
    await h.collector.init();
    await h.collector.connect();
    final before = collector.cursorsSeen.length;
    collector.emitChange();
    await Future<void>.delayed(Duration.zero);
    await h.collector.collectAndFlush();
    expect(collector.cursorsSeen.length, greaterThan(before));
    final afterChange = collector.cursorsSeen.length;
    h.collector.onResume();
    await h.collector.collectAndFlush();
    expect(collector.cursorsSeen.length, greaterThan(afterChange));
  });

  test('a collector exception is surfaced and leaves the connection', () async {
    await h.collector.init();
    await h.collector.connect();
    collector.collectError = StateError('store closed');
    await h.collector.collectAndFlush();
    expect(h.collector.status, CollectorStatus.connected);
    expect(h.collector.error, isNotNull);
  });

  test('disconnect forgets the local state only', () async {
    await h.collector.init();
    await h.collector.connect();
    await h.collector.disconnect();
    expect(h.collector.sourceId, isNull);
    expect(h.collector.status, CollectorStatus.disconnected);
    expect(await h.outbox.readState('collector.u1.sourceId'), isNull);
    expect(h.api.calls, isNot(contains('DELETE /sources/src-phone')));
  });

  test('signing out resets the collector', () async {
    await h.collector.init();
    await h.collector.connect();
    h.api.on('POST', '/auth/logout', (_) => null);
    await h.auth.logout();
    expect(h.collector.sourceId, isNull);
    expect(h.collector.status, CollectorStatus.disconnected);
  });

  test('the collector kind follows the platform collector', () {
    expect(h.collector.kind, SourceKind.healthkit);
  });
}
