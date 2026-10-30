import 'dart:async';

import 'package:flutter/foundation.dart';

import '../providers/sources_provider.dart';
import '../services/ingest_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'outbox_repository.dart';

/// Moves the outbox to the server. One flush at a time; a request to flush
/// while one runs is remembered and honoured when the current pass ends.
///
/// Rows are deleted only after a 200. Any failure, network or server, leaves
/// them in place and stops the pass; the next flush picks up where it was.
class SyncEngine extends ChangeNotifier with SafeNotifier {
  final IngestService _ingest;
  final OutboxRepository _outbox;
  final SourcesProvider _sources;
  final int _batchSize;
  final DateTime Function() _clock;

  int _pendingCount = 0;
  DateTime? _lastFlushAt;
  String? _lastError;
  int _lastRejected = 0;
  bool _flushing = false;
  bool _flushAgain = false;
  Future<bool>? _inFlight;

  SyncEngine({
    required this._ingest,
    required this._outbox,
    required this._sources,
    this._batchSize = defaultOutboxBatchSize,
    DateTime Function()? clock,
  }) : _clock = clock ?? (() => DateTime.now().toUtc());

  /// Rows waiting in the outbox, as of the last count.
  int get pendingCount => _pendingCount;

  /// When the last batch was accepted. Null until one has been.
  DateTime? get lastFlushAt => _lastFlushAt;

  /// Why the last pass stopped, or null when it finished cleanly.
  String? get lastError => _lastError;

  /// Rows the server refused in the last accepted batch (unknown metric, bad
  /// unit). They are not retried.
  int get lastRejected => _lastRejected;
  bool get isFlushing => _flushing;

  /// Re-reads the outbox size, for the screen.
  Future<void> refreshPending() async {
    _pendingCount = await _outbox.count();
    notify();
  }

  /// Queues a batch and flushes it.
  Future<bool> enqueueAndFlush(CollectedBatch batch) async {
    if (!batch.isEmpty) await _outbox.enqueue(batch);
    return flush();
  }

  /// Sends everything pending. True when the outbox is empty afterwards.
  Future<bool> flush() {
    final running = _inFlight;
    if (running != null) {
      _flushAgain = true;
      return running;
    }
    final future = _runFlush();
    _inFlight = future;
    unawaited(future.whenComplete(() => _inFlight = null));
    return future;
  }

  Future<bool> _runFlush() async {
    _flushing = true;
    _lastError = null;
    notify();
    try {
      do {
        _flushAgain = false;
        final clean = await _drain();
        if (!clean) return false;
      } while (_flushAgain);
      return true;
    } finally {
      _flushing = false;
      _pendingCount = await _outbox.count();
      notify();
    }
  }

  /// One pass over the outbox. False when a batch failed.
  Future<bool> _drain() async {
    while (true) {
      final batch = await _outbox.nextBatch(limit: _batchSize);
      if (batch == null || batch.isEmpty) return true;
      final source = _sources.byId(batch.sourceId);
      final cursor = batch.cursorAfter ?? source?.cursor;
      try {
        final result = await _ingest.postBatch(
          sourceId: batch.sourceId,
          cursor: cursor,
          samples: [for (final s in batch.samples) s.sample],
          workouts: [for (final w in batch.workouts) w.workout],
        );
        await _outbox.markSent(
          sampleIds: batch.sampleIds,
          workoutIds: batch.workoutIds,
        );
        final now = _clock();
        _lastFlushAt = now;
        _lastRejected = result.rejected.length;
        _sources.applyFlush(
          batch.sourceId,
          cursor: result.cursor ?? cursor,
          accepted: result.accepted,
          at: now,
        );
        _pendingCount = await _outbox.count();
        notify();
      } catch (e) {
        _lastError = describeError(e);
        return false;
      }
    }
  }
}
