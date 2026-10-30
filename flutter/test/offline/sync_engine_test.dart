import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/offline/outbox_repository.dart';
import 'package:selfkept/offline/sync_engine.dart';
import 'package:selfkept/providers/auth_provider.dart';
import 'package:selfkept/providers/sources_provider.dart';
import 'package:selfkept/services/ingest_service.dart';
import 'package:selfkept/services/sources_service.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/fake_health_collector.dart';

void main() {
  late FakeApiClient api;
  late AuthProvider auth;
  late SourcesProvider sources;
  late MemoryOutboxRepository outbox;
  late SyncEngine engine;
  final now = DateTime.utc(2026, 10, 29, 9);

  setUp(() async {
    api = FakeApiClient();
    auth = AuthProvider(api);
    sources = SourcesProvider(service: SourcesService(api), auth: auth);
    api.on('GET', '/sources', (_) => [Fixtures.source()]);
    await sources.load();
    outbox = MemoryOutboxRepository();
    engine = SyncEngine(
      ingest: IngestService(api),
      outbox: outbox,
      sources: sources,
      batchSize: 2,
      clock: () => now,
    );
  });

  tearDown(() {
    engine.dispose();
    sources.dispose();
    auth.dispose();
  });

  Map<String, dynamic> lastBody() =>
      api.bodies[api.calls.lastIndexOf('POST /ingest/batch')]
          as Map<String, dynamic>;

  test('flush sends pages, marks them sent and persists the cursor', () async {
    api.on('POST', '/ingest/batch', (_) => Fixtures.ingestResult(cursor: 'c9'));
    await outbox.enqueue(
      CollectedBatch(sourceId: 's1', samples: hrSamples(3), cursorAfter: 'c9'),
    );
    await engine.refreshPending();
    expect(engine.pendingCount, 3);

    final clean = await engine.flush();

    expect(clean, isTrue);
    expect(api.calls.where((c) => c == 'POST /ingest/batch').length, 2);
    expect(engine.pendingCount, 0);
    expect(await outbox.count(), 0);
    expect(engine.lastFlushAt, now);
    expect(engine.lastError, isNull);
    expect(engine.isFlushing, isFalse);
    // The cursor from the server lands on the Source.
    expect(sources.byId('s1')!.cursor, 'c9');
    expect(sources.byId('s1')!.lastSyncAt, now);
    expect(sources.byId('s1')!.sampleCount, 120 + 2 + 2);
    final body = lastBody();
    expect(body['sourceId'], 's1');
    expect((body['samples'] as List), hasLength(1));
    expect(body['cursor'], 'c9');
  });

  test(
    'a partial page repeats the last known cursor instead of jumping',
    () async {
      api.on('POST', '/ingest/batch', (data) {
        final body = data as Map<String, dynamic>;
        return Fixtures.ingestResult(cursor: body['cursor'] as String?);
      });
      await outbox.enqueue(
        CollectedBatch(
          sourceId: 's1',
          samples: hrSamples(3),
          cursorAfter: 'new',
        ),
      );
      await engine.flush();
      final firstBody =
          api.bodies[api.calls.indexOf('POST /ingest/batch')]
              as Map<String, dynamic>;
      // First page leaves one row behind: keep the server on the old anchor.
      expect(firstBody['cursor'], 'anchor-1');
      // Last page carries the new anchor.
      expect(lastBody()['cursor'], 'new');
      expect(sources.byId('s1')!.cursor, 'new');
    },
  );

  test('a 500 keeps every row and records the error', () async {
    api.fail(
      'POST',
      '/ingest/batch',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await outbox.enqueue(CollectedBatch(sourceId: 's1', samples: hrSamples(3)));

    final clean = await engine.flush();

    expect(clean, isFalse);
    expect(await outbox.count(), 3);
    expect(engine.pendingCount, 3);
    expect(engine.lastError, 'The server had a problem.');
    expect(engine.lastFlushAt, isNull);
    expect(sources.byId('s1')!.cursor, 'anchor-1');
    // Only one request: the pass stops at the first failure.
    expect(api.calls.where((c) => c == 'POST /ingest/batch').length, 1);
  });

  test(
    'a network failure keeps rows too, and the next flush recovers',
    () async {
      api.fail('POST', '/ingest/batch', const ApiException.network());
      await outbox.enqueue(
        CollectedBatch(sourceId: 's1', samples: hrSamples(2)),
      );
      expect(await engine.flush(), isFalse);
      expect(await outbox.count(), 2);

      api.on('POST', '/ingest/batch', (_) => Fixtures.ingestResult());
      expect(await engine.flush(), isTrue);
      expect(await outbox.count(), 0);
      expect(engine.lastError, isNull);
    },
  );

  test('rejected rows are counted and not retried', () async {
    api.on(
      'POST',
      '/ingest/batch',
      (_) => {
        'accepted': 1,
        'duplicates': 0,
        'rejected': [
          {'sourceUid': 'hk-1', 'reason': 'unknown metric'},
        ],
        'cursor': 'c1',
      },
    );
    await outbox.enqueue(CollectedBatch(sourceId: 's1', samples: hrSamples(2)));
    await engine.flush();
    expect(engine.lastRejected, 1);
    expect(await outbox.count(), 0);
  });

  test('concurrent flushes share one pass and run again if asked', () async {
    final gate = Completer<Map<String, dynamic>>();
    var calls = 0;
    api.on('POST', '/ingest/batch', (_) {
      calls += 1;
      return calls == 1 ? gate.future : Fixtures.ingestResult();
    });
    await outbox.enqueue(CollectedBatch(sourceId: 's1', samples: hrSamples(1)));
    final first = engine.flush();
    expect(engine.isFlushing, isTrue);
    final second = engine.flush();
    expect(identical(first, second), isTrue);
    // Rows that arrive during the pass are picked up by the repeat.
    await outbox.enqueue(
      CollectedBatch(sourceId: 's1', samples: hrSamples(1, from: 5)),
    );
    gate.complete(Fixtures.ingestResult());
    expect(await first, isTrue);
    expect(await outbox.count(), 0);
    expect(engine.isFlushing, isFalse);
  });

  test('enqueueAndFlush queues and sends in one call', () async {
    api.on('POST', '/ingest/batch', (_) => Fixtures.ingestResult());
    expect(
      await engine.enqueueAndFlush(
        CollectedBatch(sourceId: 's1', samples: hrSamples(1)),
      ),
      isTrue,
    );
    expect(api.calls, contains('POST /ingest/batch'));
  });

  test('an empty outbox flushes clean without a request', () async {
    expect(await engine.flush(), isTrue);
    expect(api.calls, isNot(contains('POST /ingest/batch')));
  });
}
