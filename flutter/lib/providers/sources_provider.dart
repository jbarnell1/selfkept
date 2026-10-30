import 'package:flutter/foundation.dart';

import '../models/source.dart';
import '../services/sources_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The person's registered sources and what the server knows about each.
class SourcesProvider extends ChangeNotifier with SafeNotifier {
  final SourcesService _service;
  final AuthProvider _auth;

  List<Source> _sources = const [];
  bool _loading = false;
  bool _hasLoaded = false;
  bool _busy = false;
  String? _error;

  SourcesProvider({required this._service, required this._auth}) {
    _auth.addListener(_onAuthChanged);
  }

  List<Source> get sources => _sources;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  bool get isBusy => _busy;
  String? get error => _error;

  Source? byId(String id) {
    for (final s in _sources) {
      if (s.id == id) return s;
    }
    return null;
  }

  /// The most recent server-side sync across all sources.
  DateTime? get lastSyncAt {
    DateTime? best;
    for (final s in _sources) {
      final at = s.lastSyncAt;
      if (at != null && (best == null || at.isAfter(best))) best = at;
    }
    return best;
  }

  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      _sources = await _service.list();
      _hasLoaded = true;
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// Registers this device install as a source (or fetches the existing one)
  /// and returns it. Null when the server refused; [error] says why.
  Future<Source?> register({
    required SourceKind kind,
    required String deviceName,
    required String platform,
    required String appVersion,
  }) async {
    Source? created;
    final ok = await _run(() async {
      created = await _service.create(
        kind: kind,
        deviceName: deviceName,
        platform: platform,
        appVersion: appVersion,
      );
      _upsert(created!);
    });
    return ok ? created : null;
  }

  /// Optimistic: the switch moves at once and moves back on refusal.
  Future<bool> setEnabled(String id, bool enabled) async {
    final before = byId(id);
    if (before == null) return false;
    _upsert(before.copyWith(enabled: enabled));
    _error = null;
    notify();
    try {
      final updated = await _service.update(id, enabled: enabled);
      _upsert(updated);
      notify();
      return true;
    } catch (e) {
      _upsert(before);
      _error = describeError(e);
      notify();
      return false;
    }
  }

  Future<bool> remove(String id) => _run(() async {
    await _service.delete(id);
    _sources = [
      for (final s in _sources)
        if (s.id != id) s,
    ];
  });

  /// Called by the sync engine after an accepted batch so the screen shows
  /// the new cursor and counts without a refetch.
  void applyFlush(
    String sourceId, {
    String? cursor,
    int accepted = 0,
    required DateTime at,
  }) {
    final current = byId(sourceId);
    if (current == null) return;
    _upsert(
      current.copyWith(
        cursor: cursor,
        lastSyncAt: at,
        lastSampleAt: accepted > 0 ? at : null,
        sampleCount: current.sampleCount + accepted,
      ),
    );
    notify();
  }

  void clearError() {
    if (_error == null) return;
    _error = null;
    notify();
  }

  void _upsert(Source source) {
    final index = _sources.indexWhere((s) => s.id == source.id);
    final next = List.of(_sources);
    if (index < 0) {
      next.add(source);
    } else {
      next[index] = source;
    }
    _sources = next;
  }

  Future<bool> _run(Future<void> Function() action) async {
    _busy = true;
    _error = null;
    notify();
    try {
      await action();
      return true;
    } catch (e) {
      _error = describeError(e);
      return false;
    } finally {
      _busy = false;
      notify();
    }
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || !_hasLoaded) return;
    _sources = const [];
    _hasLoaded = false;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
