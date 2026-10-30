import '../models/sample.dart';

/// Default page size when flushing. The contract allows 5,000; staying well
/// under it keeps a request small on a slow tailnet link.
const int defaultOutboxBatchSize = 2000;

/// What one collect produced, ready to be queued for one server source.
class CollectedBatch {
  final String sourceId;
  final List<Sample> samples;
  final List<Workout> workouts;

  /// The collector cursor after this collect. Null when the collector has no
  /// resume token (a manual entry, for example).
  final String? cursorAfter;

  const CollectedBatch({
    required this.sourceId,
    this.samples = const [],
    this.workouts = const [],
    this.cursorAfter,
  });

  bool get isEmpty => samples.isEmpty && workouts.isEmpty;
  int get length => samples.length + workouts.length;
}

/// A queued sample with its local row id.
class PendingSample {
  final int id;
  final String sourceId;
  final Sample sample;
  final String? cursorAfter;
  final DateTime created;

  const PendingSample({
    required this.id,
    required this.sourceId,
    required this.sample,
    this.cursorAfter,
    required this.created,
  });
}

/// A queued workout with its local row id.
class PendingWorkout {
  final int id;
  final String sourceId;
  final Workout workout;
  final String? cursorAfter;
  final DateTime created;

  const PendingWorkout({
    required this.id,
    required this.sourceId,
    required this.workout,
    this.cursorAfter,
    required this.created,
  });
}

/// One page of the outbox for one source, in insertion order.
class OutboxBatch {
  final String sourceId;
  final List<PendingSample> samples;
  final List<PendingWorkout> workouts;

  /// Cursor to send with this page, or null when rows from the same collect
  /// are still queued behind it (the engine then repeats the last known
  /// cursor so the server never skips ahead).
  final String? cursorAfter;

  const OutboxBatch({
    required this.sourceId,
    this.samples = const [],
    this.workouts = const [],
    this.cursorAfter,
  });

  bool get isEmpty => samples.isEmpty && workouts.isEmpty;
  int get length => samples.length + workouts.length;
  List<int> get sampleIds => [for (final s in samples) s.id];
  List<int> get workoutIds => [for (final w in workouts) w.id];
}

/// Durable queue between the collectors and POST /ingest/batch. Rows are
/// removed only by [markSent]; nothing else deletes.
abstract class OutboxRepository {
  Future<void> enqueue(CollectedBatch batch);

  /// The oldest pending rows for one source, at most [limit] samples and up
  /// to [workoutLimit] workouts. Null when nothing is pending.
  Future<OutboxBatch?> nextBatch({
    int limit = defaultOutboxBatchSize,
    int workoutLimit = 200,
  });

  Future<void> markSent({
    List<int> sampleIds = const [],
    List<int> workoutIds = const [],
  });

  /// Pending samples plus pending workouts.
  Future<int> count();

  /// Collector state. Null when the key was never written.
  Future<String?> readState(String key);
  Future<void> writeState(String key, String? value);

  Future<void> close();
}

/// In-memory outbox: the web build (which never collects) and tests.
class MemoryOutboxRepository implements OutboxRepository {
  final List<PendingSample> _samples = [];
  final List<PendingWorkout> _workouts = [];
  final Map<String, String?> _state = {};
  final DateTime Function() _clock;
  int _nextId = 1;

  MemoryOutboxRepository({DateTime Function()? clock})
    : _clock = clock ?? (() => DateTime.now().toUtc());

  @override
  Future<void> enqueue(CollectedBatch batch) async {
    final now = _clock();
    for (final s in batch.samples) {
      _samples.add(
        PendingSample(
          id: _nextId++,
          sourceId: batch.sourceId,
          sample: s,
          cursorAfter: batch.cursorAfter,
          created: now,
        ),
      );
    }
    for (final w in batch.workouts) {
      _workouts.add(
        PendingWorkout(
          id: _nextId++,
          sourceId: batch.sourceId,
          workout: w,
          cursorAfter: batch.cursorAfter,
          created: now,
        ),
      );
    }
  }

  @override
  Future<OutboxBatch?> nextBatch({
    int limit = defaultOutboxBatchSize,
    int workoutLimit = 200,
  }) async {
    final sourceId = _oldestSourceId();
    if (sourceId == null) return null;
    final samples = [
      for (final s in _samples)
        if (s.sourceId == sourceId) s,
    ].take(limit).toList();
    final workouts = [
      for (final w in _workouts)
        if (w.sourceId == sourceId) w,
    ].take(workoutLimit).toList();
    final remaining =
        _samples.where((s) => s.sourceId == sourceId).length -
        samples.length +
        _workouts.where((w) => w.sourceId == sourceId).length -
        workouts.length;
    return OutboxBatch(
      sourceId: sourceId,
      samples: samples,
      workouts: workouts,
      cursorAfter: remaining == 0 ? newestCursor(samples, workouts) : null,
    );
  }

  String? _oldestSourceId() {
    PendingSample? s = _samples.isEmpty ? null : _samples.first;
    PendingWorkout? w = _workouts.isEmpty ? null : _workouts.first;
    if (s == null) return w?.sourceId;
    if (w == null) return s.sourceId;
    return s.id < w.id ? s.sourceId : w.sourceId;
  }

  /// The cursor of the most recently queued row in a page.
  static String? newestCursor(
    List<PendingSample> samples,
    List<PendingWorkout> workouts,
  ) {
    int bestId = -1;
    String? cursor;
    for (final s in samples) {
      if (s.id > bestId) {
        bestId = s.id;
        cursor = s.cursorAfter;
      }
    }
    for (final w in workouts) {
      if (w.id > bestId) {
        bestId = w.id;
        cursor = w.cursorAfter;
      }
    }
    return cursor;
  }

  @override
  Future<void> markSent({
    List<int> sampleIds = const [],
    List<int> workoutIds = const [],
  }) async {
    final s = sampleIds.toSet();
    final w = workoutIds.toSet();
    _samples.removeWhere((row) => s.contains(row.id));
    _workouts.removeWhere((row) => w.contains(row.id));
  }

  @override
  Future<int> count() async => _samples.length + _workouts.length;

  @override
  Future<String?> readState(String key) async => _state[key];

  @override
  Future<void> writeState(String key, String? value) async {
    if (value == null) {
      _state.remove(key);
    } else {
      _state[key] = value;
    }
  }

  @override
  Future<void> close() async {}
}
