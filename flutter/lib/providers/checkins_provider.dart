import 'package:flutter/foundation.dart';

import '../models/checkin.dart';
import '../services/checkins_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// Recent check-ins and the one being kept right now. Keeping is optimistic:
/// the entry shows under Today at once and is replaced by the server's copy,
/// or removed with a message if the server refused.
class CheckinsProvider extends ChangeNotifier with SafeNotifier {
  final CheckinsService _service;
  final AuthProvider _auth;
  final DateTime Function() _clock;

  List<Checkin> _recent = const [];
  bool _loading = false;
  bool _hasLoaded = false;
  bool _saving = false;
  bool _justKept = false;
  int _keptCount = 0;
  String? _error;

  CheckinsProvider({
    required this._service,
    required this._auth,
    DateTime Function()? clock,
  }) : _clock = clock ?? (() => DateTime.now().toUtc()) {
    _auth.addListener(_onAuthChanged);
  }

  /// Newest first.
  List<Checkin> get recent => _recent;
  Checkin? get last => _recent.isEmpty ? null : _recent.first;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  bool get isSaving => _saving;

  /// True right after a successful keep until [acknowledge] is called, so the
  /// page can show "Kept." once.
  bool get justKept => _justKept;

  /// How many check-ins the server has accepted this session. Today watches
  /// it: a kept check-in changes the sentence, so Today reloads.
  int get keptCount => _keptCount;
  String? get error => _error;

  /// Loads the last [days] days.
  Future<void> load({int days = 14}) async {
    _loading = true;
    _error = null;
    notify();
    try {
      final now = _clock();
      _recent = await _service.list(
        from: now.subtract(Duration(days: days)),
        to: now.add(const Duration(days: 1)),
      );
      _hasLoaded = true;
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// Keeps a check-in. True when the server accepted it.
  Future<bool> create({required int mood, String? note}) async {
    final placeholder = Checkin(
      id: 'pending-${_clock().microsecondsSinceEpoch}',
      personId: _auth.user?.person.id ?? '',
      at: _clock(),
      mood: mood,
      note: note == null || note.trim().isEmpty ? null : note.trim(),
    );
    _recent = [placeholder, ..._recent];
    _saving = true;
    _justKept = false;
    _error = null;
    notify();
    try {
      final saved = await _service.create(mood: mood, note: note);
      _recent = [for (final c in _recent) c.id == placeholder.id ? saved : c];
      _justKept = true;
      _keptCount += 1;
      return true;
    } catch (e) {
      _recent = [
        for (final c in _recent)
          if (c.id != placeholder.id) c,
      ];
      _error = describeError(e);
      return false;
    } finally {
      _saving = false;
      notify();
    }
  }

  Future<bool> delete(String id) async {
    final before = _recent;
    _recent = [
      for (final c in _recent)
        if (c.id != id) c,
    ];
    _error = null;
    notify();
    try {
      await _service.delete(id);
      return true;
    } catch (e) {
      _recent = before;
      _error = describeError(e);
      notify();
      return false;
    }
  }

  /// Clears the one-shot "Kept." flag.
  void acknowledge() {
    if (!_justKept) return;
    _justKept = false;
    notify();
  }

  void clearError() {
    if (_error == null) return;
    _error = null;
    notify();
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || !_hasLoaded) return;
    _recent = const [];
    _hasLoaded = false;
    _justKept = false;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
