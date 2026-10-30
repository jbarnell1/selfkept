import 'dart:convert';

import 'package:drift/drift.dart';

import '../models/sample.dart';
import 'outbox_database.dart';
import 'outbox_repository.dart';

/// SQLite-backed outbox for the phone apps.
class DriftOutboxRepository implements OutboxRepository {
  final OutboxDatabase _db;
  final DateTime Function() _clock;

  DriftOutboxRepository(this._db, {DateTime Function()? clock})
    : _clock = clock ?? (() => DateTime.now().toUtc());

  @override
  Future<void> enqueue(CollectedBatch batch) async {
    if (batch.isEmpty) return;
    final now = _clock();
    await _db.batch((b) {
      b.insertAll(_db.pendingSamples, [
        for (final s in batch.samples)
          PendingSamplesCompanion.insert(
            sourceId: batch.sourceId,
            sourceUid: s.sourceUid,
            metric: s.metric,
            start: s.start.toUtc(),
            end: Value(s.end?.toUtc()),
            value: s.value,
            unit: s.unit,
            meta: Value(jsonEncode(s.meta)),
            cursorAfter: Value(batch.cursorAfter),
            created: now,
          ),
      ]);
      b.insertAll(_db.pendingWorkouts, [
        for (final w in batch.workouts)
          PendingWorkoutsCompanion.insert(
            sourceId: batch.sourceId,
            sourceUid: w.sourceUid,
            type: w.type,
            start: w.start.toUtc(),
            end: w.end.toUtc(),
            distanceM: Value(w.distanceM),
            energyKcal: Value(w.energyKcal),
            avgHr: Value(w.avgHr),
            maxHr: Value(w.maxHr),
            routeGeoJson: Value(
              w.routeGeoJson == null ? null : jsonEncode(w.routeGeoJson),
            ),
            meta: Value(jsonEncode(w.meta)),
            cursorAfter: Value(batch.cursorAfter),
            created: now,
          ),
      ]);
    });
  }

  @override
  Future<OutboxBatch?> nextBatch({
    int limit = defaultOutboxBatchSize,
    int workoutLimit = 200,
  }) async {
    final sourceId = await _oldestSourceId();
    if (sourceId == null) return null;

    final sampleRows =
        await (_db.select(_db.pendingSamples)
              ..where((t) => t.sourceId.equals(sourceId))
              ..orderBy([(t) => OrderingTerm.asc(t.id)])
              ..limit(limit))
            .get();
    final workoutRows =
        await (_db.select(_db.pendingWorkouts)
              ..where((t) => t.sourceId.equals(sourceId))
              ..orderBy([(t) => OrderingTerm.asc(t.id)])
              ..limit(workoutLimit))
            .get();

    final samples = [for (final r in sampleRows) _toPendingSample(r)];
    final workouts = [for (final r in workoutRows) _toPendingWorkout(r)];

    final totalForSource =
        await _countWhere(_db.pendingSamples, sourceId) +
        await _countWhere(_db.pendingWorkouts, sourceId);
    final drained = totalForSource == samples.length + workouts.length;

    return OutboxBatch(
      sourceId: sourceId,
      samples: samples,
      workouts: workouts,
      cursorAfter: drained ? _newestCursor(sampleRows, workoutRows) : null,
    );
  }

  Future<String?> _oldestSourceId() async {
    final s =
        await (_db.select(_db.pendingSamples)
              ..orderBy([(t) => OrderingTerm.asc(t.id)])
              ..limit(1))
            .getSingleOrNull();
    final w =
        await (_db.select(_db.pendingWorkouts)
              ..orderBy([(t) => OrderingTerm.asc(t.id)])
              ..limit(1))
            .getSingleOrNull();
    if (s == null) return w?.sourceId;
    if (w == null) return s.sourceId;
    return s.created.isAfter(w.created) ? w.sourceId : s.sourceId;
  }

