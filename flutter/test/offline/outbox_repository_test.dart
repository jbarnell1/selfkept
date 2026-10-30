import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/metric.dart';
import 'package:selfkept/models/sample.dart';
import 'package:selfkept/offline/drift_outbox_repository.dart';
import 'package:selfkept/offline/outbox_database.dart';
import 'package:selfkept/offline/outbox_repository.dart';

import '../helpers/fake_health_collector.dart';

/// The same contract is exercised against the in-memory and the SQLite
/// implementation.
void main() {
  group('MemoryOutboxRepository', () {
    outboxContract(() async => MemoryOutboxRepository());
  });

  group('DriftOutboxRepository', () {
    outboxContract(
      () async => DriftOutboxRepository(
        OutboxDatabase(NativeDatabase.memory()),
        clock: () => DateTime.utc(2026, 10, 29, 12),
      ),
    );
  });
}

void outboxContract(Future<OutboxRepository> Function() open) {
  late OutboxRepository box;

  setUp(() async => box = await open());
  tearDown(() => box.close());

  test('starts empty', () async {
    expect(await box.count(), 0);
    expect(await box.nextBatch(), isNull);
  });

  test(
    'enqueue then nextBatch returns rows in order with the cursor',
    () async {
      await box.enqueue(
        CollectedBatch(
          sourceId: 's1',
          samples: hrSamples(3),
          workouts: [
            Workout.fromJson({
              'sourceUid': 'w1',
              'type': 'running',
              'start': '2026-10-29T06:00:00Z',
              'end': '2026-10-29T06:30:00Z',
              'distanceM': 5000,
              'routeGeoJson': {'type': 'LineString'},
              'meta': {'indoor': false},
            }),
          ],
          cursorAfter: 'c1',
        ),
      );
      expect(await box.count(), 4);
      final batch = await box.nextBatch();
      expect(batch, isNotNull);
      expect(batch!.sourceId, 's1');
      expect(batch.samples.map((s) => s.sample.sourceUid), [
        'hk-0',
        'hk-1',
        'hk-2',
      ]);
      expect(batch.samples.first.sample.metric, 'heart_rate_bpm');
      expect(batch.samples.first.sample.unit, 'bpm');
      expect(batch.samples.first.sample.start, DateTime.utc(2026, 10, 29, 6));
      expect(batch.samples.first.sample.end, isNull);
      expect(batch.workouts, hasLength(1));
      expect(batch.workouts.first.workout.distanceM, 5000);
      expect(batch.workouts.first.workout.routeGeoJson, {'type': 'LineString'});
      expect(batch.workouts.first.workout.meta, {'indoor': false});
      expect(batch.cursorAfter, 'c1');
    },
  );

  test('a page that leaves rows behind withholds the cursor', () async {
    await box.enqueue(
      CollectedBatch(sourceId: 's1', samples: hrSamples(5), cursorAfter: 'c1'),
    );
    final first = await box.nextBatch(limit: 3);
    expect(first!.samples, hasLength(3));
    expect(first.cursorAfter, isNull);
    await box.markSent(sampleIds: first.sampleIds);
    expect(await box.count(), 2);
    final second = await box.nextBatch(limit: 3);
    expect(second!.samples, hasLength(2));
    expect(second.cursorAfter, 'c1');
    await box.markSent(sampleIds: second.sampleIds);
    expect(await box.count(), 0);
    expect(await box.nextBatch(), isNull);
  });

  test(
    'the newest collect wins the cursor once everything is in one page',
    () async {
      await box.enqueue(
        CollectedBatch(
          sourceId: 's1',
          samples: hrSamples(2),
          cursorAfter: 'c1',
        ),
      );
      await box.enqueue(
        CollectedBatch(
          sourceId: 's1',
          samples: hrSamples(1, from: 10),
          cursorAfter: 'c2',
        ),
      );
      final batch = await box.nextBatch();
      expect(batch!.samples, hasLength(3));
      expect(batch.cursorAfter, 'c2');
    },
  );

  test('batches never mix sources; the oldest source goes first', () async {
    await box.enqueue(
      CollectedBatch(sourceId: 'a', samples: hrSamples(1), cursorAfter: 'ca'),
    );
    await box.enqueue(
      CollectedBatch(
        sourceId: 'b',
        samples: hrSamples(1, from: 5),
        cursorAfter: 'cb',
      ),
    );
    final first = await box.nextBatch();
    expect(first!.sourceId, 'a');
    expect(first.samples, hasLength(1));
    await box.markSent(sampleIds: first.sampleIds);
    final second = await box.nextBatch();
    expect(second!.sourceId, 'b');
    expect(second.cursorAfter, 'cb');
  });

  test('markSent with unknown ids is harmless', () async {
    await box.enqueue(CollectedBatch(sourceId: 's1', samples: hrSamples(1)));
    await box.markSent(sampleIds: const [999], workoutIds: const [999]);
    expect(await box.count(), 1);
  });

  test('state survives, overwrites and clears', () async {
    expect(await box.readState('k'), isNull);
    await box.writeState('k', 'v1');
    expect(await box.readState('k'), 'v1');
    await box.writeState('k', 'v2');
    expect(await box.readState('k'), 'v2');
    await box.writeState('k', null);
    expect(await box.readState('k'), isNull);
  });

  test('meta round-trips through the row', () async {
    await box.enqueue(
      CollectedBatch(
        sourceId: 's1',
        samples: [
          Sample.of(
            MetricKey.sleepStage,
            sourceUid: 'sl-1',
            start: DateTime.utc(2026, 10, 28, 23),
            end: DateTime.utc(2026, 10, 29, 0, 30),
            value: 3,
            meta: const {'stage': 'deep'},
          ),
        ],
      ),
    );
    final batch = await box.nextBatch();
    final s = batch!.samples.single.sample;
    expect(s.isInterval, isTrue);
    expect(s.end, DateTime.utc(2026, 10, 29, 0, 30));
    expect(s.meta, {'stage': 'deep'});
    expect(s.unit, 'stage');
  });
}