  Future<int> _countWhere(TableInfo<Table, dynamic> table, String sourceId) {
    final column = table.columnsByName['source_id']!;
    final query = _db.selectOnly(table)
      ..addColumns([countAll()])
      ..where(column.equals(sourceId));
    return query.map((row) => row.read(countAll())!).getSingle();
  }

  /// Cursor of the row that was queued last: newest `created`, then highest
  /// id within that moment.
  static String? _newestCursor(
    List<PendingSampleRow> samples,
    List<PendingWorkoutRow> workouts,
  ) {
    DateTime? bestAt;
    int bestId = -1;
    String? cursor;
    void consider(DateTime at, int id, String? c) {
      final later =
          bestAt == null ||
          at.isAfter(bestAt!) ||
          (at.isAtSameMomentAs(bestAt!) && id > bestId);
      if (later) {
        bestAt = at;
        bestId = id;
        cursor = c;
      }
    }

    for (final s in samples) {
      consider(s.created, s.id, s.cursorAfter);
    }
    for (final w in workouts) {
      consider(w.created, w.id, w.cursorAfter);
    }
    return cursor;
  }

  @override
  Future<void> markSent({
    List<int> sampleIds = const [],
    List<int> workoutIds = const [],
  }) async {
    await _db.transaction(() async {
      if (sampleIds.isNotEmpty) {
        await (_db.delete(
          _db.pendingSamples,
        )..where((t) => t.id.isIn(sampleIds))).go();
      }
      if (workoutIds.isNotEmpty) {
        await (_db.delete(
          _db.pendingWorkouts,
        )..where((t) => t.id.isIn(workoutIds))).go();
      }
    });
  }

  @override
  Future<int> count() async {
    final s = await _countAll(_db.pendingSamples);
    final w = await _countAll(_db.pendingWorkouts);
    return s + w;
  }

  Future<int> _countAll(TableInfo<Table, dynamic> table) {
    final query = _db.selectOnly(table)..addColumns([countAll()]);
    return query.map((row) => row.read(countAll())!).getSingle();
  }

  @override
  Future<String?> readState(String key) async {
    final row = await (_db.select(
      _db.syncStates,
    )..where((t) => t.key.equals(key))).getSingleOrNull();
    return row?.value;
  }

  @override
  Future<void> writeState(String key, String? value) async {
    if (value == null) {
      await (_db.delete(_db.syncStates)..where((t) => t.key.equals(key))).go();
      return;
    }
    await _db
        .into(_db.syncStates)
        .insertOnConflictUpdate(
          SyncStatesCompanion.insert(key: key, value: Value(value)),
        );
  }

  @override
  Future<void> close() => _db.close();

  static PendingSample _toPendingSample(PendingSampleRow r) => PendingSample(
    id: r.id,
    sourceId: r.sourceId,
    cursorAfter: r.cursorAfter,
    created: r.created,
    sample: Sample(
      sourceUid: r.sourceUid,
      metric: r.metric,
      start: r.start,
      end: r.end,
      value: r.value,
      unit: r.unit,
      sourceId: r.sourceId,
      meta: _decodeObject(r.meta) ?? const {},
    ),
  );

  static PendingWorkout _toPendingWorkout(PendingWorkoutRow r) =>
      PendingWorkout(
        id: r.id,
        sourceId: r.sourceId,
        cursorAfter: r.cursorAfter,
        created: r.created,
        workout: Workout(
          sourceUid: r.sourceUid,
          type: r.type,
          start: r.start,
          end: r.end,
          distanceM: r.distanceM,
          energyKcal: r.energyKcal,
          avgHr: r.avgHr,
          maxHr: r.maxHr,
          routeGeoJson: _decodeObject(r.routeGeoJson),
          meta: _decodeObject(r.meta) ?? const {},
        ),
      );

  static Map<String, dynamic>? _decodeObject(String? text) {
    if (text == null || text.isEmpty) return null;
    try {
      final decoded = jsonDecode(text);
      if (decoded is Map<String, dynamic>) return decoded;
      if (decoded is Map) return Map<String, dynamic>.from(decoded);
    } on FormatException {
      // A damaged meta column should not block the whole queue.
    }
    return null;
  }
}
